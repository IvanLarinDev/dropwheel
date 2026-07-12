#!/usr/bin/env node

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync, execFileSync } = require("child_process");
const taskState = require("./task-state.js");

function loadPapercutsAnalyzer() {
  const analyzer = path.join(__dirname, "..", "scripts", "papercuts-release.js");
  if (!fs.existsSync(analyzer)) return null;
  try { return require(analyzer); }
  catch { return null; }
}

function git(root, args) {
  return execFileSync("git", args, { cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], timeout: 30000, killSignal: "SIGKILL" }).trim();
}

function gitMaybe(root, args) {
  try { return git(root, args); }
  catch { return ""; }
}

function gitRaw(root, args) {
  return execFileSync("git", args, { cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], timeout: 30000, killSignal: "SIGKILL" });
}

function repoRoot(cwd) {
  return git(cwd, ["rev-parse", "--show-toplevel"]);
}

function hasRef(root, ref) {
  try { git(root, ["rev-parse", "--verify", ref]); return true; }
  catch { return false; }
}

function parseArgs(argv) {
  const out = { command: argv[0] || "", slug: "", base: "", branch: "", dir: "", commit: "", dryRun: false, json: false, errors: [] };
  let i = 1;
  if (out.command === "start" && argv[i] && !argv[i].startsWith("--")) out.slug = argv[i++];
  for (; i < argv.length; i++) {
    const arg = argv[i];
    if (["--base", "--branch", "--dir", "--commit"].includes(arg)) {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) out.errors.push(`${arg} requires a value`);
      else out[arg.slice(2)] = argv[++i];
    } else if (arg === "--dry-run") out.dryRun = true;
    else if (arg === "--json") out.json = true;
    else out.errors.push(`unknown argument: ${arg}`);
  }
  if (!["start", "check", "finish", "status", "report"].includes(out.command)) out.errors.push("command must be start, check, finish, status, or report");
  if (out.command === "start" && !/^[a-z0-9][a-z0-9-]{0,50}$/.test(out.slug)) out.errors.push("start requires a lowercase slug (letters, digits, hyphens)");
  return out;
}

function defaultBase(root) {
  if (hasRef(root, "origin/main")) return "origin/main";
  if (hasRef(root, "main")) return "main";
  if (hasRef(root, "origin/master")) return "origin/master";
  return "HEAD";
}

function startPlan(root, args) {
  const current = git(root, ["branch", "--show-current"]);
  const base = args.base || defaultBase(root);
  const branch = args.branch || `codex/${args.slug}`;
  const protectedBranch = ["main", "master"].includes(current);
  const useCurrent = !protectedBranch && current && !args.dir && !args.branch;
  const suffix = Date.now().toString(36);
  const worktree = useCurrent ? root : path.resolve(args.dir || path.join(os.tmpdir(), `harness-${path.basename(root)}-${args.slug}-${suffix}`));
  return { root, current, base, branch: useCurrent ? current : branch, worktree, createWorktree: !useCurrent };
}

function runInherited(command, args, cwd) {
  const shell = process.platform === "win32" && /\.cmd$/i.test(command);
  const result = spawnSync(command, args, { cwd, stdio: "inherit", shell });
  if (result.error) return { ok: false, code: 1, error: result.error.message };
  return { ok: result.status === 0, code: result.status || 0, error: "" };
}

function emit(value, json) {
  if (json) console.log(JSON.stringify(value));
  else {
    console.log(`task ${value.command}: ${value.ok ? "ready" : "failed"}`);
    if (value.branch) console.log(`  branch: ${value.branch}`);
    if (value.worktree) console.log(`  worktree: ${value.worktree}`);
    if (value.base) console.log(`  base: ${value.base}`);
    if (value.health) {
      console.log(`  health: ${value.health.summary}`);
      for (const item of value.health.items || []) console.log(`    - ${item}`);
    }
    if (value.report) {
      console.log("  changed:");
      for (const item of value.report.changed) console.log(`    - ${item}`);
      console.log("  verified:");
      for (const item of value.report.verified) console.log(`    - ${item}`);
      console.log("  remaining:");
      for (const item of value.report.remaining) console.log(`    - ${item}`);
      console.log("  manual:");
      for (const item of value.report.manual) console.log(`    - ${item}`);
    }
    if (value.next) console.log(`  next: ${value.next}`);
    for (const note of value.notes || []) console.log(`  note: ${note}`);
  }
}

