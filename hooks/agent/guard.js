#!/usr/bin/env node
// guard.js - unified agent-adapter hook for PreToolUse shell/file/read calls.
//
// Architecture: all policy lives in exported synchronous
// run(ctx, env) -> { exitCode, stdout, stderr }, with no process.exit and no
// output side effects. The CLI wrapper at the bottom only reads stdin and applies
// the result. Tests and dispatchers can call run() in-process without spawn cost.
//
// BLOCK (exit 2):
//   - harness bypass: --no-verify / git commit -n, core.hooksPath, LEFTHOOK=0,
//     lefthook uninstall, and writes to .git/hooks;
//   - writes to harness files through file tools and shell commands;
//   - edits to existing lint/format config files;
//   - degenerate loops, leaked tool markup, low-entropy commands;
//   - truncated or unreadable payloads.
// NOTE (exit 0 + additionalContext):
//   - git commit/merge/push or file writes on main/master;
//   - UI file edits that need the DESIGN stage;
//   - fact-force: editing an existing file that was not read in this session.
//
// Strictness profiles are runner/user environment controls, not agent commands.
// Approved bypass details are not printed in block messages; see AGENTS.md.
// Internal hook errors fail open, but are made visible on stderr.

const fs = require("fs");
const os = require("os");
const path = require("path");
const crypto = require("crypto");
const { execSync } = require("child_process");
const { parse } = require(path.join(__dirname, "_input.js"));
const { loadConfig, isUiPath, normRel, isProtectedPath, isProtectedShellWrite,
  isLintConfigShellWrite, isLintConfigPath, interpreterProtectedHint } = require(path.join(__dirname, "..", "_lib.js"));

const TTL_MS = 2 * 60 * 60 * 1000;
const SEEN_MAX = 200;
const BYPASS_HINT = "Bypass requires explicit user approval; ask the user first. The escape hatch is documented in AGENTS.md.";

// ---------- profiles ----------
const ALL_CHECKS = ["bypass", "protected", "lintconfig", "corruption", "entropy", "loops", "main-note", "design-note", "fact-force"];
const PROFILES = {
  minimal: new Set(["bypass", "protected"]),
  standard: new Set(ALL_CHECKS),
  strict: new Set(ALL_CHECKS),
};
function getProfile(env) {
  const p = String(env.HARNESS_PROFILE || "standard").trim().toLowerCase();
  return PROFILES[p] ? p : "standard";
}
function checkEnabled(id, env) {
  const off = String(env.HARNESS_DISABLED_CHECKS || "").split(",").map((s) => s.trim().toLowerCase());
  if (off.includes(id)) return false;
  return PROFILES[getProfile(env)].has(id);
}
function envAllow(env, name) {
  return ["1", "true", "yes", "on"].includes(String(env[name] || "").trim().toLowerCase());
}

// ---------- result object, no process.exit inside policy logic ----------
function allowRes(notes) {
  if (!notes || !notes.length) return { exitCode: 0, stdout: "", stderr: "" };
  const text = notes.join("\n");
  return {
    exitCode: 0,
    stdout: JSON.stringify({
      additionalContext: text, // simple runners
      hookSpecificOutput: { hookEventName: "PreToolUse", additionalContext: text }, // Claude Code
    }) + "\n",
    stderr: text + "\n",
  };
}
function blockRes(text) {
  return { exitCode: 2, stdout: "", stderr: text + "\n" };
}

