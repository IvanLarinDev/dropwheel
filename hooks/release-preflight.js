#!/usr/bin/env node
// release-preflight.js - executable release gate for the harness release loop.
//
// Checks the risky parts that are easy to miss by prose:
//   - clean worktree;
//   - prepare mode: release HEAD is based on the configured base ref;
//   - post-merge mode: the release tag commit is already included in the base ref;
//   - the local release tag exists and points at HEAD;
//   - the remote tag does not already exist;
//   - project manifest versions match the release tag.
//
// Usage:
//   node hooks/release-preflight.js --tag v1.2.3 [--root <dir>] [--base origin/main] [--json]
//     --allow-dirty       do not fail on dirty worktree
//     --allow-missing-tag do not require a local tag at HEAD yet
//     --allow-remote-tag  allow the tag to already exist on origin
//     --require-tag-in-base require the tag commit to be an ancestor of --base

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const SKIP_DIRS = new Set([".git", "node_modules", "target", "bin", "obj", "dist", "build", ".venv", "venv", "__pycache__", ".next"]);
const MAX_DEPTH = 6;

function parseArgs(argv) {
  const a = {
    root: process.cwd(),
    base: "origin/main",
    tag: "",
    json: false,
    allowDirty: false,
    allowMissingTag: false,
    allowRemoteTag: false,
    requireTagInBase: false,
  };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--base") a.base = argv[++i];
    else if (argv[i] === "--tag") a.tag = argv[++i];
    else if (argv[i] === "--json") a.json = true;
    else if (argv[i] === "--allow-dirty") a.allowDirty = true;
    else if (argv[i] === "--allow-missing-tag") a.allowMissingTag = true;
    else if (argv[i] === "--allow-remote-tag") a.allowRemoteTag = true;
    else if (argv[i] === "--require-tag-in-base") a.requireTagInBase = true;
  }
  a.root = path.resolve(a.root);
  return a;
}

function git(root, args) {
  return execFileSync("git", args, {
    cwd: root,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    timeout: args[0] === "ls-remote" ? 60000 : 10000,
    killSignal: "SIGKILL",
  }).trim();
}
function gitOk(root, args) {
  try { git(root, args); return true; } catch { return false; }
}
function gitOut(root, args) {
  try { return git(root, args); } catch { return ""; }
}
function gitResult(root, args) {
  try { return { ok: true, out: git(root, args) }; }
  catch (e) { return { ok: false, out: String((e && e.stderr) || (e && e.message) || "") }; }
}

function add(res, level, msg, extra = {}) {
  res.results.push({ level, msg, ...extra });
}
function fail(res, msg, extra) { add(res, "FAIL", msg, extra); }
function warn(res, msg, extra) { add(res, "WARN", msg, extra); }
function pass(res, msg, extra) { add(res, "PASS", msg, extra); }

function semverFromTag(tag) {
  const m = String(tag || "").match(/^v(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)$/);
  return m ? m[1] : "";
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

function firstMatch(text, re) {
  const m = re.exec(text);
  return m ? m[1] : "";
}

function tomlSection(text, name) {
  const lines = String(text || "").split(/\r?\n/);
  const out = [];
  let inSection = false;
  for (const line of lines) {
    const sec = line.match(/^\s*\[([^\]]+)\]\s*$/);
    if (sec) {
      if (inSection) break;
      inSection = sec[1] === name;
      continue;
    }
    if (inSection) out.push(line);
  }
  return out.join("\n");
}

function packageJsonVersion(abs, rel) {
  if (path.basename(rel) !== "package.json") return null;
  try {
    const j = JSON.parse(fs.readFileSync(abs, "utf8"));
    return typeof j.version === "string" && j.version ? { rel, kind: "package.json", version: j.version } : null;
  } catch { return null; }
}

