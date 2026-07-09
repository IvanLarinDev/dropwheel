// _input.js - runtime-agnostic input normalizer for the agent-adapter hooks.
//
// Different LLM harnesses describe a pending tool call with different field names.
// These hooks read a JSON payload on stdin and normalize it, so the SAME hook works
// under Claude Code, a custom Agent SDK loop, or any runner that can pipe JSON and
// react to the exit code.
//
// Normalized contract returned by parse():
//   { tool, command, filePath, sessionId, projectDir, stopHookActive, raw }
//
// Exit-code convention (documented for all runtimes):
//   exit 0  = allow
//   exit 2  = BLOCK the pending tool call (Claude Code PreToolUse "deny")
//   stderr text = human-readable reason (shown by most runners)
//
// Non-blocking note (see note()): Claude Code reads additionalContext ONLY from
//   {"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"..."}}
// - a bare top-level {"additionalContext": "..."} is ignored. We emit BOTH shapes
// (top-level for simpler runners, hookSpecificOutput for Claude Code) + stderr mirror.

function readStdin() {
  // Read stdin without a hard idle deadline. The main completion signal is `end`.
  // On idle, finish only if the buffer already parses as complete JSON; otherwise
  // wait for the rest of a bursty payload until the cap. Size cap sets truncated,
  // and guard.js decides fail-closed. Empty idle waits until the cap for manual runs.
  const IDLE_MS = 300, CAP_MS = 5000, MAX_BYTES = 2 * 1024 * 1024;
  return new Promise((resolve) => {
    let data = "", done = false, idle = null, truncated = false;
    const finish = () => {
      if (done) return;
      done = true; clearTimeout(idle); clearTimeout(cap);
      resolve({ data, truncated });
    };
    const cap = setTimeout(finish, CAP_MS);
    const onIdle = () => {
      if (truncated) return finish();       // size cap hit; do not wait longer
      if (!data) return arm();              // no data yet; wait until cap
      try { JSON.parse(data); finish(); }   // complete JSON; finish
      catch { arm(); }                      // incomplete JSON; wait for more
    };
    const arm = () => { clearTimeout(idle); idle = setTimeout(onIdle, IDLE_MS); };
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (c) => {
      const room = MAX_BYTES - data.length;
      if (room <= 0 || c.length > room) truncated = true;
      if (room > 0) data += c.slice(0, room);
      arm();
    });
    process.stdin.on("end", finish);
    process.stdin.on("error", finish);
    arm();
  });
}

function firstString(...vals) {
  for (const v of vals) if (typeof v === "string" && v.length) return v;
  return "";
}

async function parse() {
  const { data, truncated } = await readStdin();
  let raw = {};
  let parseError = false;
  try { raw = JSON.parse(data || "{}"); }
  catch { parseError = data.trim().length > 0; } // empty stdin = manual run, not an error
  const ti = raw.tool_input || raw.toolInput || raw.input || raw.arguments || {};
  return {
    tool: firstString(raw.tool_name, raw.toolName, raw.tool, raw.name),
    command: firstString(ti.command, raw.command),
    filePath: firstString(
      ti.file_path,
      ti.path,
      ti.filePath,
      ti.filename,
      ti.file,
      ti.target_file,
      ti.targetFile,
      ti.notebook_path,
      ti.notebookPath,
      raw.file_path,
      raw.path,
      raw.filePath,
      raw.filename,
      raw.file,
      raw.target_file,
      raw.targetFile,
      raw.notebook_path,
      raw.notebookPath
    ),
    sessionId: firstString(
      process.env.HARNESS_SESSION_ID,
      process.env.CLAUDE_SESSION_ID,
      process.env.ZCODE_SESSION_ID,
      raw.session_id,
      raw.sessionId
    ),
    projectDir: firstString(
      process.env.HARNESS_PROJECT_DIR,
      process.env.CLAUDE_PROJECT_DIR,
      process.env.ZCODE_PROJECT_DIR,
      raw.cwd,
      process.cwd()
    ),
    stopHookActive: raw.stop_hook_active === true || raw.stopHookActive === true,
    truncated,
    parseError,
    raw,
  };
}

// Non-blocking note injected into agent context. eventName: "PreToolUse" | "PostToolUse" | ...
function note(text, eventName = "PreToolUse") {
  try {
    process.stdout.write(JSON.stringify({
      additionalContext: text, // simpler runners
      hookSpecificOutput: { hookEventName: eventName, additionalContext: text }, // Claude Code
    }) + "\n");
  } catch {}
  process.stderr.write(text + "\n");
  process.exit(0);
}

function block(text) {
  process.stderr.write(text + "\n");
  process.exit(2);
}

module.exports = { parse, note, block, firstString };
