// _lib.js - shared helpers for the harness hooks (guard.js, design-gate.js).
// Single source of truth for: glob-to-regex, harness.config.json loading, path
// normalization. No CLI, no side effects.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const DEFAULT_UI_GLOBS = [
  "**/*.ui", "**/*.qml", "**/*.slint", "**/*.xaml", "**/*.axaml",
  "**/*.razor", "**/*.cshtml", "**/*.tsx", "**/*.jsx", "**/*.vue",
  "**/*.svelte", "**/*.html", "**/*.css", "**/*.scss", "**/*.sass",
  "**/*.less", "**/res/layout/**/*.xml", "**/*View.swift",
  "**/ui/**", "**/views/**", "**/widgets/**", "**/components/**",
  "**/pages/**", "**/screens/**", "**/*_window.py", "**/*_dialog.py",
  "**/*_view.py", "**/*_widget.py",
];
const DEFAULT_UI_EXCLUDE = [
  "**/node_modules/**", "**/vendor/**", "**/dist/**", "**/build/**",
  "**/bin/**", "**/obj/**", "**/coverage/**", "**/generated/**",
  "**/*.generated.*", "**/*.g.*", "**/*.test.*", "**/*.spec.*",
  "**/__tests__/**", "**/test/**", "**/tests/**", "**/fixtures/**",
];
const DEFAULT_MOCKUPS = {
  dir: "design/mockups",
  min: 4,
  mockupExtensions: [".html", ".svg", ".png", ".jpg", ".jpeg", ".webp", ".pdf"],
  manifestFile: "DESIGN.json",
  approvalFile: "APPROVED",
  waiverFile: "WAIVER.json",
  cosmeticFile: "COSMETIC.json",
};
const DEFAULT_PROTECTED = [
  "hooks/", ".harness/", "lefthook.yml", "harness.config.json", ".gitleaks.toml", "cog.toml",
  ".github/rulesets/", ".github/workflows/", ".claude/settings.json", ".git/",
];

// Lint/format configs of the target project (ECC config-protection pattern):
// agents sometimes "fix" a red VERIFY by weakening config instead of fixing code.
// Block edits to existing configs; creating a config from scratch is legitimate
// bootstrap work. Mixed-purpose files (pyproject.toml, package.json, tsconfig.json)
// are intentionally not listed.
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

