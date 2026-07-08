// _lib.js — shared helpers for the harness hooks (guard.js, design-gate.js).
// Single source of truth for: glob→regex, harness.config.json loading, path
// normalization. No CLI, no side effects.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const DEFAULT_UI_GLOBS = ["**/*.ui", "**/*.qml", "**/*.slint", "**/ui/**", "**/views/**", "**/widgets/**"];
const DEFAULT_MOCKUPS = {
  dir: "design/mockups",
  min: 4,
  mockupExtensions: [".html", ".svg", ".png", ".jpg", ".jpeg", ".webp", ".pdf"],
  approvalFile: "APPROVED",
};
const DEFAULT_PROTECTED = [
  "hooks/", "lefthook.yml", "harness.config.json", ".gitleaks.toml", "cog.toml",
  ".github/rulesets/", ".github/workflows/", ".claude/settings.json", ".git/",
];

// Линт/формат-конфиги ЦЕЛЕВОГО проекта (паттерн ECC config-protection): агенты
// «чинят» красный VERIFY, ослабляя конфиг вместо кода. Блокируем правку
// СУЩЕСТВУЮЩЕГО конфига; создание с нуля — легитимный bootstrap. Смешанные файлы
// (pyproject.toml, package.json, tsconfig.json) намеренно НЕ в списке.
const DEFAULT_LINT_CONFIGS = [
  ".eslintrc", ".eslintrc.js", ".eslintrc.cjs", ".eslintrc.json", ".eslintrc.yml", ".eslintrc.yaml",
  "eslint.config.js", "eslint.config.mjs", "eslint.config.cjs", "eslint.config.ts", "eslint.config.mts", "eslint.config.cts",
  ".eslintignore",
  ".prettierrc", ".prettierrc.js", ".prettierrc.cjs", ".prettierrc.json", ".prettierrc.yml", ".prettierrc.yaml",
  "prettier.config.js", "prettier.config.cjs", "prettier.config.mjs", ".prettierignore",
  "biome.json", "biome.jsonc",
  "ruff.toml", ".ruff.toml",
  "rustfmt.toml", ".rustfmt.toml", "clippy.toml", ".clippy.toml",
  ".editorconfig", ".flake8", "mypy.ini", ".mypy.ini", "pytest.ini",
  ".stylelintrc", ".stylelintrc.json", ".stylelintrc.yml", "stylelint.config.js",
  ".markdownlint.json", ".markdownlint.yaml", ".markdownlintrc", ".shellcheckrc",
];