function statusEntries(root) {
  let raw = "";
  try { raw = gitRaw(root, ["status", "--porcelain=v1", "--untracked-files=all"]); }
  catch { raw = ""; }
  if (!raw) return [];
  return raw.split(/\r?\n/).filter(Boolean).map((line) => ({ code: line.slice(0, 2), path: line.slice(3) }));
}

function branchChanges(root, base) {
  const raw = gitMaybe(root, ["diff", "--name-status", `${base}...HEAD`]);
  if (!raw) return [];
  return raw.split(/\r?\n/).filter(Boolean);
}

function papercutsHealth(root) {
  const file = path.join(root, ".papercuts.jsonl");
  if (!fs.existsSync(file)) return { open: 0, resolved: 0, candidates: [], warnings: [] };
  const papercutsRelease = loadPapercutsAnalyzer();
  if (!papercutsRelease) {
    return { open: 0, resolved: 0, candidates: [], warnings: ["Papercuts analyzer is unavailable in this installed runtime"] };
  }
  const folded = papercutsRelease.foldLog(fs.readFileSync(file, "utf8"));
  const open = folded.items.filter((item) => item.status === "open");
  const candidates = typeof papercutsRelease.automationCandidates === "function"
    ? papercutsRelease.automationCandidates(folded, { limit: 3 })
    : [];
  return { open: open.length, resolved: folded.items.length - open.length, candidates, warnings: folded.warnings };
}

function worktreeStatus(root, args) {
  const base = args.base || defaultBase(root);
  const branch = gitMaybe(root, ["branch", "--show-current"]) || "(detached)";
  const protectedBranch = ["main", "master"].includes(branch);
  const dirty = statusEntries(root);
  const branchDiff = branchChanges(root, base);
  const baseline = taskState.loadBaseline(root);
  const preTaskDirtUnchanged = Boolean(baseline) && taskState.unchangedFromBaseline(root);
  const savedCheck = taskState.lastEvent(root, "check");
  const savedFinish = taskState.lastEvent(root, "finish");
  const savedLatestVerification = taskState.loadEvents(root)
    .filter((event) => event.kind === "check" || event.kind === "finish")
    .at(-1) || null;
  const receiptsByBase = new Map();
  const withFreshness = (event) => {
    if (!event) return null;
    const receiptBase = event.receipt && event.receipt.baseRef || event.base || base;
    let current = receiptsByBase.get(receiptBase);
    if (!current) {
      try { current = taskState.captureReceipt(root, receiptBase); }
      catch { current = null; }
      receiptsByBase.set(receiptBase, current);
    }
    return { ...event, ...taskState.receiptFreshness(root, event, current) };
  };
  const lastCheck = withFreshness(savedCheck);
  const lastFinish = withFreshness(savedFinish);
  const lastVerification = withFreshness(savedLatestVerification);
  const papercuts = papercutsHealth(root);
  const items = [];

  if (protectedBranch && dirty.length) items.push(`protected ${branch} has local dirt; keep implementation in a task worktree`);
  else if (protectedBranch) items.push(`protected ${branch} is clean enough to start a task or release worktree`);
  else items.push(`feature branch ${branch} is the active work area`);

  if (dirty.length) items.push(`${dirty.length} working-tree change(s) present`);
  else items.push("working tree is clean");
  if (branchDiff.length) items.push(`${branchDiff.length} branch file change(s) versus ${base}`);
  if (preTaskDirtUnchanged) items.push("pre-task dirt is unchanged from the saved baseline");
  if (lastCheck) items.push(lastCheck.stale
    ? `last check is stale (was ${lastCheck.ok ? "passed" : "failed"} at ${lastCheck.ts}): ${lastCheck.reasons.join(", ")}`
    : `last check ${lastCheck.ok ? "passed" : "failed"} at ${lastCheck.ts}`);
  if (lastFinish) items.push(lastFinish.stale
    ? `last finish is stale (was ${lastFinish.ok ? "passed" : "failed"} at ${lastFinish.ts}): ${lastFinish.reasons.join(", ")}`
    : `last finish ${lastFinish.ok ? "passed" : "failed"} at ${lastFinish.ts}`);
  if (papercuts.open) items.push(`${papercuts.open} open Papercuts record(s), ${papercuts.candidates.length} automation candidate group(s)`);
  if (papercuts.warnings.length) items.push(`Papercuts data warnings: ${papercuts.warnings.join("; ")}`);

  const blocked = protectedBranch && dirty.length && !preTaskDirtUnchanged;
  const summary = blocked ? "attention needed" : dirty.length || branchDiff.length ? "work in progress" : "clean";
  let next = "node hooks/task.js start <slug>";
  if (!protectedBranch && dirty.length) next = "node hooks/task.js check";
  else if (!protectedBranch && branchDiff.length) next = "node hooks/task.js finish";
  else if (protectedBranch && dirty.length) next = "review or preserve local main dirt before release cleanup";

  return {
    ok: !blocked,
    command: args.command,
    branch,
    base,
    worktree: root,
    health: { summary, items },
    status: { dirty, branchDiff, preTaskDirtUnchanged, baselineCapturedAt: baseline && baseline.capturedAt, lastCheck, lastFinish, lastVerification, papercuts },
    next,
  };
}

