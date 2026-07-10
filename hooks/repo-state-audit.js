#!/usr/bin/env node
// repo-state-audit.js - verify that development and accepted-main checkouts
// converge to one clean base commit with no leftover branches or worktrees.
//
// Usage:
//   node hooks/repo-state-audit.js [--root <dir>] [--accepted-root <dir>]
//     [--base main] [--strict] [--json]

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

function parseArgs(argv) {
  const out = {
    root: process.cwd(), acceptedRoot: "", base: "main",
    strict: false, json: false, errors: [],
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--root" || arg === "--accepted-root" || arg === "--base") {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) out.errors.push(`${arg} requires a value`);
      else if (arg === "--root") out.root = argv[++i];
      else if (arg === "--accepted-root") out.acceptedRoot = argv[++i];
      else out.base = argv[++i];
    } else if (arg === "--strict") out.strict = true;
    else if (arg === "--json") out.json = true;
    else out.errors.push(`unknown option: ${arg}`);
  }
  out.root = path.resolve(out.root);
  if (out.acceptedRoot) out.acceptedRoot = path.resolve(out.acceptedRoot);
  return out;
}

function run(root, args) {
  return execFileSync("git", args, {
    cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    timeout: 10000, killSignal: "SIGKILL",
  }).trim();
}

function result(root, args) {
  try { return { ok: true, out: run(root, args) }; }
  catch (e) { return { ok: false, error: String(e.stderr || e.message || "").trim() }; }
}

function normalized(value) {
  const resolved = path.resolve(value);
  let canonical = resolved;
  try { canonical = fs.realpathSync.native(resolved); } catch {}
  return canonical.replace(/\\/g, "/").toLowerCase();
}

function parseWorktrees(text) {
  const out = [];
  let item = null;
  for (const raw of String(text || "").split(/\r?\n/)) {
    if (raw.startsWith("worktree ")) {
      if (item) out.push(item);
      item = { path: raw.slice("worktree ".length), branch: "", detached: false };
    } else if (item && raw.startsWith("branch refs/heads/")) {
      item.branch = raw.slice("branch refs/heads/".length);
    } else if (item && raw === "detached") {
      item.detached = true;
    }
  }
  if (item) out.push(item);
  return out;
}

function issue(report, code, message, details = {}) {
  report.issues.push({ code, message, ...details });
}

function inspectRoot(root, role, base, report) {
  if (!fs.existsSync(root)) {
    issue(report, "missing_root", `${role} root does not exist: ${root}`, { role, root });
    return null;
  }
  if (!result(root, ["rev-parse", "--is-inside-work-tree"]).ok) {
    issue(report, "not_git", `${role} root is not a git worktree: ${root}`, { role, root });
    return null;
  }
  const common = result(root, ["rev-parse", "--path-format=absolute", "--git-common-dir"]);
  const baseRef = base.startsWith("refs/") ? base : `refs/heads/${base}`;
  const baseResult = result(root, ["rev-parse", "--verify", baseRef]);
  if (!baseResult.ok) {
    issue(report, "missing_base", `${role} base ref does not exist: ${base}`, { role, root, base });
  }
  const info = {
    role, root, commonDir: common.ok ? normalized(common.out) : normalized(path.join(root, ".git")),
    baseSha: baseResult.ok ? baseResult.out : "",
  };
  report.roots.push(info);
  return info;
}

function audit(argv = process.argv.slice(2)) {
  const args = parseArgs(argv);
  const report = {
    ok: true, strict: args.strict, base: args.base, roots: [], issues: [],
    mergedBranches: [], unmergedBranches: [], dirtyWorktrees: [], extraWorktrees: [],
  };
  for (const error of args.errors) issue(report, "invalid_argument", error);
  if (args.errors.length) return report;

  const inputs = [{ root: args.root, role: "development" }];
  if (args.acceptedRoot) inputs.push({ root: args.acceptedRoot, role: "accepted" });
  const inspected = inputs.map((item) => inspectRoot(item.root, item.role, args.base, report)).filter(Boolean);
  const groups = new Map();
  for (const item of inspected) {
    if (!groups.has(item.commonDir)) groups.set(item.commonDir, []);
    groups.get(item.commonDir).push(item);
  }

  for (const members of groups.values()) {
    const probe = members[0].root;
    const allowed = new Set(members.map((item) => normalized(item.root)));
    const worktreesResult = result(probe, ["worktree", "list", "--porcelain"]);
    if (!worktreesResult.ok) {
      issue(report, "worktree_list_failed", `cannot list worktrees: ${worktreesResult.error}`, { root: probe });
      continue;
    }
    for (const worktree of parseWorktrees(worktreesResult.out)) {
      const worktreePath = path.resolve(worktree.path);
      if (!allowed.has(normalized(worktreePath))) {
        const entry = { root: probe, path: worktreePath, branch: worktree.branch, detached: worktree.detached };
        report.extraWorktrees.push(entry);
        issue(report, "extra_worktree", `unexpected worktree remains: ${worktreePath}`, entry);
      }
      const status = result(worktreePath, ["status", "--porcelain"]);
      if (!status.ok || status.out) {
        const entry = { root: probe, path: worktreePath, status: status.ok ? status.out.split(/\r?\n/) : [], error: status.error || "" };
        report.dirtyWorktrees.push(entry);
        issue(report, "dirty_worktree", `worktree is not clean: ${worktreePath}`, entry);
      }
    }

    const localRefs = result(probe, ["for-each-ref", "--format=%(refname:short)%09%(objectname)", "refs/heads"]);
    if (!localRefs.ok) {
      issue(report, "branch_list_failed", `cannot list local branches: ${localRefs.error}`, { root: probe });
      continue;
    }
    const baseRef = args.base.startsWith("refs/") ? args.base : `refs/heads/${args.base}`;
    for (const line of String(localRefs.out || "").split(/\r?\n/).filter(Boolean)) {
      const [branch, oid] = line.split("\t");
      if (`refs/heads/${branch}` === baseRef) continue;
      const ancestor = result(probe, ["merge-base", "--is-ancestor", oid, baseRef]).ok;
      const entry = { root: probe, branch, oid };
      if (ancestor) {
        report.mergedBranches.push(entry);
        issue(report, "merged_branch", `merged local branch remains: ${branch}`, entry);
      } else {
        report.unmergedBranches.push(entry);
        issue(report, "unmerged_branch", `local branch contains commits outside ${args.base}: ${branch}`, entry);
      }
    }
  }

  if (args.acceptedRoot && inspected.length === 2 && inspected[0].baseSha && inspected[1].baseSha &&
      inspected[0].baseSha !== inspected[1].baseSha) {
    issue(report, "base_mismatch", `${args.base} differs between development and accepted roots`, {
      developmentSha: inspected[0].baseSha, acceptedSha: inspected[1].baseSha,
    });
  }
  report.ok = report.issues.length === 0;
  return report;
}

function main(argv = process.argv.slice(2)) {
  const report = audit(argv);
  const json = argv.includes("--json");
  if (json) console.log(JSON.stringify(report));
  else {
    console.log(`repo state audit: ${report.base}`);
    for (const root of report.roots) console.log(`  ${root.role}: ${root.baseSha || "missing"} (${root.root})`);
    for (const item of report.issues) console.log(`  ${item.code}: ${item.message}`);
    console.log(report.ok ? "\nRepository topology is converged." : "\nRepository topology is not converged.");
  }
  process.exit(report.strict && !report.ok ? 1 : 0);
}

if (require.main === module) main();

module.exports = { audit, parseArgs, parseWorktrees };
