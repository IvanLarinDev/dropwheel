#!/usr/bin/env node
// stop-reminder.js - Stop hook. Reminds about VERIFY / COMMIT / REPORT only when
// the working tree has uncommitted changes; clean tree stays quiet.
//
// Claude Code Stop hooks do not support additionalContext. The only way to
// deliver text is {"decision":"block","reason":"..."}. If the runner sends
// stop_hook_active=true, this hook stays quiet to avoid an infinite stop loop.

const { execSync } = require("child_process");
const crypto = require("crypto");
const fs = require("fs");
const os = require("os");
const path = require("path");
const taskState = require(path.join(__dirname, "..", "task-state.js"));

function stateFile(cwd) {
  const id = crypto.createHash("sha1").update(String(cwd)).digest("hex").slice(0, 12);
  return path.join(os.tmpdir(), `harness-stop-reminder-${id}.json`);
}
function statusHash(status) {
  return crypto.createHash("sha1").update(String(status)).digest("hex");
}
function readState(file) {
  try { return JSON.parse(fs.readFileSync(file, "utf8")); }
  catch { return {}; }
}
function writeState(file, state) {
  const tmp = file + "." + process.pid + ".tmp";
  try {
    fs.writeFileSync(tmp, JSON.stringify({ ...state, ts: Date.now() }));
    fs.renameSync(tmp, file);
  } catch {
    try { fs.unlinkSync(tmp); } catch {}
  }
}
function rawString(v) {
  if (typeof v === "string") return v;
  if (Array.isArray(v)) return v.map(rawString).join("\n");
  if (v && typeof v === "object") return Object.values(v).map(rawString).join("\n");
  return "";
}
function transcriptPath(raw) {
  return rawString(raw && (raw.transcript_path || raw.transcriptPath || raw.transcript));
}
function lastAssistantTextFromTranscript(file) {
  if (!file || !fs.existsSync(file)) return "";
  let last = "";
  try {
    const lines = fs.readFileSync(file, "utf8").split(/\r?\n/).filter(Boolean);
    for (const line of lines) {
      let obj;
      try { obj = JSON.parse(line); } catch { continue; }
      const role = rawString(obj.role || obj.type || (obj.message && obj.message.role)).toLowerCase();
      if (!/assistant/.test(role)) continue;
      const text = rawString(obj.content || obj.message || obj.text || obj.parts);
      if (text.trim()) last = text;
    }
  } catch {}
  return last;
}
function explainedIntentionalDirty(text) {
  const s = String(text || "").toLowerCase();
  if (!s) return false;
  const mentionsDirty = /dirty tree|uncommitted|working tree|left over|left intentionally/.test(s);
  const intentional = /intentional|intentionally|left on purpose|not touched|left unchanged/.test(s);
  const reportsLoop = /verify|verified|self-review|diff|commit|report/.test(s);
  const reviewOnly = /review-only|review only|did not edit|did not commit|no commit\/pr|commit\/pr not created/.test(s);
  return (mentionsDirty && intentional && reportsLoop) || (reviewOnly && reportsLoop);
}
function isHarnessOrLocalStatus(line) {
  const p = line.replace(/\\/g, "/").replace(/^\S\S\s+/, "");
  return /^(?:\.github\/rulesets\/|hooks\/|\.claude\/|\.codex\/|AGENTS\.md|lefthook\.yml|harness\.config\.json|settings\.example\.json|cog\.toml|\.gitleaks\.toml|\.gitattributes|\.gitignore)$/.test(p);
}

(async () => {
  let cwd = process.cwd();
  let stopHookActive = false;
  let raw = {};
  try {
    const { parse } = require(path.join(__dirname, "_input.js"));
    const ctx = await parse();
    if (ctx.projectDir) cwd = ctx.projectDir;
    stopHookActive = ctx.stopHookActive;
    raw = ctx.raw || {};
  } catch {}
  if (stopHookActive) process.exit(0); // already reminded during this Stop; avoid loops

  let status = "";
  try {
    status = execSync("git status --porcelain", { cwd, stdio: ["ignore", "pipe", "ignore"], timeout: 3000, killSignal: "SIGKILL" })
      .toString().trim();
  } catch {}
  if (!status) process.exit(0);
  // `task start` records pre-existing dirt. Do not interrupt a read-only task
  // when that exact baseline is still present and nothing new was introduced.
  if (taskState.unchangedFromBaseline(cwd)) process.exit(0);

  const file = stateFile(cwd);
  const hash = statusHash(status);
  const state = readState(file);
  if (state.hash === hash && state.reminded === true) process.exit(0);
  writeState(file, { hash, reminded: true });

  const lines = status.split("\n");
  const harnessLike = lines.filter(isHarnessOrLocalStatus);
  if (harnessLike.length === lines.length) {
    const lastAssistantText = lastAssistantTextFromTranscript(transcriptPath(raw));
    if (explainedIntentionalDirty(lastAssistantText)) {
      writeState(file, { hash, reminded: true, explained: true });
      process.exit(0);
    }
  }
  const shown = lines.slice(0, 20).join("\n") + (lines.length > 20 ? `\n... ${lines.length - 20} more` : "");
  const harnessNote = harnessLike.length
    ? "\nSome dirty files look like bootstrap/harness/local files. If they are intentional, the next Stop with the same git status will be allowed.\n"
    : "\nIf the dirty tree is intentional and the report explains why, the next Stop with the same git status will be allowed.\n";
  const reason =
    "stop-reminder: uncommitted changes remain; are the loop steps complete?\n" +
    "  4. VERIFY (node hooks/verify.js + git diff review) -> 5. COMMIT on a feature branch -> 6. REPORT.\n" +
    "A commit is not always required: explicitly report why changes remain uncommitted.\n" +
    harnessNote +
    "git status (first lines):\n" + shown;
  try { process.stdout.write(JSON.stringify({ decision: "block", reason }) + "\n"); } catch {}
  process.stderr.write(reason + "\n");
  process.exit(0);
})();