function report(root, args) {
  const state = worktreeStatus(root, { ...args, command: "report" });
  const dirty = state.status.dirty.map((entry) => `${entry.code.trim() || "modified"} ${entry.path}`);
  const branchDiff = state.status.branchDiff;
  const lastVerification = state.status.lastVerification;
  const candidates = state.status.papercuts.candidates || [];
  const changed = [
    branchDiff.length ? `${branchDiff.length} branch file change(s) versus ${state.base}` : "no committed branch diff versus base",
    dirty.length ? `${dirty.length} working-tree change(s): ${dirty.slice(0, 8).join(", ")}${dirty.length > 8 ? ", ..." : ""}` : "working tree clean",
  ];
  const verified = [];
  if (lastVerification) verified.push(lastVerification.stale
    ? `${lastVerification.kind} stale (was ${lastVerification.ok ? "passed" : "failed"} at ${lastVerification.ts}): ${(lastVerification.commands || []).join(" -> ")}`
    : `${lastVerification.kind} ${lastVerification.ok ? "passed" : "failed"} at ${lastVerification.ts}: ${(lastVerification.commands || []).join(" -> ")}`);
  else verified.push("no task check/finish event recorded in this worktree");
  const remaining = [];
  if (lastVerification && lastVerification.stale) {
    remaining.push("rerun task check or finish because the latest verification receipt is stale");
  }
  if (state.status.dirty.length) remaining.push("review or commit the working-tree changes");
  if (state.branch === "main" || state.branch === "master") remaining.push("start a feature/release worktree before implementation");
  if (candidates.length) {
    remaining.push(`Papercuts automation candidates: ${candidates.map((item) => `${item.label} (${item.count})`).join(", ")}`);
  }
  if (!remaining.length) remaining.push("no local task follow-up detected");
  const manual = ["for user-facing changes, include a short manual test note before final handoff"];
  return { ...state, report: { changed, verified, remaining, manual } };
}