// glob to RegExp (supports **, *, literal)
function globToRe(g) {
  const re = g.replace(/[.+^${}()|[\]\\]/g, "\\$&")
    .replace(/\*\*\//g, "@@DS@@").replace(/\*\*/g, "@@SS@@").replace(/\*/g, "[^/]*")
    .replace(/@@DS@@/g, "(?:.*/)?").replace(/@@SS@@/g, ".*");
  return new RegExp("^" + re + "$", "i");
}

// harness.config.json (missing/broken file -> defaults; hooks stay fail-open)
function loadConfig(root) {
  let c = {};
  try { c = JSON.parse(fs.readFileSync(path.join(root, "harness.config.json"), "utf8")); } catch {}
  const ui = c.ui || {};
  const uiDisabled = c.capabilities && c.capabilities.ui === "none";
  return {
    uiGlobs: uiDisabled ? [] : (ui.globs || DEFAULT_UI_GLOBS),
    uiExclude: ui.exclude || DEFAULT_UI_EXCLUDE,
    mockups: { ...DEFAULT_MOCKUPS, ...(ui.mockups || {}) },
    protected: (c.protected && c.protected.paths) || DEFAULT_PROTECTED,
    lintConfigs: (c.protected && c.protected.lintConfigs) || DEFAULT_LINT_CONFIGS,
  };
}

function isUiPath(rel, cfg) {
  const included = (cfg.uiGlobs || DEFAULT_UI_GLOBS).map(globToRe).some((re) => re.test(rel));
  const excluded = (cfg.uiExclude || DEFAULT_UI_EXCLUDE).map(globToRe).some((re) => re.test(rel));
  return included && !excluded;
}

// Absolute/relative path -> normalized repo-relative posix path.
// Collapses ./ and ..; strips the project prefix case-insensitively - WITHOUT
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
// Covers write/delete/move verbs across POSIX, cmd, and PowerShell, plus sed/perl
// in-place edits and > / >> redirection. Windows verbs matter because this repo is
// commonly driven from PowerShell/cmd; without them, commands such as
// `del hooks\x.js` or `Remove-Item lefthook.yml` would bypass protection.
// Verbs match only when the target is a protected path, so ordinary commands and
// read-only calls (cat/ls/node hooks/x.js) pass through.
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

function normalizePathFragments(text) {
  return String(text || "").replace(
    /[A-Za-z0-9_.-]+(?:[\/\\]+[A-Za-z0-9_.-]+)+/g,
    (frag) => path.posix.normalize(frag.replace(/\\/g, "/"))
  );
}

// SEP is a path separator: `/` or `\`. PowerShell/cmd often use backslashes, so
// `del hooks\agent\guard.js` must match the same way as `rm hooks/...`.
const SEP = "[\\/\\\\]";

// Writes to protected harness paths, expressed as repo-root prefixes.
function isProtectedShellWrite(scrubbed, protectedList) {
  const scanned = normalizePathFragments(scrubbed);
  // Order matters: replace slashes with SEP before escaping dots. Otherwise dot
  // escaping inserts backslashes that the slash replacement would later corrupt.
  const esc = (str) => str.replace(/[\/\\]/g, SEP).replace(/\./g, "\\.");
  // "hooks/" protects both "hooks/x" and a bare "rm -rf hooks"; files use word boundaries.
  const alt = "(?:\\." + SEP + ")?(?:" + protectedList.map((p) =>
    p.endsWith("/") ? esc(p.slice(0, -1)) + "(?:" + SEP + "|[\\s;&|]|$)" : esc(p) + "\\b"
  ).join("|") + ")";
  // Lookbehind without a path separator keeps src/hooks/useAuth.ts from matching.
  return shellWriteHit(scanned, alt, "(?<=^|[\\s=:'\"(])", false);
}

// Writes to lint/format config files by basename, in any directory, with / or \.
function isLintConfigShellWrite(scrubbed, lintConfigs) {
  const scanned = normalizePathFragments(scrubbed);
  const names = lintConfigs.map((n) => n.replace(/\./g, "\\.")).join("|");
  const alt = `(?:[^\\s;|&<>]*${SEP})?(?:${names})\\b`;
  return shellWriteHit(scanned, alt, "(?<=^|[\\s=:'\"(/\\\\])", true);
}

// rel is normalized; compare by basename, case-insensitively.
function isLintConfigPath(rel, lintConfigs) {
  const base = String(rel).toLowerCase().split("/").pop();
  return lintConfigs.some((n) => n.toLowerCase() === base);
}

// ---------- protected-path writes through inline interpreter eval ----------
// Commands such as `node -e "fs.writeFileSync('hooks/x')"`,
// `python -c "open('lefthook.yml','w')"`, or `bash -c "rm -rf hooks/"`
// hide the write verb/path inside a quoted program, so scrubbed shell-write
// detection cannot see them. Inspect the raw command and block only when the
// command is eval-like, has a write indicator, and references a protected path.
// Encoded PowerShell is intentionally blocked as opaque eval: the write/path
// cannot be inspected before execution.
const INTERP_EVAL_RE = /\b(?:node|nodejs|deno|bun|python|python3|py|perl|ruby|php|pwsh|powershell|bash|sh|zsh)\b[^\n]*?(?:\s-e\b|\s--eval\b|\s-c\b|\seval\b|\s-Command\b|\s-EncodedCommand\b)/i;
const INTERP_ENCODED_RE = /\b(?:pwsh|powershell)\b[^\n]*\s-EncodedCommand\b/i;
const INTERP_WRITE_RE = /writefile|writefilesync|appendfile|createwritestream|write_text|write_bytes|unlink\s*\(|rmtree\s*\(|\brm(?:sync)?\s*\(|remove\s*\(|replace\s*\(|rename\s*\(|shutil\.(?:rmtree|move)|os\.(?:remove|unlink|replace|rename)|path\([^)]*\)\.(?:write_text|write_bytes|unlink)|(?:fs|file)\.write|\[\s*['"]write|['"]write['"]\s*\+|\+\s*['"]filesync['"]|open\s*\([^)]*['"][aw]|set-content|add-content|out-file|>{1,2}|\b(?:rm|del|erase|move|mv|remove-item|ren|rename)\b/i;
function interpreterProtectedHint(rawCmd, protectedList) {
  const s = String(rawCmd);
  if (!INTERP_EVAL_RE.test(s)) return null;
  if (INTERP_ENCODED_RE.test(s)) return "encoded-command";
  if (!INTERP_WRITE_RE.test(s)) return null;
  const low = normalizePathFragments(s.replace(/\\/g, "/").toLowerCase());
  for (const p of protectedList) {
    const pref = p.toLowerCase().replace(/\/$/, "").replace(/[.]/g, "\\.");
    if (new RegExp("(?:^|[\\s'\"(=:,])(?:\\.\\/)?" + pref + "(?:/|\\b)").test(low)) return p;
  }
  return null;
}

// ---------- changed files (branch/worktree diff) ----------
// Changed files in the branch relative to a base. Returns {files, base} when diff
// succeeds, even if the diff is empty, or {error} when no base can be used. Those
// outcomes are intentionally distinct: empty diff means "no changes", while error
// means "could not check". Silent fail-open would make gates ineffective in repos
// without the expected base. explicitFiles is normalized and returned without git.
// By default this is a branch-only contract for CI/design-gate; local verify can
// explicitly include dirty/staged/untracked files via includeDirty.
function changedFiles(base, root, explicitFiles, opts = {}) {
  if (explicitFiles) {
    const files = normalizeChangedFiles(root, explicitFiles);
    return { files, explicit: true, branchFiles: files, dirtyFiles: [], includeDirty: false };
  }
  const remoteFirst = /^origin\//.test(String(base || ""));
  const fallbacks = remoteFirst
    ? [base, "origin/HEAD", "main", "master"]
    : [base, "main", "master", "origin/main", "origin/HEAD"];
  const bases = [...new Set(fallbacks.filter(Boolean))];
  for (const b of bases) {
    for (const args of [["diff", "--name-only", `${b}...HEAD`], ["diff", "--name-only", b]]) {
      try {
        const out = execFileSync("git", args, { cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"], timeout: 5000, killSignal: "SIGKILL" });
        const branchFiles = parseFiles(out, root);
        const worktreeFiles = opts.includeDirty ? dirtyFiles(root) : [];
        return {
          files: opts.includeDirty ? mergeFiles(root, branchFiles, worktreeFiles) : branchFiles,
          base: b,
          explicit: false,
          branchFiles,
          dirtyFiles: worktreeFiles,
          includeDirty: !!opts.includeDirty,
        };
      } catch {}
    }
  }
  return { error: `git diff failed for every base (${bases.join(", ")})` };
}

function workingTreeChangedFiles(base, root, explicitFiles) {
  return changedFiles(base, root, explicitFiles, { includeDirty: true });
}

function normalizeChangedFile(root, fp) {
  const rel = normRel(fp, root);
  if (!rel || rel === "." || rel === ".." || rel.startsWith("../")) return "";
  if (/^(?:[A-Za-z]:)?\//.test(rel) || /^[A-Za-z]:\//.test(rel)) return "";
  return rel;
}

function normalizeChangedFiles(root, files) {
  return mergeFiles(root, files);
}

function parseFiles(out, root) {
  return normalizeChangedFiles(root, String(out || "").split(/\r?\n/));
}

function gitFiles(root, args) {
  try {
    return parseFiles(execFileSync("git", args, {
      cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
      timeout: 5000, killSignal: "SIGKILL",
    }), root);
  } catch {
    return [];
  }
}

function dirtyFiles(root) {
  return mergeFiles(root,
    gitFiles(root, ["diff", "--name-only"]),
    gitFiles(root, ["diff", "--name-only", "--cached"]),
    gitFiles(root, ["ls-files", "--others", "--exclude-standard"])
  );
}

function mergeFiles(root, ...lists) {
  const out = [];
  const seen = new Set();
  for (const list of lists) {
    for (const f of list || []) {
      const rel = normalizeChangedFile(root, f);
      if (!rel || seen.has(rel)) continue;
      seen.add(rel);
      out.push(rel);
    }
  }
  return out;
}

function doctorEnvironmentReady(report) {
  return Boolean(report && report.ok === true && report.blocked !== true && Number(report.envs || 0) === 0);
}

module.exports = {
  DEFAULT_UI_GLOBS, DEFAULT_UI_EXCLUDE, DEFAULT_MOCKUPS, DEFAULT_PROTECTED, DEFAULT_LINT_CONFIGS,
  globToRe, loadConfig, isUiPath, normRel, isProtectedPath,
  isProtectedShellWrite, isLintConfigShellWrite, isLintConfigPath,
  interpreterProtectedHint,
  changedFiles, workingTreeChangedFiles,
  doctorEnvironmentReady,
};
