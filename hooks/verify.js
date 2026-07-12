#!/usr/bin/env node
// verify.js - executable, multi-stack VERIFY runner (BACKLOG P1-8).
//
// Makes AGENTS.md step 4 (VERIFY) an actual command instead of prose. Auto-detects
// which stacks live in the repo by marker files and runs lint -> build -> test for
// each, fail-fast, with warnings-as-errors defaults. Covers the user's stacks:
//   Python/Qt (ruff + pytest), C#/WPF (dotnet format/build -warnaserror/test),
//   Rust/Tauri (cargo fmt + clippy -D warnings + test; Tauri = node front + rust back),
//   Node (npm lint/build/test).
//
// Usage:
//   node hooks/verify.js [--mode fast|full|release] [--root <dir>] [--stack <id>] [--changed [--base <ref>]] [--list] [--json] [--strict-audit]
//     --list     detect + print the plan, do not run
//     --stack    run only the named stack
//     --root     repo root (default: cwd)
//     --changed  verify only stacks whose dir is touched in the branch diff (faster inner loop)
//     --base     git ref to diff against for --changed (default: main; fallback master/origin/HEAD)
//     --files    explicit comma-separated changed files (tests/CI; bypasses git)
//     --check-harness-syntax  internal lightweight JS syntax check for harness files
//     --strict-audit  fail VERIFY if the debug-audit cannot compute its changed-file scope
//   --changed fail-safe: if the diff can't be computed, verify ALL stacks (loud warn),
//   never silently skip verification; empty diff = nothing to verify.
//
// Config: harness.config.json -> "verify". If "verify.stacks" is present it REPLACES
// the auto-detected defaults (explicit control); otherwise DEFAULT_STACKS are used.
// Per-step: optional (missing tool -> skip only), okCodes { "<exit>": "note" }
// - explicit non-zero exits that are warnings, not failures (e.g. pytest 5 = no tests collected).
//
// Debug audit: runs alongside stack checks and scans only changed files for
// leftover debug statements. Hard markers fail VERIFY; soft markers are notes
// only when harness.config.json -> debugAudit.soft is true.
//
// Exit 0 = all steps passed (or nothing to verify), exit 1 = a required step failed
//          or the debug audit found a hard marker.

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");
const { performance } = require("perf_hooks");
const { loadDebugAudit, debugAudit, loadStacks, planVerifyTargets } = require(path.join(__dirname, "verify-core.js"));
const DEFAULT_STEP_TIMEOUT_MS = 15 * 60 * 1000;

// ---------- args ----------
function parseArgs(argv) {
  const a = { root: process.cwd(), stack: null, mode: "full", list: false, json: false, changed: false, base: "main", files: null, checkHarnessSyntax: false, strictAudit: false, errors: [] };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--stack") a.stack = argv[++i];
    else if (argv[i] === "--mode") a.mode = String(argv[++i] || "");
    else if (argv[i] === "--fast") a.mode = "fast";
    else if (argv[i] === "--list") a.list = true;
    else if (argv[i] === "--json") a.json = true;
    else if (argv[i] === "--changed") a.changed = true;
    else if (argv[i] === "--base") a.base = argv[++i];
    else if (argv[i] === "--files") a.files = (argv[++i] || "").split(",").map((s) => s.trim()).filter(Boolean);
    else if (argv[i] === "--check-harness-syntax") a.checkHarnessSyntax = true;
    else if (argv[i] === "--strict-audit") a.strictAudit = true;
  }
  if (!["fast", "full", "release"].includes(a.mode)) a.errors.push("--mode must be fast, full, or release");
  if (a.mode === "fast") a.changed = true;
  if (a.mode === "release") a.strictAudit = true;
  return a;
}

function listHarnessJs(root) {
  const out = [];
  function walk(dir) {
    let entries;
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const e of entries) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.isFile() && e.name.endsWith(".js")) out.push(p);
    }
  }
  walk(path.join(root, "hooks"));
  for (const rel of ["install.js"]) {
    const p = path.join(root, rel);
    try { if (fs.statSync(p).isFile()) out.push(p); } catch {}
  }
  return out;
}

function checkHarnessSyntax(root) {
  const files = listHarnessJs(root);
  let failed = false;
  for (const file of files) {
    const r = spawnSync(process.execPath, ["--check", file], { cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], timeout: 30 * 1000, killSignal: "SIGKILL" });
    if (r.status !== 0 || r.error) {
      failed = true;
      const rel = path.relative(root, file) || file;
      process.stderr.write(`syntax failed: ${rel}\n`);
      if (r.stdout) process.stdout.write(r.stdout);
      if (r.stderr) process.stderr.write(r.stderr);
      if (r.error) process.stderr.write(String(r.error.message || r.error) + "\n");
    }
  }
  if (!files.length) console.log("harness syntax: no JS files found");
  else console.log(`harness syntax: checked ${files.length} JS file(s)`);
  process.exit(failed ? 1 : 0);
}