function start(root, args) {
  const plan = startPlan(root, args);
  if (args.dryRun) return { ok: true, command: "start", ...plan, dryRun: true, notes: [] };
  if (plan.createWorktree) {
    if (fs.existsSync(plan.worktree)) return { ok: false, command: "start", ...plan, notes: ["worktree path already exists"] };
    if (hasRef(root, `refs/heads/${plan.branch}`)) return { ok: false, command: "start", ...plan, notes: ["branch already exists"] };
    git(root, ["worktree", "add", "-b", plan.branch, plan.worktree, plan.base]);
  }
  const notes = [];
  const lefthook = process.platform === "win32" ? "lefthook.cmd" : "lefthook";
  const hook = runInherited(lefthook, ["install"], plan.worktree);
  if (!hook.ok) notes.push(`lefthook activation needs attention; task finish will still run pre-commit directly${hook.error ? `: ${hook.error}` : ""}`);
  taskState.saveBaseline(plan.worktree);
  return { ok: true, command: "start", ...plan, notes, next: "node hooks/task.js check" };
}

function check(root, args) {
  const base = args.base || defaultBase(root);
  const verify = runInherited(process.execPath, [path.join(root, "hooks", "verify.js"), "--mode", "fast", "--base", base], root);
  const design = verify.ok ? runInherited(process.execPath, [path.join(root, "hooks", "design-gate.js"), "--base", base, "--advisory"], root) : { ok: false, code: verify.code };
  const result = { ok: verify.ok && design.ok, command: "check", branch: git(root, ["branch", "--show-current"]), base, worktree: root, notes: [], next: "node hooks/task.js finish", code: verify.ok ? design.code : verify.code };
  taskState.recordEvent(root, {
    kind: "check",
    ok: result.ok,
    base,
    branch: result.branch,
    commands: [`node hooks/verify.js --mode fast --base ${base}`, `node hooks/design-gate.js --base ${base} --advisory`],
    receipt: taskState.captureReceipt(root, base),
  });
  return result;
}

function commitBranch(root) {
  const branch = git(root, ["branch", "--show-current"]);
  return { branch, allowed: Boolean(branch) && !["main", "master"].includes(branch) };
}

function preTaskDirtyPaths(root) {
  return taskState.remainingBaselineDirtyPaths(root);
}

