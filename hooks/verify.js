#!/usr/bin/env node
// verify.js — executable, multi-stack VERIFY runner (BACKLOG P1-8).
//
// Makes AGENTS.md step 4 (VERIFY) an actual command instead of prose. Auto-detects
// which stacks live in the repo by marker files and runs lint -> build -> test for
// each, fail-fast, with warnings-as-errors defaults. Covers the user's stacks:
//   Python/Qt (ruff + pytest), C#/WPF (dotnet format/build -warnaserror/test),
//   Rust/Tauri (cargo fmt + clippy -D warnings + test; Tauri = node front + rust back),
//   Node (npm lint/build/test).
//
// Usage:
//   node hooks/verify.js [--root <dir>] [--stack <id>] [--changed [--base <ref>]] [--list] [--json]
//     --list     detect + print the plan, do not run
//     --stack    run only the named stack
//     --root     repo root (default: cwd)
//     --changed  verify only stacks whose dir is touched in the branch diff (faster inner loop)
//     --base     git ref to diff against for --changed (default: main; fallback master/origin/HEAD)
//     --files    explicit comma-separated changed files (tests/CI; bypasses git)
//   --changed fail-safe: if the diff can't be computed, verify ALL stacks (loud warn),
//   never silently skip verification; empty diff = nothing to verify.
//
// Config: harness.config.json -> "verify". If "verify.stacks" is present it REPLACES
// the auto-detected defaults (explicit control); otherwise DEFAULT_STACKS are used.
// Per-step: optional (missing tool → skip), okCodes { "<exit>": "note" } — non-zero
// exits that are warnings, not failures (e.g. pytest 5 = no tests collected).
//
// Debug-аудит: параллельно со стеками verify сканирует ТОЛЬКО изменённые в diff
// файлы на забытые отладочные строки. hard-маркеры (debugger; / breakpoint() /
// pdb.set_trace() / dbg!()) валят VERIFY; soft (console.log / print) — заметка,
// включается harness.config.json -> debugAudit.soft. Область — diff (база = --base
// или debugAudit.base); без diff аудит пропускается (не сканируем весь репо, иначе
// массовые легитимные console.log/print дали бы шум). exclude-глобы — пропуск путей.
//
// Exit 0 = all steps passed (or nothing to verify), exit 1 = a required step failed
//          ИЛИ debug-аудит нашёл hard-находку.

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");
const { changedFiles, globToRe } = require(path.join(__dirname, "_lib.js"));

// Файл `file` лежит под каталогом стека `rel`? Корневой стек (rel ".") владеет всем
// деревом → матчит любой изменённый файл; глубокий стек — только свой подкаталог.
function fileUnder(rel, file) {
  const r = String(rel).replace(/\\/g, "/");
  const f = String(file).replace(/\\/g, "/");
  if (r === "." || r === "") return true;
  return f === r || f.startsWith(r + "/");
}

// ---------- debug-leftover audit (только изменённые файлы) ----------
// Маркеры «никогда не коммить». Точные по синтаксису (требуют `;`/`()`), поэтому
// строковые определения ниже и regex-литералы НЕ матчат сами себя (self-FP).
// `what` намеренно без реального синтаксиса (без точки/скобок) — та же причина.
// Привязка к расширениям: `breakpoint()`/`set_trace` — только .py, `dbg!` — только
// .rs; иначе слово-омоним в чужом языке дал бы ложную тревогу.
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

// harness.config.json → debugAudit (дефолты: включён, база main, soft off, без exclude).
function loadDebugAudit(root) {
  try {
    const cfg = JSON.parse(fs.readFileSync(path.join(root, "harness.config.json"), "utf8"));
    const d = cfg.debugAudit || {};
    return {
      enabled: d.enabled !== false,
      base: d.base || "main",
      soft: d.soft === true,
      exclude: Array.isArray(d.exclude) ? d.exclude : [],
    };
  } catch {
    return { enabled: true, base: "main", soft: false, exclude: [] };
  }
}

// Один файл → массив находок. Бинарь/крупный/нечитаемый → пропуск (не ошибка).
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
    for (const m of markers) if (m.re.test(lines[i])) hits.push({ rel, line: i + 1, what: m.what, soft: softSet.has(m) });
  }
  return hits;
}

