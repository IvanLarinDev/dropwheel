#!/usr/bin/env node
// release-cleanup.js - remove merged development/release branches after a
// published release has passed artifact smoke testing. The shared --branch
// mode powers post-merge cleanup for one development branch.
//
// Dry-run is the default. --apply authorizes deletion, but only for branches
// with a known development prefix whose commits are ancestors of --base.
// Dirty worktrees and diverged refs block cleanup. Unmerged branches block
// exact mode and are reported-but-skipped in release-wide mode. Tags are never
// touched. Local and remote deletion use the OIDs that passed ancestry checks.
//
// Usage:
//   node hooks/release-cleanup.js [--root <dir>] [--base origin/main]
//     [--remote origin] [--branch feat/name] [--no-fetch] [--apply] [--json]

const path = require("path");
const { execFileSync } = require("child_process");

const BRANCH_PREFIXES = [
  "codex/", "feat/", "feature/", "fix/", "bugfix/", "docs/", "chore/",
  "refactor/", "perf/", "build/", "style/", "release/", "hotfix/",
  "test/", "ci/", "improvement/",
];
const PROTECTED_BRANCHES = new Set(["main", "master"]);
const RELEASE_PREFIXES = ["release/", "hotfix/"];

function parseArgs(argv) {
  const a = {
    root: process.cwd(), base: "origin/main", remote: "origin",
    branch: "", fetch: true, apply: false, json: false, argErrors: [],
  };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--base") a.base = argv[++i];
    else if (argv[i] === "--remote") a.remote = argv[++i];
    else if (argv[i] === "--branch") {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) a.argErrors.push("--branch requires a branch name");
      else a.branch = argv[++i];
    }
    else if (argv[i] === "--no-fetch") a.fetch = false;
    else if (argv[i] === "--apply") a.apply = true;
    else if (argv[i] === "--json") a.json = true;
  }
  a.root = path.resolve(a.root);
  return a;
}

function run(root, args, opts = {}) {
  return execFileSync("git", args, {
    cwd: root,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    timeout: opts.remote ? 60000 : 10000,
    killSignal: "SIGKILL",
  }).trim();
}

function result(root, args, opts) {
  try { return { ok: true, out: run(root, args, opts) }; }
  catch (e) { return { ok: false, error: String((e && e.stderr) || (e && e.message) || "").trim() }; }
}

function lines(text) {
  return String(text || "").split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
}

function isManagedBranch(name) {
  return !PROTECTED_BRANCHES.has(name) && BRANCH_PREFIXES.some((prefix) => name.startsWith(prefix));
}

function isPostMergeBranch(name) {
  return isManagedBranch(name) && !RELEASE_PREFIXES.some((prefix) => name.startsWith(prefix));
}

function isAncestor(root, ref, base) {
  return result(root, ["merge-base", "--is-ancestor", ref, base]).ok;
}

function parseWorktrees(text) {
  const out = [];
  let item = null;
  for (const raw of String(text || "").split(/\r?\n/)) {
    if (raw.startsWith("worktree ")) {
      if (item) out.push(item);
      item = { path: raw.slice("worktree ".length), branch: "" };
    } else if (item && raw.startsWith("branch refs/heads/")) {
      item.branch = raw.slice("branch refs/heads/".length);
    }
  }
  if (item) out.push(item);
  return out;
}

function samePath(a, b) {
  const normalize = (value) => path.resolve(value).replace(/\\/g, "/").toLowerCase();
  return normalize(a) === normalize(b);
}