// ---------- state (per session, in tmpdir) ----------
function stateFile(sessionId, projectDir) {
  const id = sessionId || "proj-" + crypto.createHash("sha1").update(String(projectDir)).digest("hex").slice(0, 12);
  return path.join(os.tmpdir(), `harness-guard-${id}.json`);
}
function readState(p) {
  try {
    const s = JSON.parse(fs.readFileSync(p, "utf8"));
    if (Date.now() - s.ts > TTL_MS) return { hist: [], streak: 0, seen: [] };
    return { hist: s.hist || [], streak: s.streak || 0, seen: s.seen || [] };
  } catch { return { hist: [], streak: 0, seen: [] }; }
}
// Atomic write: temp file in the same directory plus rename. Parallel hooks can
// otherwise leave truncated JSON or lose history during read-modify-write races.
function writeState(p, s) {
  const tmp = p + "." + process.pid + ".tmp";
  try {
    fs.writeFileSync(tmp, JSON.stringify({ ...s, ts: Date.now() }));
    fs.renameSync(tmp, p);
  } catch { try { fs.unlinkSync(tmp); } catch {} }
}
function markSeen(st, rel) {
  if (!st.seen.includes(rel)) {
    st.seen.push(rel);
    if (st.seen.length > SEEN_MAX) st.seen.shift();
  }
}

function currentBranch(cwd) {
  try {
    const b = execSync("git symbolic-ref --short HEAD", { cwd, stdio: ["ignore", "pipe", "ignore"], timeout: 2000, killSignal: "SIGKILL" }).toString().trim();
    return b === "HEAD" ? "" : b;
  } catch { return ""; }
}