// Аудит изменённых файлов. Область — ТОЛЬКО diff (в отличие от стеков, где fail-safe
// = проверить всё): без diff НЕ сканируем весь репозиторий, т.к. console.log/print
// массово легитимны и дали бы шум. → { hard:[], soft:[], skipped:<причина>|null }.
function debugAudit(root, opts, base, explicitFiles) {
  if (!opts.enabled) return { hard: [], soft: [], skipped: "отключён в harness.config.json" };
  const cf = changedFiles(base || opts.base, root, explicitFiles);
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

// ---------- defaults (warnings-as-errors baked in) ----------
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
    // pytest exit 5 = «тесты не собраны»: проект без тестов — warning, не вечно-красный VERIFY
    { name: "test", run: "pytest -q", okCodes: { 5: "pytest: тесты не найдены — добавь хотя бы smoke-тест" } },
  ] },
  { id: "node", markers: ["package.json"], steps: [
    { name: "lint", run: "npm run lint --if-present" },
    { name: "build", run: "npm run build --if-present" },
    { name: "test", run: "npm test --if-present" },
  ] },
];

const SKIP_DIRS = new Set([".git", "node_modules", "target", "bin", "obj", "dist", "build", ".venv", "venv", "__pycache__", ".next", ".idea", ".vscode"]);
const MAX_DEPTH = 6;

// ---------- args ----------
function parseArgs(argv) {
  const a = { root: process.cwd(), stack: null, list: false, json: false, changed: false, base: "main", files: null };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--stack") a.stack = argv[++i];
    else if (argv[i] === "--list") a.list = true;
    else if (argv[i] === "--json") a.json = true;
    else if (argv[i] === "--changed") a.changed = true;
    else if (argv[i] === "--base") a.base = argv[++i];
    else if (argv[i] === "--files") a.files = (argv[++i] || "").split(",").map((s) => s.trim()).filter(Boolean);
  }
  return a;
}

// ---------- config ----------
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

// ---------- filename glob (within a directory) ----------
function fnMatch(pattern, name) {
  if (pattern === name) return true;
  const re = "^" + pattern.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, "[^/]*") + "$";
  return new RegExp(re).test(name);
}

// ---------- detect: dirs that contain a stack's marker ----------
function detect(root, stacks) {
  const found = []; // {stack, dir(abs), rel}
  function walk(dir, depth) {
    let entries;
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
    const names = entries.filter((e) => e.isFile()).map((e) => e.name);
    for (const s of stacks) {
      const markers = s.markers || [];
      if (markers.some((m) => names.some((n) => fnMatch(m, n)))) {
        found.push({ stack: s, dir, rel: path.relative(root, dir) || "." });
      }
    }
    if (depth >= MAX_DEPTH) return;
    for (const e of entries) if (e.isDirectory() && !SKIP_DIRS.has(e.name)) walk(path.join(dir, e.name), depth + 1);
  }
  walk(root, 0);
  return found;
}