function main(argv = process.argv.slice(2), options = {}) {
  const a = parseArgs(argv);
  const label = options.label || (a.branch ? "post-merge cleanup" : "release cleanup");
  const report = {
    ok: true,
    apply: a.apply,
    mode: a.branch ? "branch" : "release",
    branch: a.branch,
    base: a.base,
    remote: a.remote,
    candidates: [],
    removedWorktrees: [],
    deletedLocal: [],
    deletedRemote: [],
    blocked: [],
    skipped: [],
    absent: [],
    errors: [],
  };

  report.errors.push(...a.argErrors);
  if (options.requireBranch && !a.branch) report.errors.push("--branch is required for post-merge cleanup");
  if (a.branch && !isPostMergeBranch(a.branch)) {
    report.errors.push(`branch is not eligible for post-merge cleanup: ${a.branch}`);
  }

  if (report.errors.length === 0 && !result(a.root, ["rev-parse", "--is-inside-work-tree"]).ok) {
    report.errors.push("not a git repository");
  }
  if (report.errors.length === 0 && !result(a.root, ["rev-parse", "--verify", "--quiet", a.base]).ok) {
    report.errors.push(`base ref not found: ${a.base}`);
  }
  if (report.errors.length === 0 && !result(a.root, ["remote", "get-url", a.remote]).ok) {
    report.errors.push(`remote not found: ${a.remote}`);
  }
  if (report.errors.length === 0 && a.fetch) {
    const fetched = result(a.root, ["fetch", a.remote, "--prune"], { remote: true });
    if (!fetched.ok) report.errors.push(`fetch failed: ${fetched.error}`);
  }

  if (report.errors.length === 0) {
    const localRefs = lines(run(a.root, ["for-each-ref", "--format=%(refname:short)%09%(objectname)", "refs/heads"]))
      .map((line) => {
        const [name, oid] = line.split("\t");
        return { name, oid };
      });
    const remotePrefix = `${a.remote}/`;
    const remoteRefs = lines(run(a.root, ["for-each-ref", "--format=%(refname:short)%09%(objectname)", `refs/remotes/${a.remote}`]))
      .map((line) => {
        const [ref, oid] = line.split("\t");
        return { ref, oid, name: ref.startsWith(remotePrefix) ? ref.slice(remotePrefix.length) : "" };
      })
      .filter((item) => item.name && item.ref !== `${a.remote}/HEAD`);
    const worktrees = parseWorktrees(run(a.root, ["worktree", "list", "--porcelain"]));
    const worktreeByBranch = new Map(worktrees.filter((item) => item.branch).map((item) => [item.branch, item.path]));
    const localMap = new Map(localRefs.map((item) => [item.name, item]));
    const remoteMap = new Map(remoteRefs.map((item) => [item.name, item]));
    const names = new Set([...localRefs.map((item) => item.name), ...remoteRefs.map((item) => item.name)]);
    const scopedNames = a.branch ? [a.branch] : [...names].filter(isManagedBranch).sort();

    for (const name of scopedNames) {
      const local = localMap.get(name);
      const localExists = !!local;
      const remote = remoteMap.get(name);
      const remoteRef = remote ? remote.ref : "";
      if (!localExists && !remoteRef) {
        report.absent.push(name);
        continue;
      }
      const localMerged = localExists && isAncestor(a.root, name, a.base);
      const remoteMerged = !!remoteRef && isAncestor(a.root, remoteRef, a.base);
      const entry = {
        branch: name,
        local: localExists,
        localOid: local ? local.oid : "",
        remote: !!remoteRef,
        remoteOid: remote ? remote.oid : "",
        worktree: worktreeByBranch.get(name) || "",
      };
      const reasons = [];
      if (localExists && !localMerged) reasons.push("local branch contains commits not merged into base");
      if (remoteRef && !remoteMerged) reasons.push("remote branch contains commits not merged into base");
      if (!localMerged && !remoteMerged) {
        const target = a.branch ? report.blocked : report.skipped;
        target.push({ ...entry, reasons });
        continue;
      }
      if (entry.worktree && samePath(entry.worktree, a.root)) reasons.push("branch is checked out in the cleanup worktree");
      if (entry.worktree && !samePath(entry.worktree, a.root)) {
        const status = result(entry.worktree, ["status", "--porcelain"]);
        if (!status.ok) reasons.push("cannot inspect linked worktree");
        else if (status.out) reasons.push("linked worktree is dirty");
      }

      if (reasons.length) {
        report.blocked.push({ ...entry, reasons });
        continue;
      }
      report.candidates.push(entry);
    }

    if (a.apply) {
      for (const entry of report.candidates) {
        let localSafe = true;
        if (entry.worktree) {
          const removed = result(a.root, ["worktree", "remove", entry.worktree]);
          if (!removed.ok) {
            localSafe = false;
            report.errors.push(`cannot remove worktree for ${entry.branch}: ${removed.error}`);
          } else {
            report.removedWorktrees.push(entry.worktree);
          }
        }
        if (entry.local && localSafe) {
          const deleted = result(a.root, ["update-ref", "-d", `refs/heads/${entry.branch}`, entry.localOid]);
          if (!deleted.ok) {
            localSafe = false;
            report.errors.push(`cannot delete local branch ${entry.branch} at ${entry.localOid}: ${deleted.error}`);
          } else {
            report.deletedLocal.push(entry.branch);
          }
        }
        if (entry.remote && localSafe) {
          const lease = `--force-with-lease=refs/heads/${entry.branch}:${entry.remoteOid}`;
          const deleted = result(a.root, ["push", lease, a.remote, "--delete", entry.branch], { remote: true });
          if (!deleted.ok) report.errors.push(`cannot delete remote branch ${entry.branch}: ${deleted.error}`);
          else report.deletedRemote.push(entry.branch);
        }
      }
    }
  }

  report.ok = report.errors.length === 0 && report.blocked.length === 0;
  if (a.json) {
    console.log(JSON.stringify(report));
  } else {
    console.log(`${label} (${a.apply ? "apply" : "dry-run"}): ${a.base}`);
    for (const entry of report.candidates) console.log(`  ${a.apply ? "deleted" : "candidate"}: ${entry.branch}`);
    for (const entry of report.blocked) console.log(`  blocked: ${entry.branch} (${entry.reasons.join("; ")})`);
    for (const entry of report.skipped) console.log(`  skipped: ${entry.branch} (${entry.reasons.join("; ")})`);
    for (const branch of report.absent) console.log(`  absent: ${branch}`);
    for (const error of report.errors) console.log(`  error: ${error}`);
    if (!a.apply && report.candidates.length) {
      const gate = a.branch ? "the PR is MERGED and main CI succeeds" : "release smoke testing succeeds";
      console.log(`\nRe-run with --apply after ${gate}.`);
    }
    console.log(report.ok ? `\n${label} passed.` : `\n${label} incomplete.`);
  }
  process.exit(report.ok ? 0 : 1);
}

if (require.main === module) main();

module.exports = { main, parseArgs, isManagedBranch, isPostMergeBranch };
