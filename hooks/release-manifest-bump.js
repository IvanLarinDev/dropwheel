#!/usr/bin/env node
// release-manifest-bump.js - synchronize project version manifests before cog tags.
//
// Usage:
//   node hooks/release-manifest-bump.js --tag v1.2.3 [--root <dir>] [--dry-run] [--json]
//   node hooks/release-manifest-bump.js --version 1.2.3 [--root <dir>] [--dry-run] [--json]

const fs = require("fs");
const path = require("path");

const SKIP_DIRS = new Set([".git", "node_modules", "target", "bin", "obj", "dist", "build", ".venv", "venv", "__pycache__", ".next"]);
const MAX_DEPTH = 6;

function parseArgs(argv) {
  const a = { root: process.cwd(), tag: "", version: "", dryRun: false, json: false };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--tag") a.tag = argv[++i];
    else if (argv[i] === "--version") a.version = argv[++i];
    else if (argv[i] === "--dry-run") a.dryRun = true;
    else if (argv[i] === "--json") a.json = true;
  }
  a.root = path.resolve(a.root);
  return a;
}

function semverFromTag(tag) {
  const m = String(tag || "").match(/^v(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)$/);
  return m ? m[1] : "";
}

function validVersion(version) {
  return /^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(String(version || ""));
}

function escapeRe(s) {
  return String(s).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function walk(root, visit) {
  function rec(dir, depth) {
    let entries;
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const e of entries) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) {
        if (depth < MAX_DEPTH && !SKIP_DIRS.has(e.name)) rec(p, depth + 1);
      } else if (e.isFile()) {
        visit(p, path.relative(root, p).replace(/\\/g, "/"));
      }
    }
  }
  rec(root, 0);
}

function replaceXmlVersion(text, version) {
  let from = "";
  let next = text.replace(/(<Version>\s*)([^<\s]+)(\s*<\/Version>)/i, (_m, pre, old, post) => {
    from = old;
    return pre + version + post;
  });
  if (from) return { next, from };
  next = text.replace(/(<VersionPrefix>\s*)([^<\s]+)(\s*<\/VersionPrefix>)/i, (_m, pre, old, post) => {
    from = old;
    return pre + version + post;
  });
  return from ? { next, from } : null;
}

function replaceTomlVersion(text, section, version) {
  const re = new RegExp(`(^\\s*\\[${escapeRe(section)}\\]\\s*$)([\\s\\S]*?)(?=^\\s*\\[[^\\]]+\\]\\s*$|\\s*$)`, "m");
  const m = re.exec(text);
  if (!m) return null;
  let from = "";
  const body = m[2].replace(/^(\s*version\s*=\s*")([^"]+)(".*)$/m, (_line, pre, old, post) => {
    from = old;
    return pre + version + post;
  });
  if (!from) return null;
  return { next: text.slice(0, m.index) + m[1] + body + text.slice(m.index + m[0].length), from };
}

function packageJsonUpdate(abs, version) {
  if (path.basename(abs) !== "package.json") return null;
  let text = "";
  try { text = fs.readFileSync(abs, "utf8"); } catch { return null; }
  let json;
  try { json = JSON.parse(text); } catch { return null; }
  if (typeof json.version !== "string" || !json.version) return null;
  const from = json.version;
  json.version = version;
  return { kind: "package.json", from, next: JSON.stringify(json, null, 2) + "\n" };
}

function textUpdate(abs, rel, version) {
  let text = "";
  try { text = fs.readFileSync(abs, "utf8"); } catch { return null; }
  if (rel.toLowerCase().endsWith(".csproj")) {
    const hit = replaceXmlVersion(text, version);
    return hit ? { kind: "csproj", from: hit.from, next: hit.next } : null;
  }
  if (path.basename(rel) === "Cargo.toml") {
    const hit = replaceTomlVersion(text, "package", version);
    return hit ? { kind: "Cargo.toml", from: hit.from, next: hit.next } : null;
  }
  if (path.basename(rel) === "pyproject.toml") {
    const hit = replaceTomlVersion(text, "project", version);
    return hit ? { kind: "pyproject.toml", from: hit.from, next: hit.next } : null;
  }
  return null;
}

function updateManifest(abs, rel, version, dryRun) {
  const hit = packageJsonUpdate(abs, version) || textUpdate(abs, rel, version);
  if (!hit) return null;
  const changed = hit.from !== version;
  if (changed && !dryRun) fs.writeFileSync(abs, hit.next);
  return { rel, kind: hit.kind, from: hit.from, to: version, changed };
}

function add(res, level, msg, extra = {}) {
  res.results.push({ level, msg, ...extra });
}
function fail(res, msg, extra) { add(res, "FAIL", msg, extra); }
function warn(res, msg, extra) { add(res, "WARN", msg, extra); }
function pass(res, msg, extra) { add(res, "PASS", msg, extra); }

function main() {
  const a = parseArgs(process.argv.slice(2));
  const version = a.version || semverFromTag(a.tag);
  const res = { ok: true, root: a.root, tag: a.tag, version, dryRun: a.dryRun, results: [], manifests: [] };

  if (!validVersion(version)) {
    fail(res, "release version is required; pass --tag vX.Y.Z or --version X.Y.Z");
  } else {
    walk(a.root, (abs, rel) => {
      try {
        const hit = updateManifest(abs, rel, version, a.dryRun);
        if (hit) res.manifests.push(hit);
      } catch (e) {
        fail(res, `could not update ${rel}: ${e.message || e}`);
      }
    });
    if (!res.manifests.length) {
      warn(res, "no project version manifests found");
    } else {
      const changed = res.manifests.filter((m) => m.changed);
      changed.length
        ? pass(res, `${a.dryRun ? "would update" : "updated"} ${changed.length}/${res.manifests.length} project version manifest(s)`)
        : pass(res, `project version manifests already match ${version}`);
    }
  }

  res.ok = !res.results.some((r) => r.level === "FAIL");
  if (a.json) {
    console.log(JSON.stringify(res));
  } else {
    const icon = { PASS: "+", WARN: "!", FAIL: "X" };
    console.log(`release manifest bump: ${version || "(missing version)"}`);
    for (const r of res.results) console.log(`  ${icon[r.level]} ${r.msg}`);
    for (const m of res.manifests) {
      console.log(`  ${m.changed ? "~" : "="} ${m.rel}: ${m.from} -> ${m.to} (${m.kind})`);
    }
    console.log(res.ok ? "\nrelease manifest bump passed." : "\nrelease manifest bump failed.");
  }
  process.exit(res.ok ? 0 : 1);
}

if (require.main === module) main();