function csprojVersion(abs, rel) {
  if (!rel.toLowerCase().endsWith(".csproj")) return null;
  let text = "";
  try { text = fs.readFileSync(abs, "utf8"); } catch { return null; }
  const explicitVersion = firstMatch(text, /<Version>\s*([^<\s]+)\s*<\/Version>/i);
  const versionPrefix = firstMatch(text, /<VersionPrefix>\s*([^<\s]+)\s*<\/VersionPrefix>/i);
  const versionSuffix = firstMatch(text, /<VersionSuffix>\s*([^<\s]+)\s*<\/VersionSuffix>/i);
  const version = explicitVersion || (versionPrefix ? (versionSuffix ? `${versionPrefix}-${versionSuffix}` : versionPrefix) : "");
  return version ? { rel, kind: "csproj", version } : null;
}

function cargoVersion(abs, rel) {
  if (path.basename(rel) !== "Cargo.toml") return null;
  let text = "";
  try { text = fs.readFileSync(abs, "utf8"); } catch { return null; }
  const version = firstMatch(tomlSection(text, "package"), /^\s*version\s*=\s*"([^"]+)"/m);
  return version ? { rel, kind: "Cargo.toml", version } : null;
}

function pyprojectVersion(abs, rel) {
  if (path.basename(rel) !== "pyproject.toml") return null;
  let text = "";
  try { text = fs.readFileSync(abs, "utf8"); } catch { return null; }
  const version = firstMatch(tomlSection(text, "project"), /^\s*version\s*=\s*"([^"]+)"/m);
  return version ? { rel, kind: "pyproject.toml", version } : null;
}

function collectVersions(root) {
  const versions = [];
  walk(root, (abs, rel) => {
    const hit = csprojVersion(abs, rel) || packageJsonVersion(abs, rel) || cargoVersion(abs, rel) || pyprojectVersion(abs, rel);
    if (hit) versions.push(hit);
  });
  return versions;
}

function checkGitState(a, res) {
  if (!gitOk(a.root, ["rev-parse", "--is-inside-work-tree"])) {
    fail(res, "not a git repository");
    return;
  }
  pass(res, "git repository detected");

  const dirty = gitOut(a.root, ["status", "--porcelain"]);
  if (dirty && !a.allowDirty) fail(res, "worktree is dirty", { details: dirty.split(/\r?\n/).filter(Boolean).slice(0, 20) });
  else if (dirty) warn(res, "worktree is dirty but allowed", { details: dirty.split(/\r?\n/).filter(Boolean).slice(0, 20) });
  else pass(res, "worktree is clean");

  if (a.base) {
    if (!gitOk(a.root, ["rev-parse", "--verify", "--quiet", a.base])) {
      fail(res, `base ref not found: ${a.base}`);
    } else if (a.requireTagInBase) {
      const tagRef = a.tag ? `refs/tags/${a.tag}^{}` : "";
      if (!tagRef || !gitOk(a.root, ["rev-parse", "--verify", "--quiet", tagRef])) {
        fail(res, `cannot verify tag ancestry in ${a.base}: local tag is missing`);
      } else if (gitOk(a.root, ["merge-base", "--is-ancestor", tagRef, a.base])) {
        pass(res, `tag ${a.tag} is included in ${a.base}`);
      } else {
        fail(res, `tag ${a.tag} is not included in ${a.base}`);
      }
    } else if (gitOk(a.root, ["merge-base", "--is-ancestor", a.base, "HEAD"])) {
      pass(res, `HEAD is based on ${a.base}`);
    } else {
      fail(res, `HEAD is not based on ${a.base}`);
    }
  }
}