// glob → RegExp (supports **, *, literal)
function globToRe(g) {
  const re = g.replace(/[.+^${}()|[\]\\]/g, "\\$&")
    .replace(/\*\*\//g, "@@DS@@").replace(/\*\*/g, "@@SS@@").replace(/\*/g, "[^/]*")
    .replace(/@@DS@@/g, "(?:.*/)?").replace(/@@SS@@/g, ".*");
  return new RegExp("^" + re + "$", "i");
}

// harness.config.json (missing/broken file → defaults; hooks stay fail-open)
function loadConfig(root) {
  let c = {};
  try { c = JSON.parse(fs.readFileSync(path.join(root, "harness.config.json"), "utf8")); } catch {}
  const ui = c.ui || {};
  return {
    uiGlobs: ui.globs || DEFAULT_UI_GLOBS,
    mockups: { ...DEFAULT_MOCKUPS, ...(ui.mockups || {}) },
    protected: (c.protected && c.protected.paths) || DEFAULT_PROTECTED,
    lintConfigs: (c.protected && c.protected.lintConfigs) || DEFAULT_LINT_CONFIGS,
  };
}

// Absolute/relative path → normalized repo-relative posix path.
// Collapses ./ and ..; strips the project prefix case-insensitively — WITHOUT
// normalization "./lefthook.yml" or "design/../hooks/x" would dodge prefix checks.
function normRel(fp, projectDir) {
  let f = String(fp).replace(/\\/g, "/");
  const pd = String(projectDir || "").replace(/\\/g, "/").replace(/\/$/, "");
  if (pd && f.toLowerCase().startsWith(pd.toLowerCase() + "/")) f = f.slice(pd.length + 1);
  f = path.posix.normalize(f);
  if (f.startsWith("./")) f = f.slice(2);
  return f;
}

// Is rel under one of the protected prefixes? Case-insensitive: Windows/macOS
// filesystems are case-insensitive, so "Lefthook.yml" IS lefthook.yml there.
function isProtectedPath(rel, protectedList) {
  const r = rel.toLowerCase();
  return protectedList.some((pref) => {
    const p = pref.toLowerCase();
    return r === p || r === p.replace(/\/$/, "") || r.startsWith(p.endsWith("/") ? p : p + "/");
  });
}

// ---------- shell-write detection ----------
// Covers write/delete/move verbs across POSIX, cmd и PowerShell + sed/perl -i и
// перенаправление > / >>. Windows-глаголы (del/move/Remove-Item/Set-Content…) —
// потому что здесь основная оболочка PowerShell/cmd, а не bash: без них
// `del hooks\x.js` или `Remove-Item lefthook.yml` проходили мимо защиты.
// Все глаголы срабатывают ТОЛЬКО когда цель — защищённый путь (${target}),
// поэтому обычные команды не задеваются. Read-only (cat/ls/node hooks/x.js) — мимо.
const WRITE_VERBS =
  "rm|mv|cp|tee|chmod|ln|truncate|touch|" + // POSIX
  "del|erase|rmdir|rd|move|ren|rename|copy|" + // cmd.exe
  "Remove-Item|Move-Item|Rename-Item|Copy-Item|Set-Content|Add-Content|Clear-Content|Out-File|New-Item"; // PowerShell
function shellWriteHit(scrubbed, alt, lb, dirPrefixInRedirect) {
  const seg = "[^;|&<>]*";
  const target = lb + alt;
  const redir = dirPrefixInRedirect ? `>>?\\s*(?:[^\\s;|&]*[\\/\\\\])?${alt}` : `>>?\\s*${alt}`;
  const res = [
    new RegExp(`(?:^|[\\s;&|])(?:${WRITE_VERBS})\\s+${seg}${target}`, "i"),
    new RegExp(`(?:^|[\\s;&|])(?:sed|perl)\\s+${seg}\\s-[a-z]*i[a-z.]*\\b${seg}${target}`, "i"),
    new RegExp(`(?:^|[\\s;&|])(?:sed|perl)\\s+${seg}-[a-z]*i[a-z.]*\\s+${seg}${target}`, "i"),
    new RegExp(redir, "i"),
  ];
  return res.some((re) => re.test(scrubbed));
}

// SEP — разделитель пути: `/` ИЛИ `\` (в PowerShell/cmd путь пишут через backslash,
// поэтому `del hooks\agent\guard.js` должен матчиться так же, как `rm hooks/...`).
const SEP = "[\\/\\\\]";

// Запись в защищённые пути харнесса (префиксы от корня репо).
function isProtectedShellWrite(scrubbed, protectedList) {
  // Порядок важен: сначала слэши → SEP, потом точки → `\.`. Иначе экранирование
  // точки вставит `\`, который замена слэшей затрёт (lefthook.yml → lefthook[\/\\]yml).
  const esc = (str) => str.replace(/[\/\\]/g, SEP).replace(/\./g, "\\.");
  // "hooks/" защищает и "hooks/x", и голое "rm -rf hooks"; файлы — по границе слова.
  const alt = "(?:\\." + SEP + ")?(?:" + protectedList.map((p) =>
    p.endsWith("/") ? esc(p.slice(0, -1)) + "(?:" + SEP + "|[\\s;&|]|$)" : esc(p) + "\\b"
  ).join("|") + ")";
  // Lookbehind БЕЗ разделителя: src/hooks/useAuth.ts (React) — не файл харнесса.
  return shellWriteHit(scrubbed, alt, "(?<=^|[\\s=:'\"(])", false);
}

// Запись в lint/format-конфиг (по basename, в любом каталоге — через / или \).
function isLintConfigShellWrite(scrubbed, lintConfigs) {
  const names = lintConfigs.map((n) => n.replace(/\./g, "\\.")).join("|");
  const alt = `(?:[^\\s;|&<>]*${SEP})?(?:${names})\\b`;
  return shellWriteHit(scrubbed, alt, "(?<=^|[\\s=:'\"(/\\\\])", true);
}

// rel — нормализованный путь; сравнение по basename, регистронезависимо.
function isLintConfigPath(rel, lintConfigs) {
  const base = String(rel).toLowerCase().split("/").pop();
  return lintConfigs.some((n) => n.toLowerCase() === base);
}

// ---------- запись в защищённый путь через инлайн-eval интерпретатора ----------
// `node -e "fs.writeFileSync('hooks/x')"`, `python -c "open('lefthook.yml','w')"`,
// `bash -c "rm -rf hooks/"` обходят write-verb-детекцию: глагол/путь спрятаны в
// строке, а scrubQuotes её обнуляет. Работаем по СЫРОЙ команде. Это НОТА, не блок:
// в -e путь может быть безобидной строкой, жёстко блокировать нельзя, но напомнить
// про обход стоит. Триггерим только при совпадении трёх условий: интерпретатор с
// eval-флагом + индикатор записи + литерал защищённого пути (минимум ложных).
const INTERP_EVAL_RE = /\b(?:node|nodejs|deno|bun|python|python3|py|perl|ruby|php|pwsh|powershell|bash|sh|zsh)\b[^\n]*?(?:\s-e\b|\s--eval\b|\s-c\b|\seval\b|\s-Command\b|\s-EncodedCommand\b)/i;
const INTERP_WRITE_RE = /writefile|writefilesync|appendfile|createwritestream|fs\.write|\.write\s*\(|open\s*\([^)]*['"][aw]|set-content|add-content|out-file|>{1,2}|\b(?:rm|del|erase|move|mv|remove-item|ren|rename)\b/i;
function interpreterProtectedHint(rawCmd, protectedList) {
  const s = String(rawCmd);
  if (!INTERP_EVAL_RE.test(s) || !INTERP_WRITE_RE.test(s)) return null;
  const low = s.replace(/\\/g, "/").toLowerCase();
  for (const p of protectedList) {
    const pref = p.toLowerCase().replace(/\/$/, "").replace(/[.]/g, "\\.");
    if (new RegExp("(?:^|[\\s'\"(/=:])" + pref + "(?:/|\\b)").test(low)) return p;
  }
  return null;
}

// ---------- changed files (branch diff) ----------
// Изменённые файлы ветки относительно базы. Возвращает {files, base} при успешном
// diff (пусть даже ПУСТОМ) или {error} если ни одна база не доступна — это РАЗНЫЕ
// исходы: пустой diff = «изменений нет», ошибка = «не смогли проверить». Молчаливый
// fail-open при ошибке означал бы, что в репо без ожидаемой базы гейт/фильтр просто
// никогда не работает. explicitFiles (тесты/CI) возвращается как есть, без git.
// Общий источник для design-gate.js (гейт) и verify.js (--changed фильтр стеков).
function changedFiles(base, root, explicitFiles) {
  if (explicitFiles) return { files: explicitFiles };
  const remoteFirst = /^origin\//.test(String(base || ""));
  const fallbacks = remoteFirst
    ? [base, "origin/HEAD", "main", "master"]
    : [base, "main", "master", "origin/main", "origin/HEAD"];
  const bases = [...new Set(fallbacks.filter(Boolean))];
  for (const b of bases) {
    for (const args of [["diff", "--name-only", `${b}...HEAD`], ["diff", "--name-only", b]]) {
      try {
        const out = execFileSync("git", args, { cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"], timeout: 5000, killSignal: "SIGKILL" });
        return { files: out.split(/\r?\n/).map((s) => s.trim()).filter(Boolean), base: b };
      } catch {}
    }
  }
  return { error: `git diff не удался ни для одной базы (${bases.join(", ")})` };
}

module.exports = {
  DEFAULT_UI_GLOBS, DEFAULT_MOCKUPS, DEFAULT_PROTECTED, DEFAULT_LINT_CONFIGS,
  globToRe, loadConfig, normRel, isProtectedPath,
  isProtectedShellWrite, isLintConfigShellWrite, isLintConfigPath,
  interpreterProtectedHint,
  changedFiles,
};
