// verify-core.js - side-effect-light VERIFY planning and audit policy.
//
// verify.js owns the CLI, process.exit calls, and command execution. This module
// owns stack detection, changed-file target selection, and debug-audit policy so
// those rules can be tested or reused without running external commands.

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");
const { workingTreeChangedFiles, globToRe } = require(path.join(__dirname, "_lib.js"));

function fileUnder(rel, file) {
  const r = String(rel).replace(/\\/g, "/");
  const f = String(file).replace(/\\/g, "/");
  if (r === "." || r === "") return true;
  return f === r || f.startsWith(r + "/");
}

const DEBUG_HARD = [
  { ext: [".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs"], re: /(?:^|[^.\w])debugger\s*;/, what: "debugger statement" },
  { ext: [".py"], re: /(?:^|[^.\w])breakpoint\s*\(\s*\)/, what: "breakpoint call" },
  { ext: [".py"], re: /(?:^|[^.\w])i?pdb\.set_trace\s*\(/, what: "pdb set_trace" },
  { ext: [".rs"], re: /(?:^|[^.\w])dbg!\s*\(/, what: "dbg macro" },
  { ext: [".cs"], re: /Debugger\.Break\s*\(/, what: "Debugger Break" },
];
const DEBUG_SOFT = [
  { ext: [".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs"], re: /(?:^|[^.\w])console\.(?:log|debug)\s*\(/, what: "console.log/debug" },
  { ext: [".py"], re: /(?:^|[^.\w])print\s*\(/, what: "print()" },
];

function loadDebugAudit(root) {
  try {
    const cfg = JSON.parse(fs.readFileSync(path.join(root, "harness.config.json"), "utf8"));
    const d = cfg.debugAudit || {};
    return {
      enabled: d.enabled !== false,
      base: d.base || "main",
      soft: d.soft === true,
      exclude: Array.isArray(d.exclude) ? d.exclude : [],
      strict: d.strict === true,
    };
  } catch {
    return { enabled: true, base: "main", soft: false, exclude: [], strict: false };
  }
}

function maskQuotedSegments(line) {
  let out = "", quote = null, esc = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (quote) {
      if (esc) { esc = false; out += " "; continue; }
      if (ch === "\\") { esc = true; out += " "; continue; }
      if (ch === quote) { quote = null; out += ch; continue; }
      out += " ";
      continue;
    }
    if (ch === "\"" || ch === "'" || ch === "`") { quote = ch; out += ch; continue; }
    out += ch;
  }
  return out;
}

function stripCommentTail(line, ext) {
  if ([".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".rs", ".cs"].includes(ext)) return line.replace(/\/\/.*$/, "");
  if (ext === ".py") return line.replace(/#.*$/, "");
  return line;
}

function scanFileForDebug(abs, rel, soft) {
  let text;
  try {
    const st = fs.statSync(abs);
    if (!st.isFile() || st.size > 1024 * 1024) return [];
    text = fs.readFileSync(abs, "utf8");
  } catch { return []; }
  if (text.includes(String.fromCharCode(0))) return [];
  const ext = path.extname(rel).toLowerCase();
  const softSet = new Set(DEBUG_SOFT);
  const markers = (soft ? DEBUG_HARD.concat(DEBUG_SOFT) : DEBUG_HARD).filter((m) => m.ext.includes(ext));
  if (!markers.length) return [];
  const hits = [];
  const lines = text.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = stripCommentTail(maskQuotedSegments(lines[i]), ext);
    for (const m of markers) if (m.re.test(line)) hits.push({ rel, line: i + 1, what: m.what, soft: softSet.has(m) });
  }
  return hits;
}

function debugAudit(root, opts, base, explicitFiles) {
  if (!opts.enabled) return { hard: [], soft: [], skipped: "disabled in harness.config.json" };
  const cf = workingTreeChangedFiles(base || opts.base, root, explicitFiles);
  if (cf.error) return { hard: [], soft: [], skipped: cf.error };
  const excludeRe = (opts.exclude || []).map(globToRe);
  const hard = [], soft = [];
  for (const f of cf.files) {
    const rel = String(f).replace(/\\/g, "/");
    if (excludeRe.some((re) => re.test(rel))) continue;
    for (const h of scanFileForDebug(path.join(root, rel), rel, opts.soft)) (h.soft ? soft : hard).push(h);
  }
  return { hard, soft, skipped: null };
}

const DEFAULT_STACKS = [
  { id: "rust", markers: ["Cargo.toml"], steps: [
    { name: "fmt", run: "cargo fmt --all --check" },
    { name: "clippy", run: "cargo clippy --all-targets --all-features -- -D warnings" },
    { name: "test", run: "cargo test --all" },
  ] },
  { id: "dotnet", markers: ["*.sln", "*.csproj"], steps: [
    { name: "format", run: "dotnet format --verify-no-changes", optional: true },
    { name: "build", run: "dotnet build --nologo -warnaserror" },
    { name: "test", run: "dotnet test --nologo" },
  ] },
  { id: "python", markers: ["pyproject.toml", "requirements.txt", "setup.py"], steps: [
    { name: "lint", run: "ruff check ." },
    { name: "format", run: "ruff format --check .", optional: true },
    { name: "test", run: "pytest -q", okCodes: { 5: "pytest: no tests collected; add at least a smoke test" } },
  ] },
  { id: "node", markers: ["package.json"], steps: [
    { name: "lint", run: "npm run lint --if-present" },
    { name: "build", run: "npm run build --if-present" },
    { name: "test", run: "npm test --if-present" },
  ] },
];

const SKIP_DIRS = new Set([".git", ".codex", "node_modules", "target", "bin", "obj", "dist", "build", ".venv", "venv", "__pycache__", ".next", ".idea", ".vscode"]);
const MAX_DEPTH = 6;
const HARNESS_CHANGED = [
  "hooks/", "lefthook.yml", "harness.config.json", ".gitleaks.toml", "cog.toml",
  ".github/rulesets/", ".github/workflows/", "settings.example.json", "AGENTS.md",
];

function loadStacks(root) {
  try {
    const cfg = JSON.parse(fs.readFileSync(path.join(root, "harness.config.json"), "utf8"));
    if (cfg.verify && Array.isArray(cfg.verify.stacks) && cfg.verify.stacks.length) {
      return { stacks: cfg.verify.stacks, failFast: cfg.verify.failFast !== false, explicit: true };
    }
    return { stacks: DEFAULT_STACKS, failFast: !(cfg.verify && cfg.verify.failFast === false), explicit: false };
  } catch {
    return { stacks: DEFAULT_STACKS, failFast: true, explicit: false };
  }
}

function isHarnessChangedFile(file) {
  const f = String(file || "").replace(/\\/g, "/");
  return HARNESS_CHANGED.some((p) => p.endsWith("/") ? f.startsWith(p) : f === p);
}

function harnessTarget(root) {
  const steps = [];
  if (fs.existsSync(path.join(root, "hooks", "test.js"))) steps.push({ name: "self-test", run: "node test.js", cwdRel: "hooks" });
  return { stack: { id: "harness", steps }, dir: root, rel: "." };
}

function harnessSyntaxTarget(root) {
  return { stack: { id: "harness-syntax", steps: [{ name: "syntax", run: "node hooks/verify.js --check-harness-syntax" }] }, dir: root, rel: "." };
}

function ensureHarnessSyntaxTarget(root, targets) {
  if (!fs.existsSync(path.join(root, "hooks", "verify.js"))) return targets;
  if (targets.some((t) => t.stack && t.stack.id === "harness-syntax")) return targets;
  return targets.concat([harnessSyntaxTarget(root)]);
}

function isGitRepo(root) {
  const r = spawnSync("git", ["rev-parse", "--is-inside-work-tree"], {
    cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
    timeout: 5000, killSignal: "SIGKILL",
  });
  return r.status === 0 && String(r.stdout || "").trim() === "true";
}

function ensureGitHygieneTarget(root, targets) {
  if (!isGitRepo(root)) return targets;
  if (targets.some((t) => t.stack && t.stack.id === "git-hygiene")) return targets;
  return targets.concat([{ stack: { id: "git-hygiene", steps: [
    { name: "diff-check", run: "git diff --check && git diff --cached --check" },
  ] }, dir: root, rel: "." }]);
}

function maybeAddHarnessTarget(root, targets, files) {
  if (!files.some(isHarnessChangedFile)) return targets;
  if (!fs.existsSync(path.join(root, "hooks", "verify.js"))) return targets;
  targets = ensureHarnessSyntaxTarget(root, targets);
  if (!fs.existsSync(path.join(root, "hooks", "test.js"))) return targets;
  if (targets.some((t) => t.stack && t.stack.id === "harness")) return targets;
  return targets.concat([harnessTarget(root)]);
}

function fnMatch(pattern, name) {
  if (pattern === name) return true;
  const re = "^" + pattern.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, "[^/]*") + "$";
  return new RegExp(re).test(name);
}

function detect(root, stacks) {
  const found = [];
  function walk(dir, depth) {
    let entries;
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
    const names = entries.filter((e) => e.isFile()).map((e) => e.name);
    for (const s of stacks) {
      const markers = s.markers || [];
      if (markers.some((m) => names.some((n) => fnMatch(m, n)))) {
        found.push({ stack: s, dir, rel: (path.relative(root, dir) || ".").replace(/\\/g, "/") });
      }
    }
    if (depth >= MAX_DEPTH) return;
    for (const e of entries) if (e.isDirectory() && !SKIP_DIRS.has(e.name)) walk(path.join(dir, e.name), depth + 1);
  }
  walk(root, 0);
  return found;
}

function planVerifyTargets(root, stacks, opts = {}) {
  let targets = detect(root, stacks);
  if (opts.changed) {
    const cf = workingTreeChangedFiles(opts.base, root, opts.files);
    if (cf.error) {
      return {
        targets: maybeAddHarnessTarget(root, targets, ["hooks/verify.js"]),
        scope: cf,
        warning: `verify --changed: ${cf.error}; filter not applied, checking all detected stacks. Pass --base <ref> to choose a base.`,
      };
    }
    const files = cf.files.map((f) => String(f).replace(/\\/g, "/"));
    targets = files.length ? targets.filter((t) => files.some((f) => fileUnder(t.rel, f))) : [];
    targets = maybeAddHarnessTarget(root, targets, files);
    return { targets, scope: cf, files };
  }
  targets = ensureHarnessSyntaxTarget(root, targets);
  targets = ensureGitHygieneTarget(root, targets);
  return { targets, scope: null, files: null };
}

module.exports = {
  DEBUG_HARD, DEBUG_SOFT, DEFAULT_STACKS,
  fileUnder, loadDebugAudit, scanFileForDebug, debugAudit,
  loadStacks, detect, planVerifyTargets,
  ensureHarnessSyntaxTarget, ensureGitHygieneTarget, maybeAddHarnessTarget,
};