function checkTag(a, res, version) {
  if (!a.tag) {
    fail(res, "release tag is required; pass --tag vX.Y.Z");
    return;
  }
  if (!version) {
    fail(res, `tag must look like vX.Y.Z: ${a.tag}`);
    return;
  }
  pass(res, `tag format ok: ${a.tag}`);

  const head = gitOut(a.root, ["rev-parse", "HEAD"]);
  const localTag = gitOut(a.root, ["rev-parse", "-q", "--verify", `refs/tags/${a.tag}^{}`]);
  if (!localTag) {
    if (a.allowMissingTag) warn(res, `local tag is missing but allowed: ${a.tag}`);
    else fail(res, `local tag is missing: ${a.tag}`);
  } else if (localTag !== head) {
    fail(res, `local tag ${a.tag} does not point at HEAD`, { tag: localTag, head });
  } else {
    pass(res, `local tag ${a.tag} points at HEAD`);
  }
  const tagType = gitOut(a.root, ["cat-file", "-t", `refs/tags/${a.tag}`]);
  if (!tagType) {
    if (!a.allowMissingTag) fail(res, `cannot inspect local tag object: ${a.tag}`);
  } else if (tagType !== "tag") {
    fail(res, `local tag ${a.tag} must be annotated`, { type: tagType });
  } else {
    pass(res, `local tag ${a.tag} is annotated`);
  }

  const remote = gitResult(a.root, ["remote", "get-url", "origin"]);
  if (!remote.ok || !remote.out) {
    fail(res, "origin remote is not configured; cannot verify remote tag state");
    return;
  }
  const remoteTag = gitResult(a.root, ["ls-remote", "--tags", "origin", `refs/tags/${a.tag}`]);
  if (!remoteTag.ok) fail(res, `cannot verify remote tag state for ${a.tag}`);
  else if (remoteTag.out && !a.allowRemoteTag) fail(res, `remote tag already exists: ${a.tag}`);
  else if (remoteTag.out) warn(res, `remote tag already exists but allowed: ${a.tag}`);
  else pass(res, `remote tag does not exist yet: ${a.tag}`);
}

function checkVersions(a, res, version) {
  if (!version) return;
  const versions = collectVersions(a.root);
  if (!versions.length) {
    warn(res, "no project version manifests found");
    return;
  }
  const mismatches = versions.filter((v) => v.version !== version);
  if (mismatches.length) {
    fail(res, `project version(s) do not match ${a.tag}`, { expected: version, mismatches });
  } else {
    pass(res, `project version manifests match ${a.tag}`, { count: versions.length });
  }
}

function checkChangelog(a, res) {
  const p = path.join(a.root, "CHANGELOG.md");
  let text = "";
  try { text = fs.readFileSync(p, "utf8"); } catch { fail(res, "CHANGELOG.md not found"); return; }
  if (text.includes(a.tag) || text.includes(semverFromTag(a.tag))) pass(res, "CHANGELOG.md mentions the release version");
  else fail(res, "CHANGELOG.md does not mention the release version");
}

function main() {
  const a = parseArgs(process.argv.slice(2));
  const res = {
    ok: true,
    tag: a.tag,
    root: a.root,
    mode: a.requireTagInBase ? "post-merge" : "prepare",
    results: [],
  };
  const version = semverFromTag(a.tag);

  checkGitState(a, res);
  checkTag(a, res, version);
  checkVersions(a, res, version);
  checkChangelog(a, res);

  res.ok = !res.results.some((r) => r.level === "FAIL");
  if (a.json) {
    console.log(JSON.stringify(res));
  } else {
    const icon = { PASS: "+", WARN: "!", FAIL: "X" };
    console.log(`release preflight: ${a.tag || "(no tag)"}`);
    for (const r of res.results) {
      console.log(`  ${icon[r.level]} ${r.msg}`);
      if (r.mismatches) for (const m of r.mismatches) console.log(`    ${m.rel}: ${m.version} (${m.kind})`);
      if (r.details) for (const d of r.details) console.log(`    ${d}`);
    }
    console.log(res.ok ? "\nrelease preflight passed." : "\nrelease preflight failed.");
  }
  process.exit(res.ok ? 0 : 1);
}

if (require.main === module) main();
