#!/usr/bin/env node
// design-gate.js — DESIGN-stage gate.
//
// Policy: GUI work must be preceded by design review. If a branch's changes touch UI
// paths, the SAME branch diff must also touch an APPROVED set of >= N mockups —
// otherwise one old approval would open the gate for all future UI work forever.
//
// Usage:
//   node hooks/design-gate.js [--base <ref>] [--root <dir>] [--files a,b,c] [--json]
//     --base   git ref to diff against (default: origin/main if available) [CI/local]
//     --files  explicit comma-separated changed files          [tests/CI]
//     --root   repo root to resolve config + mockups (default: cwd)
//
// Exit 0 = gate satisfied (or no UI change), exit 1 = UI changed without approved mockups.
// Internal error → exit 0 (never wedge unrelated work), но с ГРОМКИМ warning.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

// ---------- args ----------
function parseArgs(argv) {
  const a = { base: null, root: process.cwd(), files: null, json: false };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--base") a.base = argv[++i];
    else if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--files") a.files = (argv[++i] || "").split(",").map((s) => s.trim()).filter(Boolean);
    else if (argv[i] === "--json") a.json = true;
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

// ---------- config (общая с guard.js: hooks/_lib.js) ----------
// changedFiles — общий с verify.js (--changed): единый источник git-diff логики.
const { globToRe, loadConfig, changedFiles } = require(path.join(__dirname, "_lib.js"));

// ---------- mockups scan ----------
// Одобренный набор засчитывается ТОЛЬКО если он затронут в diff этой же ветки —
// иначе один старый approval навсегда открывал бы гейт для любых будущих UI-правок.
// Повторное использование уже одобренного набора: допиши строку в его APPROVED
// (дата/ветка) — файл попадёт в diff, и связь «этот набор ↔ это изменение» явная.
function hasApprovedMockups(root, m, changed) {
  const base = path.join(root, m.dir);
  const mockRoot = m.dir.replace(/\\/g, "/").replace(/\/$/, "");
  let dirs;
  try { dirs = fs.readdirSync(base, { withFileTypes: true }).filter((d) => d.isDirectory()); }
  catch { return { ok: false, reason: `нет каталога ${m.dir}/` }; }

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
      ? `одобренные наборы (${stale.join(", ")}) не затронуты в diff этой ветки — ` +
        `привяжи набор к изменению: допиши строку в ${m.dir}/<feature>/${m.approvalFile}`
      : `нет ${m.dir}/<feature>/ с >=${m.min} мокапами и файлом ${m.approvalFile}, затронутого в этой ветке`,
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
    // fail-open, но ГРОМКО: молчаливый пропуск = гейта нет.
    const warn = `⚠️ design-gate: ${cf.error} — гейт ПРОПУЩЕН, UI-изменения не проверены. Укажи базу явно: --base <ref>.`;
    if (a.json) console.log(JSON.stringify({ ...res, skipped: true, warn }));
    else console.error(warn);
    process.exit(0);
  }
  const files = cf.files.map((f) => f.replace(/\\/g, "/"));

  res.uiChanged = files.filter(
    (f) => !f.startsWith(mockRoot + "/") && uiRes.some((re) => re.test(f))
  );

  if (res.uiChanged.length === 0) {
    if (a.json) console.log(JSON.stringify({ ...res, note: "нет изменений в UI-путях" }));
    else console.log("OK design-gate: изменений в UI-путях нет — гейт не требуется.");
    process.exit(0);
  }

  const mk = hasApprovedMockups(a.root, cfg.mockups, files);
  res.mockups = mk;
  if (mk.ok) {
    if (a.json) console.log(JSON.stringify(res));
    else console.log(`OK design-gate: UI-изменения есть, одобренный набор мокапов затронут в ветке (${mk.feature}, ${mk.count} шт.).`);
    process.exit(0);
  }

  if (a.json) { console.log(JSON.stringify({ ...res, ok: false })); process.exit(1); }
  console.error(
    `BLOCK design-gate: изменения затрагивают GUI, но DESIGN-стадия не выполнена.\n` +
      `   UI-файлы: ${res.uiChanged.slice(0, 8).join(", ")}${res.uiChanged.length > 8 ? " ..." : ""}\n` +
      `   Требуется: ${mk.reason}.\n` +
      `   Новый набор:  node hooks/new-mockups.js <feature> → approval → создай ${cfg.mockups.dir}/<feature>/${cfg.mockups.approvalFile}.\n` +
      `   Уже одобренный набор: допиши строку в его ${cfg.mockups.approvalFile}, чтобы он попал в diff ветки.\n` +
      `   Политика: для нового/изменяемого GUI — >=${cfg.mockups.min} стилистически разных мокапа + approval.`
  );
  process.exit(1);
})();
