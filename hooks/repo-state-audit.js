#!/usr/bin/env node
// repo-state-audit.js - verify that development and accepted-main checkouts
// converge to one clean base commit with no leftover branches or worktrees.
//
// Usage:
//   node hooks/repo-state-audit.js [--root <dir>] [--accepted-root <dir>]
//     [--base main] [--remote origin] [--fetch] [--strict] [--json]

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");
const { classifyRef, listOrphanedRemoteRefs, listRemoteBranches } = require("./branch-state");

const LOCAL_GIT_TIMEOUT_MS = 10000;
const REMOTE_GIT_TIMEOUT_MS = 60000;

function parseArgs(argv) {
  const out = {
    root: process.cwd(), acceptedRoot: "", base: "main", remote: "",
    fetch: false, strict: false, json: false, errors: [],
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--root" || arg === "--accepted-root" || arg === "--base" || arg === "--remote") {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) out.errors.push(`${arg} requires a value`);
      else if (arg === "--root") out.root = argv[++i];
      else if (arg === "--accepted-root") out.acceptedRoot = argv[++i];
      else if (arg === "--base") out.base = argv[++i];
      else out.remote = argv[++i];
    } else if (arg === "--fetch") {
      out.fetch = true;
    } else if (arg === "--strict") out.strict = true;
    else if (arg === "--json") out.json = true;
    else out.errors.push(`unknown option: ${arg}`);
  }
  out.root = path.resolve(out.root);
  if (out.acceptedRoot) out.acceptedRoot = path.resolve(out.acceptedRoot);
  return out;
}

function commandTimeoutMs(options = {}) {
  return options.remote ? REMOTE_GIT_TIMEOUT_MS : LOCAL_GIT_TIMEOUT_MS;
}

function run(root, args, options = {}) {
  return execFileSync("git", args, {
    cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    timeout: commandTimeoutMs(options), killSignal: "SIGKILL",
  }).trim();
}

