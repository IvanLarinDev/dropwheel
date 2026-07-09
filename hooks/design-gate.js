#!/usr/bin/env node
// design-gate.js - DESIGN-stage gate.
//
// Policy: GUI work must be preceded by design review. If a branch's changes touch UI
// paths, the SAME branch diff must also touch an APPROVED set of >= N mockups -
// otherwise one old approval would open the gate for all future UI work forever.
//
// Usage:
//   node hooks/design-gate.js [--base <ref>] [--root <dir>] [--files a,b,c] [--json] [--strict]
//     --base   git ref to diff against (default: origin/main if available) [CI/local]
//     --files  explicit comma-separated changed files          [tests/CI]
//     --root   repo root to resolve config + mockups (default: cwd)
//     --strict diff errors fail closed (CI/server enforcement)
//
// Exit 0 = gate satisfied (or no UI change), exit 1 = UI changed without approved mockups.
// Internal error -> exit 0 locally (never wedge unrelated work), with a loud warning.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

// ---------- args ----------
function parseArgs(argv) {
  const a = { base: null, root: process.cwd(), files: null, json: false, strict: false };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--base") a.base = argv[++i];
    else if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--files") a.files = (argv[++i] || "").split(",").map((s) => s.trim()).filter(Boolean);
    else if (argv[i] === "--json") a.json = true;
    else if (argv[i] === "--strict") a.strict = true;
  }
  return a;
}

function refExists(root, ref) {
  try {
    execFileSync("git", ["rev-parse", "--verify", "--quiet", ref], {
      cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
      timeout: 5000, killSignal: "SIGKILL",
    });
    return true;
  } catch {
    return false;
  }
}
function defaultBase(root) {
  for (const ref of ["origin/main", "origin/HEAD", "main", "master"]) {
    if (refExists(root, ref)) return ref;
  }
  return "origin/main";
}

// ---------- config shared with guard.js and verify.js ----------
const { globToRe, loadConfig, changedFiles } = require(path.join(__dirname, "_lib.js"));

// ---------- mockups scan ----------
// An approved set counts only if that same set is touched in this branch diff.
// Otherwise one old approval would unlock future UI work forever.
function hasApprovedMockups(root, m, changed) {
  const base = path.join(root, m.dir);
  const mockRoot = m.dir.replace(/\\/g, "/").replace(/\/$/, "");
  let dirs;
  try { dirs = fs.readdirSync(base, { withFileTypes: true }).filter((d) => d.isDirectory()); }
  catch { return { ok: false, reason: `missing directory ${m.dir}/` }; }

  const stale = [];
  for (const d of dirs) {
    const dir = path.join(base, d.name);
    let files;
    try { files = fs.readdirSync(dir); } catch { continue; }
    const mockups = files.filter((f) => m.mockupExtensions.includes(path.extname(f).toLowerCase()));
    const approved = files.includes(m.approvalFile);
    if (mockups.length < m.min || !approved) continue;
    if (changed.some((c) => c.startsWith(`${mockRoot}/${d.name}/`)))
      return { ok: true, feature: d.name, count: mockups.length };
    stale.push(d.name);
  }
  return {
    ok: false,
    reason: stale.length
      ? `approved set(s) (${stale.join(", ")}) are not touched in this branch diff; ` +
        `touch ${m.dir}/<feature>/${m.approvalFile} to bind an existing approval to this change`
      : `no ${m.dir}/<feature>/ with >=${m.min} mockups and ${m.approvalFile} touched in this branch`,
  };
}

// ---------- main ----------
(function main() {
  const a = parseArgs(process.argv.slice(2));
  if (!a.base) a.base = defaultBase(a.root);
  const cfg = loadConfig(a.root);
  const res = { ok: true, base: a.base, uiChanged: [], mockups: null };

  const uiRes = cfg.uiGlobs.map(globToRe);
  const mockRoot = cfg.mockups.dir.replace(/\\/g, "/").replace(/\/$/, "");
  const cf = changedFiles(a.base, a.root, a.files);
  if (cf.base) res.base = cf.base;
  if (cf.error) {
    // Local default = fail-open loudly; strict/CI = fail-closed.
    const warn = `design-gate: ${cf.error}; ${a.strict ? "gate cannot verify UI changes" : "gate skipped and UI changes were not checked"}. Pass --base <ref>.`;
    if (a.json) console.log(JSON.stringify({ ...res, ok: !a.strict, skipped: true, warn }));
    else console.error(warn);
    process.exit(a.strict ? 1 : 0);
  }
  const files = cf.files.map((f) => f.replace(/\\/g, "/"));

  res.uiChanged = files.filter(
    (f) => !f.startsWith(mockRoot + "/") && uiRes.some((re) => re.test(f))
  );

  if (res.uiChanged.length === 0) {
    if (a.json) console.log(JSON.stringify({ ...res, note: "no UI-path changes" }));
    else console.log("OK design-gate: no UI-path changes; gate not required.");
    process.exit(0);
  }

  const mk = hasApprovedMockups(a.root, cfg.mockups, files);
  res.mockups = mk;
  if (mk.ok) {
    if (a.json) console.log(JSON.stringify(res));
    else console.log(`OK design-gate: UI changes have an approved mockup set touched in this branch (${mk.feature}, ${mk.count}).`);
    process.exit(0);
  }

  if (a.json) { console.log(JSON.stringify({ ...res, ok: false })); process.exit(1); }
  console.error(
    `BLOCK design-gate: GUI changes require DESIGN approval.\n` +
      `   UI files: ${res.uiChanged.slice(0, 8).join(", ")}${res.uiChanged.length > 8 ? " ..." : ""}\n` +
      `   Required: ${mk.reason}.\n` +
      `   New set: node hooks/new-mockups.js <feature>, get approval, then create ${cfg.mockups.dir}/<feature>/${cfg.mockups.approvalFile}.\n` +
      `   Existing set: touch its ${cfg.mockups.approvalFile} so it appears in this branch diff.\n` +
      `   Policy: new/changed GUI needs >=${cfg.mockups.min} stylistically distinct mockups plus approval.`
  );
  process.exit(1);
})();