// ---------- run ----------
function stepTimeoutMs(step) {
  const raw = step.timeoutMs !== undefined ? step.timeoutMs : step.timeout;
  if (raw === undefined) return DEFAULT_STEP_TIMEOUT_MS;
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? n : DEFAULT_STEP_TIMEOUT_MS;
}
function stepCwd(step, cwd) {
  if (!step.cwdRel) return cwd;
  return path.resolve(cwd, step.cwdRel);
}
function formatDuration(ms) {
  const safe = Number.isFinite(ms) ? Math.max(0, ms) : 0;
  return safe < 1000 ? `${safe.toFixed(1)}ms` : `${(safe / 1000).toFixed(2)}s`;
}
function runStep(step, cwd) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "harness-verify-step-"));
  const stdoutPath = path.join(dir, "stdout.log");
  const stderrPath = path.join(dir, "stderr.log");
  const outFd = fs.openSync(stdoutPath, "w");
  const errFd = fs.openSync(stderrPath, "w");
  let r;
  const startedAt = performance.now();
  try {
    r = spawnSync(step.run, { cwd: stepCwd(step, cwd), shell: true, stdio: ["ignore", outFd, errFd], timeout: stepTimeoutMs(step), killSignal: "SIGKILL" });
  } finally {
    try { fs.closeSync(outFd); } catch {}
    try { fs.closeSync(errFd); } catch {}
  }
  const durationMs = performance.now() - startedAt;
  if (r.error) return { ok: false, code: -1, notFound: r.error.code === "ENOENT", timedOut: r.error.code === "ETIMEDOUT", durationMs, stdoutPath, stderrPath, cleanup: () => cleanupStepOutput(dir) };
  return { ok: r.status === 0, code: r.status, durationMs, stdoutPath, stderrPath, cleanup: () => cleanupStepOutput(dir) };
}
function emitStepOutput(res) {
  for (const [file, stream] of [[res.stdoutPath, process.stdout], [res.stderrPath, process.stderr]]) {
    emitOutputFile(file, stream);
  }
}
function emitOutputFile(file, stream) {
  if (!file) return;
  let fd;
  try {
    const st = fs.statSync(file);
    if (!st.isFile() || st.size === 0) return;
    fd = fs.openSync(file, "r");
    const buf = Buffer.alloc(64 * 1024);
    for (;;) {
      const n = fs.readSync(fd, buf, 0, buf.length, null);
      if (!n) break;
      stream.write(buf.subarray(0, n));
    }
  } catch {
  } finally {
    if (fd !== undefined) try { fs.closeSync(fd); } catch {}
  }
}
function diagnosticExcerpt(res, maxLines = 8) {
  const text = [res.stderrPath, res.stdoutPath].map((file) => readSmallOutputFile(file)).filter(Boolean).join("\n").trim();
  if (!text) return "";
  const lines = text.split(/\r?\n/).map((s) => s.trimEnd()).filter((s) => s.trim()).slice(0, maxLines);
  return lines.join("\n");
}
function commandNotFound(res) {
  if (res.notFound || res.code === 9009 || res.code === 127) return true;
  const text = diagnosticExcerpt(res, 8).toLowerCase();
  return /is not recognized as an internal or external command/.test(text) ||
    /is not recognized as the name of (?:a )?(?:cmdlet|function|script file|operable program)/.test(text) ||
    /command not found/.test(text) ||
    /not found:/.test(text);
}
function readSmallOutputFile(file, maxBytes = 64 * 1024) {
  if (!file) return "";
  try {
    const st = fs.statSync(file);
    if (!st.isFile() || st.size === 0) return "";
    if (st.size <= maxBytes) return fs.readFileSync(file, "utf8");
    const start = Math.max(0, st.size - maxBytes);
    const fd = fs.openSync(file, "r");
    try {
      const buf = Buffer.alloc(st.size - start);
      fs.readSync(fd, buf, 0, buf.length, start);
      return (start > 0 ? "[output truncated]\n" : "") + buf.toString("utf8");
    } finally {
      fs.closeSync(fd);
    }
  } catch {
    return "";
  }
}
function cleanupStepOutput(dir) {
  try { fs.rmSync(dir, { recursive: true, force: true }); } catch {}
}