function result(root, args, options) {
  try { return { ok: true, out: run(root, args, options) }; }
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

function baseBranch(base) {
  return String(base).replace(/^refs\/heads\//, "").replace(/^refs\/remotes\/[^/]+\//, "").replace(/^[^/]+\//, "");
}

function baseRefName(base) {
  if (String(base).startsWith("refs/")) return String(base);
  if (String(base).includes("/")) return `refs/remotes/${base}`;
  return `refs/heads/${base}`;
}

function inspectRoot(root, role, args, report) {
  if (!fs.existsSync(root)) {
    issue(report, "missing_root", `${role} root does not exist: ${root}`, { role, root });
    return null;
  }
  if (!result(root, ["rev-parse", "--is-inside-work-tree"]).ok) {
    issue(report, "not_git", `${role} root is not a git worktree: ${root}`, { role, root });
    return null;
  }
  const common = result(root, ["rev-parse", "--path-format=absolute", "--git-common-dir"]);
  const base = args.base;
  const branchName = baseBranch(base);
  const baseRef = baseRefName(base);
  if (args.fetch && args.remote) {
    const refspec = `+refs/heads/*:refs/remotes/${args.remote}/*`;
    const fetched = result(root, ["fetch", "--prune", args.remote, refspec], { remote: true });
    if (!fetched.ok) issue(report, "fetch_failed", `${role} cannot fetch ${args.remote}: ${fetched.error}`, { role, root });
  }
  const baseResult = result(root, ["rev-parse", "--verify", baseRef]);
  if (!baseResult.ok) {
    issue(report, "missing_base", `${role} base ref does not exist: ${base}`, { role, root, base });
  }
  const headResult = result(root, ["rev-parse", "HEAD"]);
  const branchResult = result(root, ["symbolic-ref", "--quiet", "--short", "HEAD"]);
  const remoteResult = args.remote
    ? result(root, ["rev-parse", "--verify", `refs/remotes/${args.remote}/${branchName}`])
    : { ok: false, out: "" };
  const info = {
    role, root, commonDir: common.ok ? normalized(common.out) : normalized(path.join(root, ".git")),
    branch: branchResult.ok ? branchResult.out : "", headSha: headResult.ok ? headResult.out : "",
    baseSha: baseResult.ok ? baseResult.out : "", remoteSha: remoteResult.ok ? remoteResult.out : "",
  };
  report.roots.push(info);
  if (args.strict && info.branch !== branchName) {
    issue(report, "checkout_branch", `${role} checkout is ${info.branch || "detached"}; expected ${branchName}`, { role, root, expected: branchName, actual: info.branch || "detached" });
  }
  if (args.strict && info.headSha && info.baseSha && info.headSha !== info.baseSha) {
    issue(report, "checkout_not_at_base", `${role} HEAD does not equal ${base}`, { role, root, headSha: info.headSha, baseSha: info.baseSha });
  }
  if (args.remote && !remoteResult.ok) {
    issue(report, "missing_remote_base", `${role} remote base ref does not exist: ${args.remote}/${branchName}`, { role, root });
  } else if (args.remote && info.baseSha && info.remoteSha && info.baseSha !== info.remoteSha) {
    issue(report, "remote_base_mismatch", `${role} ${base} is stale or diverged from ${args.remote}/${branchName}`, { role, root, baseSha: info.baseSha, remoteSha: info.remoteSha });
  }
  return info;
}

function audit(argv = process.argv.slice(2)) {
  const args = parseArgs(argv);
  const report = {
    ok: true, strict: args.strict, base: args.base, remote: args.remote, fetch: args.fetch, roots: [], issues: [],
    mergedBranches: [], unmergedBranches: [], remoteBranches: [],
    mergedRemoteBranches: [], equivalentRemoteBranches: [], unmergedRemoteBranches: [],
    orphanedRemoteRefs: [],
    dirtyWorktrees: [], extraWorktrees: [],
  };
  for (const error of args.errors) issue(report, "invalid_argument", error);
  if (args.errors.length) return report;

  const inputs = [{ root: args.root, role: "development" }];
  if (args.acceptedRoot) inputs.push({ root: args.acceptedRoot, role: "accepted" });
  const inspected = inputs.map((item) => inspectRoot(item.root, item.role, args, report)).filter(Boolean);
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

    const orphaned = listOrphanedRemoteRefs(probe);
    if (!orphaned.ok) {
      issue(report, "orphaned_remote_ref_list_failed", `cannot inspect remote-tracking refs: ${orphaned.error}`, { root: probe });
    } else {
      for (const entry of orphaned.refs) {
        const detailed = { root: probe, ...entry };
        report.orphanedRemoteRefs.push(detailed);
        issue(report, "orphaned_remote_ref", `remote-tracking ref has no configured remote: ${entry.relative}`, detailed);
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

    if (args.remote) {
      const branchName = baseBranch(args.base);
      const remoteBase = `refs/remotes/${args.remote}/${branchName}`;
      const listed = listRemoteBranches(probe, args.remote, branchName);
      if (!listed.ok) {
        issue(report, "remote_branch_list_failed", `cannot list ${args.remote} branches: ${listed.error}`, { root: probe });
      } else {
        for (const branch of listed.branches) {
          const state = classifyRef(probe, branch.ref, remoteBase);
          const entry = { root: probe, remote: args.remote, branch: branch.name, oid: branch.oid, state: state.state };
          report.remoteBranches.push(entry);
          if (state.state === "merged") {
            report.mergedRemoteBranches.push(entry);
            issue(report, "merged_remote_branch", `merged remote branch remains: ${args.remote}/${branch.name}`, entry);
          } else if (state.state === "equivalent") {
            report.equivalentRemoteBranches.push(entry);
            issue(report, "equivalent_remote_branch", `patch-equivalent remote branch remains: ${args.remote}/${branch.name}`, entry);
          } else {
            const detailed = { ...entry, reason: state.reason };
            report.unmergedRemoteBranches.push(detailed);
            issue(report, "unmerged_remote_branch", `remote branch contains work outside ${args.remote}/${branchName}: ${args.remote}/${branch.name}`, detailed);
          }
        }
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
    for (const root of report.roots) console.log(`  ${root.role}: ${root.branch || "detached"} HEAD=${root.headSha || "missing"} base=${root.baseSha || "missing"}${root.remoteSha ? ` remote=${root.remoteSha}` : ""} (${root.root})`);
    for (const item of report.issues) console.log(`  ${item.code}: ${item.message}`);
    console.log(report.ok ? "\nRepository topology is converged." : "\nRepository topology is not converged.");
  }
  process.exit(report.strict && !report.ok ? 1 : 0);
}

if (require.main === module) main();

module.exports = { audit, parseArgs, parseWorktrees, commandTimeoutMs };