// ---------- detectors ----------
// Narrow tool-call markup pattern; generic HTML such as <toolbar> stays valid.
const CORRUPTION_RE = /<\/?(?:tool_?(?:call|use|result)|function_call|invoke|antml)[\s>_:/]|^\s*["']\s*,?\s*\d*\s*</i;
function isTrivial(cmd) {
  const c = cmd.trim();
  if (!c) return true;
  if (/^echo\s+(['"]?)([^\s|;&"'{}]{1,8})\1$/.test(c)) return true;
  if (/^ls(\s+-{1,2}[a-zA-Z-]+)*\s*$/.test(c)) return true;
  if (/^(pwd|true|false|clear|date|:)\s*$/.test(c)) return true;
  return false;
}
function isLowEntropy(cmd) {
  const t = cmd.trim().split(/\s+/).filter(Boolean);
  if (t.length < 8) return false;
  return new Set(t).size / t.length < 0.35;
}
function tailRepeat(h) {
  if (!h.length) return 0;
  const k = h[h.length - 1];
  let n = 0;
  for (let i = h.length - 1; i >= 0 && h[i] === k; i--) n++;
  return n;
}
function tailAlt(h) {
  if (h.length < 4) return 0;
  const a = h[h.length - 1], b = h[h.length - 2];
  if (a === b) return 0;
  let n = 0;
  for (let i = h.length - 1; i >= 0; i--) {
    if (h[i] === ((h.length - 1 - i) % 2 === 0 ? a : b)) n++;
    else break;
  }
  return n;
}
function loopCheck(st, p, T) {
  const rep = tailRepeat(st.hist);
  if (rep >= T) {
    writeState(p, { hist: [], streak: 0, seen: st.seen });
    return blockRes(`guard: same action repeated ${rep} times; this looks like a loop.\n` +
      `   ${st.hist[st.hist.length - 1].slice(0, 120)}\n` +
      `   Stop, compare with the plan/TodoWrite, and change approach. Threshold: ${T}.`);
  }
  const alt = tailAlt(st.hist);
  if (alt >= 2 * T) {
    writeState(p, { hist: [], streak: 0, seen: st.seen });
    return blockRes(`guard: two actions alternated for ${alt} steps (A-B-A-B); this is a no-progress loop.\n` +
      `   Stop, compare with the plan/TodoWrite, and change approach. Threshold: ${2 * T}.`);
  }
  return null;
}

// ---------- harness bypass through shell ----------
// Windows can call git.exe/git.cmd. For git commit, -n means --no-verify.
const GIT = "git(?:\\.exe|\\.cmd)?";
const BYPASS = [
  { re: new RegExp(`\\b${GIT}\\s+commit\\b[^\\n]*(?:\\s--no-verify\\b|\\s-[a-z]*n[a-z]*\\b)`, "i"),
    why: "--no-verify / -n on git commit skips pre-commit and commit-msg hooks" },
  { re: new RegExp(`\\b${GIT}\\s+(merge|push)\\b[^\\n]*\\s--no-verify\\b`, "i"),
    why: "--no-verify on git merge/push skips hooks" },
  { re: new RegExp(`\\b${GIT}\\b[^\\n]*\\bcore\\.hookspath\\b`, "i"),
    why: "core.hooksPath disables or replaces git hooks" },
  { re: /\blefthook\s+uninstall\b/i,
    why: "lefthook uninstall removes git hooks" },
  { re: /(^|[\s;&|])LEFTHOOK\s*=\s*(0|false)\b/i,
    why: "LEFTHOOK=0 disables lefthook hooks; this is a human escape hatch, not an agent escape hatch" },
];
function isGitHooksWrite(scrubbed) {
  return /\.git[\/\\]hooks\b/i.test(scrubbed) &&
    /(^|[\s;&|])(rm|mv|cp|tee|chmod|ln|truncate|sed|del|erase|rmdir|rd|move|ren|rename|copy|Remove-Item|Move-Item|Rename-Item|Copy-Item|Set-Content|Add-Content|Clear-Content|Out-File|New-Item)\b|>/i.test(scrubbed);
}
// Blank quoted strings so commit messages mentioning -n do not look like bypasses.
function scrubQuotes(cmd) {
  return cmd.replace(/"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'/g, '""');
}
function scrubGitMessageArgs(cmd) {
  return String(cmd).replace(
    new RegExp(`(\\b${GIT}\\s+commit\\b[^\\n]*?\\s(?:-m|--message)\\s+)(?:"(?:[^"\\\\]|\\\\.)*"|'(?:[^'\\\\]|\\\\.)*')`, "gi"),
    "$1\"\""
  );
}
function unquoteShellPaths(cmd) {
  return String(cmd).replace(/"([^"\\]*(?:\\.[^"\\]*)*)"|'([^'\\]*(?:\\.[^'\\]*)*)'/g, (m, dq, sq) =>
    (dq !== undefined ? dq : sq).replace(/\\(["'\\])/g, "$1")
  );
}

// ---------- main policy ----------
function run(ctx, env = process.env) {
  try {
    // Fail closed on truncated or unreadable payloads. Empty stdin for manual
    // runs remains fail-open in parse().
    if (ctx.truncated || ctx.parseError)
      return blockRes("guard: input payload is truncated or unreadable; policy cannot decide blind.\n" +
        "   Retry with a smaller change or provide the full payload.");

    const { tool, command, filePath, sessionId, projectDir } = ctx;
    const isShell = /^(bash|shell|sh|exec|run|terminal|execute)/i.test(tool) || (!tool && command);
    const isFile = /^(write|edit|multiedit|applypatch|create|str_replace|notebookedit)/i.test(tool);
    const isRead = /^read/i.test(tool);

    // Loop thresholds; strict mode halves them.
    let T_SH = Number(env.HARNESS_LOOP_THRESHOLD) || 5;
    let T_FT = Number(env.HARNESS_TOOLLOOP_THRESHOLD) || 12;
    if (getProfile(env) === "strict") { T_SH = Math.max(2, Math.ceil(T_SH / 2)); T_FT = Math.max(3, Math.ceil(T_FT / 2)); }
    const HIST_MAX = 2 * Math.max(T_SH, T_FT) + 4;

    const p = stateFile(sessionId, projectDir);
    const st = readState(p);
    const cfg = loadConfig(projectDir);
    const notes = [];

    if (isShell && typeof command === "string" && command) {
      const scrubbed = scrubQuotes(command);
      const pathScan = unquoteShellPaths(scrubGitMessageArgs(command));

      // 1) harness bypass or shell writes to protected paths and lint configs
      const hit =
        (checkEnabled("bypass", env) &&
          (BYPASS.find((b) => b.re.test(scrubbed)) ||
            (isGitHooksWrite(pathScan) ? { why: "direct write/delete under .git/hooks" } : null))) ||
        (checkEnabled("protected", env) && isProtectedShellWrite(pathScan, cfg.protected)
          ? { why: "shell write to harness files (hooks/, configs, workflows)" } : null) ||
        (checkEnabled("lintconfig", env) && isLintConfigShellWrite(pathScan, cfg.lintConfigs)
          ? { why: "shell write to lint/format config; fix code instead of weakening config" } : null);
      if (hit) {
        if (envAllow(env, "HARNESS_ACK_BYPASS")) {
          notes.push(`guard: harness bypass was explicitly approved by the user: ${hit.why}. Explain this in the report.`);
        } else {
          return blockRes(`guard: command bypasses the harness; blocked.\n   Reason: ${hit.why}.\n   ${BYPASS_HINT}`);
        }
      }

      // 1c) writes to harness files through inline interpreter eval.
      if (checkEnabled("protected", env)) {
        const ip = interpreterProtectedHint(command, cfg.protected);
        if (ip) {
          if (envAllow(env, "HARNESS_ACK_BYPASS")) {
            notes.push(`guard: inline-eval write to harness file (${ip}) was explicitly approved by the user. Explain this in the report.`);
          } else {
            return blockRes(`guard: command looks like an inline-eval write to a harness file (${ip}).\n` +
              `   Reason: node -e / python -c / bash -c hides the write from normal shell detection.\n   ${BYPASS_HINT}`);
          }
        }
      }

      // 2) stream corruption / junk
      if (checkEnabled("corruption", env) && CORRUPTION_RE.test(scrubbed)) {
        writeState(p, { hist: [], streak: 0, seen: st.seen });
        return blockRes(`guard: leaked tool markup in command; this usually means streaming/parsing corruption.\n   ${JSON.stringify(command.slice(0, 120))}`);
      }
      if (checkEnabled("entropy", env) && isLowEntropy(command)) {
        writeState(p, { hist: [], streak: 0, seen: st.seen });
        return blockRes(`guard: command token entropy is abnormally low.\n   ${JSON.stringify(command.slice(0, 120))}`);
      }

      // 3) loops
      if (checkEnabled("loops", env)) {
        st.streak = isTrivial(command) ? st.streak + 1 : 0;
        st.hist.push("sh::" + command.trim());
        if (st.hist.length > HIST_MAX) st.hist.shift();
        if (st.streak >= T_SH) {
          writeState(p, { hist: [], streak: 0, seen: st.seen });
          return blockRes(`guard: ${st.streak} trivial commands in a row; this is a degenerate pattern.\n` +
            `   Stop and make one meaningful step. Threshold: HARNESS_LOOP_THRESHOLD=${T_SH}.`);
        }
        const lr = loopCheck(st, p, T_SH);
        if (lr) return lr;
        writeState(p, st);
      }

      // 4) early notes about protected branches
      if (checkEnabled("main-note", env)) {
        const branch = currentBranch(projectDir);
        if (["main", "master"].includes(branch) && new RegExp(`\\b${GIT}\\s+(commit|merge)\\b`).test(scrubbed))
          notes.push(`guard: git commit/merge on ${branch} will be rejected by pre-commit. Switch to a feature branch; releases use HARNESS_ALLOW_MAIN=1.`);
        if (new RegExp(`\\b${GIT}\\s+push\\b`).test(scrubbed) && /\b(main|master)\b/.test(scrubbed) && !/refs\/tags|v\d/.test(scrubbed))
          notes.push(`guard: direct push to main/master will be rejected by the server ruleset. Update main through a PR.`);
      }
      return allowRes(notes);
    }

    if ((isFile || isRead) && filePath) {
      const rel = normRel(filePath, projectDir);
      const abs = path.isAbsolute(String(filePath)) ? String(filePath) : path.join(String(projectDir), rel);

      // 1) harness files require explicit approval
      if (isFile && checkEnabled("protected", env) && isProtectedPath(rel, cfg.protected)) {
        if (envAllow(env, "HARNESS_ACK_BYPASS")) {
          notes.push(`guard: harness file edit (${rel}) was explicitly approved by the user.`);
        } else {
          return blockRes(`guard: harness file edit blocked: ${rel}\n` +
            `   Agents must not change harness hooks/configs without approval.\n   ${BYPASS_HINT}`);
        }
      }

      // 1b) existing target lint/format configs: block edits, allow new files.
      if (isFile && checkEnabled("lintconfig", env) &&
          !isProtectedPath(rel, cfg.protected) && isLintConfigPath(rel, cfg.lintConfigs)) {
        let exists = true;
        try { fs.lstatSync(abs); }
        catch (e) { if (e && e.code === "ENOENT") exists = false; } // other errors fail closed
        if (exists) {
          if (envAllow(env, "HARNESS_ACK_BYPASS")) {
            notes.push(`guard: lint config edit (${rel}) was explicitly approved by the user.`);
          } else {
            return blockRes(`guard: existing lint/format config edit blocked: ${rel}\n` +
              `   Fix failing gates in code instead of weakening config.\n` +
              `   Creating a new config file from scratch is allowed. ${BYPASS_HINT}`);
          }
        }
      }

      // 2) fact-force note: editing an existing file before reading it.
      if (isRead) markSeen(st, rel);
      if (isFile && checkEnabled("fact-force", env) && !st.seen.includes(rel)) {
        let exists = false;
        try { fs.lstatSync(abs); exists = true; } catch {}
        if (exists)
          notes.push(`guard: editing ${rel} before reading it in this session (EXPLORE -> IMPLEMENT). ` +
            `Read the file or its call sites before editing.`);
        markSeen(st, rel); // new files are considered known after this point
      }

      // 3) file-tool loops
      if (checkEnabled("loops", env)) {
        st.streak = 0;
        st.hist.push(tool.toLowerCase() + "::" + rel);
        if (st.hist.length > HIST_MAX) st.hist.shift();
        const lr = loopCheck(st, p, T_FT);
        if (lr) return lr;
      }
      writeState(p, st);

      // 4) notes: DESIGN stage and protected branches
      if (isFile) {
        const mockRoot = cfg.mockups.dir.replace(/\\/g, "/").replace(/\/$/, "");
        if (checkEnabled("design-note", env) &&
            !rel.startsWith(mockRoot + "/") && isUiPath(rel, cfg))
          notes.push(`guard: GUI file edit (${rel}). Classify the DESIGN evidence as existing-ui, new-ui, or animation, ` +
            `then create >=${cfg.mockups.min} matching variants plus APPROVED (node hooks/new-mockups.js <feature> --kind ...). ` +
            `Backend-only diffs outside ui.globs skip automatically. The hard gate is design-gate.js in pre-push/CI.`);
        if (checkEnabled("main-note", env)) {
          const branch = currentBranch(projectDir);
          if (["main", "master"].includes(branch))
            notes.push(`guard: editing files on ${branch}. Create a feature branch first: git checkout -b feat/...`);
        }
      }
      return allowRes(notes);
    }

    return { exitCode: 0, stdout: "", stderr: "" };
  } catch (e) {
    // Fail open so the hook cannot wedge the session, but make the failure visible.
    return { exitCode: 0, stdout: "", stderr: "guard: internal policy error, allowing call (fail-open): " + (e && e.message) + "\n" };
  }
}

module.exports = { run };

// ---------- CLI wrapper ----------
if (require.main === module) {
  (async () => {
    const ctx = await parse();
    const res = run(ctx);
    if (res.stderr) process.stderr.write(res.stderr);
    if (res.stdout) process.stdout.write(res.stdout);
    process.exit(res.exitCode);
  })().catch(() => process.exit(0));
}