// ---------- main ----------
(function main() {
  const verifyStartedAt = performance.now();
  const a = parseArgs(process.argv.slice(2));
  if (a.errors.length) {
    console.error("verify: " + a.errors.join("; "));
    process.exit(2);
  }
  if (a.checkHarnessSyntax) checkHarnessSyntax(a.root);
  let { stacks, failFast, explicit } = loadStacks(a.root);
  if (a.stack) stacks = stacks.filter((s) => s.id === a.stack);

  const targetPlan = planVerifyTargets(a.root, stacks, { changed: a.changed, base: a.base, files: a.files, fast: a.mode === "fast" });
  if (targetPlan.warning) console.error(targetPlan.warning);
  const targets = targetPlan.targets;

  if (a.list) {
    const plan = targets.map((t) => ({ stack: t.stack.id, dir: t.rel, steps: (t.stack.steps || []).map((s) => s.name) }));
    if (a.json) console.log(JSON.stringify({ explicit, mode: a.mode, plan }));
    else {
      console.log(`design of VERIFY (${a.mode}, ${explicit ? "config" : "auto-detect"}):`);
      if (!plan.length) console.log("  (no stacks detected)");
      for (const p of plan) console.log(`  - ${p.stack} @ ${p.dir}: ${p.steps.join(" -> ")}`);
    }
    process.exit(0);
  }

  // Debug audit runs before the early no-target exit, so it still works without stacks.
  const da = loadDebugAudit(a.root);
  const audit = debugAudit(a.root, da, a.base, a.files);
  let auditFailed = null;
  if (audit.skipped) {
    if (da.enabled) console.log(`\n- debug audit skipped: ${audit.skipped}`);
    if (da.enabled && (a.strictAudit || da.strict)) auditFailed = `debug audit strict: ${audit.skipped}`;
  } else {
    for (const h of audit.soft) console.log(`  debug line (soft): ${h.rel}:${h.line} - ${h.what}`);
    if (audit.hard.length) {
      console.error("\ndebug audit: leftover debug statements in changed files:");
      for (const h of audit.hard) console.error(`  - ${h.rel}:${h.line} - ${h.what}`);
      auditFailed = `debug audit: ${audit.hard.length} hard finding(s)`;
    }
  }

  if (!targets.length && !auditFailed) {
    console.log(a.changed
      ? "verify --changed: changed files do not touch any detected stack; nothing to check."
      : "verify: no stacks detected; nothing to check.");
    console.log(`VERIFY timing: total ${formatDuration(performance.now() - verifyStartedAt)}`);
    process.exit(0);
  }

  let failed = null;
  const summary = [];
  const warnings = [];
  outer:
  for (const t of targets) {
    for (const step of t.stack.steps || []) {
      const label = `${t.stack.id}/${step.name} @ ${t.rel}`;
      console.log(`\n> ${label}: ${step.run}`);
      const res = runStep(step, t.dir);
      const timing = ` [${formatDuration(res.durationMs)}]`;
      try {
        if (res.ok) { emitStepOutput(res); summary.push(`OK ${label}${timing}`); continue; }
        if (res.timedOut) {
          summary.push(`FAIL ${label} (timeout after ${stepTimeoutMs(step)}ms)${timing}`);
          emitStepOutput(res);
          failed = `${label}: timeout after ${stepTimeoutMs(step)}ms`;
          if (failFast) break outer; else continue;
        }
        if (commandNotFound(res)) {
          if (step.optional) {
            warnings.push(`${label}: optional tool not found - step skipped`);
            summary.push(`${label} (optional tool not found; skipped)${timing}`);
            continue;
          }
          summary.push(`${label} (tool not found)${timing}`);
          emitStepOutput(res);
          failed = `${label}: command not found; install the tool or override the step in harness.config.json`;
          if (failFast) break outer; else continue;
        }
        if (step.okCodes && step.okCodes[res.code] !== undefined) {
          const detail = diagnosticExcerpt(res);
          warnings.push(`${label}: exit ${res.code}: ${step.okCodes[res.code] || "allowed code"}${detail ? "\n" + detail : ""}`);
          summary.push(`${label} (exit ${res.code}: ${step.okCodes[res.code] || "allowed code"})${timing}`);
          continue;
        }
        summary.push(`FAIL ${label} (exit ${res.code})${timing}`);
        emitStepOutput(res);
        failed = step.optional
          ? `${label}: optional step ran but failed with exit ${res.code}`
          : `${label}: exit ${res.code}`;
        if (failFast) break outer;
      } finally {
        if (res.cleanup) res.cleanup();
      }
    }
  }

  if (warnings.length) {
    console.log("\n-- optional warnings --");
    for (const w of warnings) console.log("  WARN " + w.replace(/\n/g, "\n    "));
  }
  if (summary.length) {
    console.log("\n-- verify summary --");
    for (const s of summary) console.log("  " + s);
  }
  console.log(`\nVERIFY timing: total ${formatDuration(performance.now() - verifyStartedAt)}`);
  if (failed || auditFailed) {
    console.error(`\nVERIFY failed: ${[failed, auditFailed].filter(Boolean).join(" | ")}`);
    process.exit(1);
  }
  console.log("\nVERIFY passed.");
  process.exit(0);
})();