function finish(root, args) {
  const commitTarget = commitBranch(root);
  const steps = [];
  const recordFinish = (result, extra = {}) => {
    const base = extra.base || args.base || defaultBase(root);
    taskState.recordEvent(root, {
      kind: "finish",
      ok: result.ok,
      base,
      branch: result.branch || commitTarget.branch,
      commands: steps,
      notes: result.notes || [],
      receipt: taskState.captureReceipt(root, base),
    });
    return result;
  };
  if (args.commit && !commitTarget.allowed) {
    return recordFinish({
      ok: false,
      command: "finish",
      branch: commitTarget.branch,
      worktree: root,
      notes: [commitTarget.branch ? `refusing to commit on protected branch ${commitTarget.branch}` : "refusing to commit from detached HEAD"],
      code: 1,
    });
  }
  if (args.commit) {
    const preserved = preTaskDirtyPaths(root);
    if (preserved.length) {
      return recordFinish({
        ok: false,
        command: "finish",
        branch: commitTarget.branch,
        worktree: root,
        notes: [`refusing automated commit while pre-task dirt remains: ${preserved.slice(0, 8).join(", ")}${preserved.length > 8 ? ", ..." : ""}; use a clean task worktree or commit reviewed paths manually`],
        code: 1,
      });
    }
  }
  const base = args.base || defaultBase(root);
  steps.push(`node hooks/verify.js --mode full --base ${base}`);
  const verify = runInherited(process.execPath, [path.join(root, "hooks", "verify.js"), "--mode", "full", "--base", base], root);
  if (!verify.ok) return recordFinish({ ok: false, command: "finish", branch: commitTarget.branch, worktree: root, notes: ["full VERIFY failed"], code: verify.code }, { base });
  steps.push(`node hooks/design-gate.js --base ${base}`);
  const design = runInherited(process.execPath, [path.join(root, "hooks", "design-gate.js"), "--base", base], root);
  if (!design.ok) return recordFinish({ ok: false, command: "finish", branch: commitTarget.branch, worktree: root, notes: ["DESIGN gate failed"], code: design.code }, { base });
  steps.push("git diff --check");
  const diff = runInherited("git", ["diff", "--check"], root);
  if (!diff.ok) return recordFinish({ ok: false, command: "finish", branch: commitTarget.branch, worktree: root, notes: ["git diff --check failed"], code: diff.code }, { base });
  steps.push("git diff --cached --check");
  const stagedDiff = runInherited("git", ["diff", "--cached", "--check"], root);
  if (!stagedDiff.ok) return recordFinish({ ok: false, command: "finish", branch: commitTarget.branch, worktree: root, notes: ["git diff --cached --check failed"], code: stagedDiff.code }, { base });
  const notes = [];
  if (args.commit) {
    steps.push("git add -A");
    git(root, ["add", "-A"]);
    steps.push("git diff --cached --check");
    const stagedAfterAdd = runInherited("git", ["diff", "--cached", "--check"], root);
    if (!stagedAfterAdd.ok) return recordFinish({ ok: false, command: "finish", branch: commitTarget.branch, worktree: root, notes: ["staged diff check failed after git add"], code: stagedAfterAdd.code }, { base });
    const lefthook = process.platform === "win32" ? "lefthook.cmd" : "lefthook";
    steps.push("lefthook run pre-commit --force");
    const preCommit = runInherited(lefthook, ["run", "pre-commit", "--force"], root);
    if (!preCommit.ok) return recordFinish({ ok: false, command: "finish", branch: commitTarget.branch, worktree: root, notes: ["pre-commit checks failed"], code: preCommit.code }, { base });
    steps.push(`git commit -m ${JSON.stringify(args.commit)}`);
    const committed = runInherited("git", ["commit", "-m", args.commit], root);
    if (!committed.ok) return recordFinish({ ok: false, command: "finish", branch: commitTarget.branch, worktree: root, notes: ["commit failed"], code: committed.code }, { base });
    notes.push("changes committed; push remains explicit");
  }
  const status = git(root, ["status", "--porcelain"]);
  if (!status) taskState.clearBaseline(root);
  else notes.push("uncommitted changes remain; rerun with --commit after reviewing the diff");
  return recordFinish({ ok: true, command: "finish", branch: git(root, ["branch", "--show-current"]), base, worktree: root, notes, next: status ? `node hooks/task.js finish --commit "<type(scope): subject>"` : "push or open a PR only when authorized" }, { base });
}

function main(argv = process.argv.slice(2), cwd = process.cwd()) {
  const args = parseArgs(argv);
  if (args.errors.length) { emit({ ok: false, command: args.command || "task", notes: args.errors }, args.json); return 2; }
  let root;
  try { root = repoRoot(cwd); }
  catch (error) { emit({ ok: false, command: args.command, notes: [String(error.stderr || error.message || error).trim()] }, args.json); return 1; }
  let result;
  try {
    result = args.command === "start" ? start(root, args) :
      args.command === "check" ? check(root, args) :
      args.command === "finish" ? finish(root, args) :
      args.command === "report" ? report(root, args) :
      worktreeStatus(root, args);
  } catch (error) {
    result = { ok: false, command: args.command, notes: [String(error.stderr || error.message || error).trim()] };
  }
  emit(result, args.json);
  return result.ok ? 0 : (result.code || 1);
}

if (require.main === module) process.exit(main());

module.exports = { parseArgs, defaultBase, startPlan, commitBranch, preTaskDirtyPaths, runInherited, worktreeStatus, report, main };