// ---------- run ----------
function runStep(step, cwd) {
  const r = spawnSync(step.run, { cwd, shell: true, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  if (r.error) return { ok: false, code: -1, notFound: r.error.code === "ENOENT" };
  return { ok: r.status === 0, code: r.status, stdout: String(r.stdout || ""), stderr: String(r.stderr || "") };
}
function emitStepOutput(res) {
  if (res.stdout) process.stdout.write(res.stdout);
  if (res.stderr) process.stderr.write(res.stderr);
}
function diagnosticExcerpt(res, maxLines = 8) {
  const text = [res.stderr, res.stdout].filter(Boolean).join("\n").trim();
  if (!text) return "";
  const lines = text.split(/\r?\n/).map((s) => s.trimEnd()).filter((s) => s.trim()).slice(0, maxLines);
  return lines.join("\n");
}

// ---------- main ----------
(function main() {
  const a = parseArgs(process.argv.slice(2));
  let { stacks, failFast, explicit } = loadStacks(a.root);
  if (a.stack) stacks = stacks.filter((s) => s.id === a.stack);

  let targets = detect(a.root, stacks);

  // --changed: сузить до стеков, чьи каталоги затронуты в diff ветки. Fail-safe —
  // при ошибке diff проверяем ВСЕ стеки (громкий warn), а не молча пропускаем.
  if (a.changed) {
    const cf = changedFiles(a.base, a.root, a.files);
    if (cf.error) {
      console.error(`⚠️ verify --changed: ${cf.error} — фильтр не применён, проверяю все обнаруженные стеки. Задай базу: --base <ref>.`);
    } else {
      const files = cf.files.map((f) => f.replace(/\\/g, "/"));
      targets = files.length ? targets.filter((t) => files.some((f) => fileUnder(t.rel, f))) : [];
    }
  }

  if (a.list) {
    const plan = targets.map((t) => ({ stack: t.stack.id, dir: t.rel, steps: (t.stack.steps || []).map((s) => s.name) }));
    if (a.json) console.log(JSON.stringify({ explicit, plan }));
    else {
      console.log(`design of VERIFY (${explicit ? "config" : "auto-detect"}):`);
      if (!plan.length) console.log("  (стеки не обнаружены)");
      for (const p of plan) console.log(`  • ${p.stack} @ ${p.dir}: ${p.steps.join(" → ")}`);
    }
    process.exit(0);
  }

  // debug-аудит изменённых файлов: до раннего выхода — работает даже без стеков.
  // hard-находки валят VERIFY; soft (только при soft=true) — заметка без падения.
  const da = loadDebugAudit(a.root);
  const audit = debugAudit(a.root, da, a.base, a.files);
  let auditFailed = null;
  if (audit.skipped) {
    if (da.enabled) console.log(`\n· debug-аудит пропущен: ${audit.skipped}`);
  } else {
    for (const h of audit.soft) console.log(`  ⚠ debug-строка (soft): ${h.rel}:${h.line} — ${h.what}`);
    if (audit.hard.length) {
      console.error("\n❌ debug-аудит — забытые отладочные строки в изменённых файлах:");
      for (const h of audit.hard) console.error(`  ✗ ${h.rel}:${h.line} — ${h.what}`);
      auditFailed = `debug-аудит: ${audit.hard.length} hard-находок (debugger/breakpoint/set_trace/dbg!)`;
    }
  }

  if (!targets.length && !auditFailed) {
    console.log(a.changed
      ? "✅ verify --changed: изменённые файлы не затрагивают ни один стек — проверять нечего."
      : "✅ verify: стеки не обнаружены — проверять нечего.");
    process.exit(0);
  }

  let failed = null;
  const summary = [];
  const warnings = [];
  outer:
  for (const t of targets) {
    for (const step of t.stack.steps || []) {
      const label = `${t.stack.id}/${step.name} @ ${t.rel}`;
      console.log(`\n▶ ${label}: ${step.run}`);
      const res = runStep(step, t.dir);
      if (res.ok) { emitStepOutput(res); summary.push(`✓ ${label}`); continue; }
      if (res.notFound || res.code === 9009 || res.code === 127) {
        if (step.optional) {
          warnings.push(`${label}: optional tool not found — step skipped`);
          summary.push(`⚠ ${label} (инструмент не найден — пропущено)`);
          continue;
        }
        summary.push(`✗ ${label} (инструмент не найден)`);
        emitStepOutput(res);
        failed = `${label}: команда не найдена — установи инструмент или переопредели шаг в harness.config.json`;
        if (failFast) break outer; else continue;
      }
      if (step.okCodes && step.okCodes[res.code] !== undefined) {
        const detail = diagnosticExcerpt(res);
        warnings.push(`${label}: exit ${res.code}: ${step.okCodes[res.code] || "допустимый код"}${detail ? "\n" + detail : ""}`);
        summary.push(`⚠ ${label} (exit ${res.code}: ${step.okCodes[res.code] || "допустимый код"})`);
        continue;
      }
      if (step.optional) {
        const detail = diagnosticExcerpt(res);
        warnings.push(`${label}: optional step exited ${res.code}${detail ? "\n" + detail : "\n(no diagnostics captured)"}`);
        summary.push(`⚠ ${label} (optional, exit ${res.code})`);
        continue;
      }
      summary.push(`✗ ${label} (exit ${res.code})`);
      emitStepOutput(res);
      failed = `${label}: exit ${res.code}`;
      if (failFast) break outer;
    }
  }

  if (warnings.length) {
    console.log("\n— optional warnings —");
    for (const w of warnings) console.log("  ⚠ " + w.replace(/\n/g, "\n    "));
  }
  if (summary.length) {
    console.log("\n— verify summary —");
    for (const s of summary) console.log("  " + s);
  }
  if (failed || auditFailed) {
    console.error(`\n❌ VERIFY failed: ${[failed, auditFailed].filter(Boolean).join(" | ")}`);
    process.exit(1);
  }
  console.log("\n✅ VERIFY passed.");
  process.exit(0);
})();
