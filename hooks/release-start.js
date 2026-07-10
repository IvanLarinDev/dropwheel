#!/usr/bin/env node
// release-start.js - attach a clean detached release worktree before Cocogitto.
//
// A worktree created directly from origin/main has branch name HEAD. Cocogitto
// correctly rejects that name through branch_whitelist, even for --dry-run.
// This helper creates a temporary release/prepare-* branch, asks Cocogitto for
// the next version, and renames the branch to release/vX.Y.Z.
//
// Usage:
//   node hooks/release-start.js [--root <dir>] [--base origin/main] [--json]

// The command requires a clean detached HEAD exactly at --base. If version
// discovery or branch finalization fails, the temporary branch is removed and
// the worktree is returned to its original detached HEAD.

const path = require("path");
const { execFileSync } = require("child_process");
const { loadReleaseConfig } = require("./release-config.js");

function parseArgs(argv) {
  const a = { root: process.cwd(), base: "origin/main", json: false, errors: [] };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--root") {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) a.errors.push("--root requires a directory");
      else a.root = argv[++i];
    } else if (argv[i] === "--base") {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) a.errors.push("--base requires a git ref");
      else a.base = argv[++i];
    } else if (argv[i] === "--json") {
      a.json = true;
    } else {
      a.errors.push(`unknown option: ${argv[i]}`);
    }
  }
  a.root = path.resolve(a.root);
  return a;
}

function git(root, args, timeout = 10000) {
  return execFileSync("git", args, {
    cwd: root,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    timeout,
    killSignal: "SIGKILL",
  }).trim();
}

function gitOut(root, args) {
  try { return git(root, args); } catch { return ""; }
}

function gitOk(root, args) {
  try { git(root, args); return true; } catch { return false; }
}

function commandError(error) {
  const stderr = String(error && error.stderr || "").trim();
  const stdout = String(error && error.stdout || "").trim();
  return stderr || stdout || String(error && error.message || error || "command failed");
}

function defaultRunCog(root) {
  return execFileSync("cog", ["bump", "--auto", "--dry-run"], {
    cwd: root,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    timeout: 60000,
    killSignal: "SIGKILL",
  }).trim();
}

function parseVersion(output) {
  const clean = String(output || "").replace(/\x1b\[[0-9;]*m/g, "").trim();
  const lines = clean.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  for (let i = lines.length - 1; i >= 0; i--) {
    const match = lines[i].match(/^v?(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?)$/);
    if (match) return match[1];
  }
  return "";
}

function releaseStart(options, dependencies = {}) {
  const a = {
    root: path.resolve(options.root || process.cwd()),
    base: options.base || "origin/main",
  };
  const res = {
    ok: false,
    root: a.root,
    base: a.base,
    head: "",
    prepareBranch: "",
    branch: "",
    version: "",
    tag: "",
    rolledBack: false,
    results: [],
  };
  const add = (level, msg, extra = {}) => res.results.push({ level, msg, ...extra });
  const fail = (msg, extra) => add("FAIL", msg, extra);
  const pass = (msg, extra) => add("PASS", msg, extra);

  if (loadReleaseConfig(a.root).provider === "none") {
    fail("release capability is disabled in harness.config.json");
    return res;
  }

  if (!gitOk(a.root, ["rev-parse", "--is-inside-work-tree"])) {
    fail("not a git repository");
    return res;
  }

  const dirty = gitOut(a.root, ["status", "--porcelain"]);
  if (dirty) {
    fail("worktree is dirty", { details: dirty.split(/\r?\n/).filter(Boolean).slice(0, 20) });
    return res;
  }
  pass("worktree is clean");

  const head = gitOut(a.root, ["rev-parse", "HEAD"]);
  const baseHead = gitOut(a.root, ["rev-parse", "--verify", a.base]);
  res.head = head;
  if (!head || !baseHead) {
    fail(`base ref not found: ${a.base}`);
    return res;
  }
  if (head !== baseHead) {
    fail(`HEAD must exactly match ${a.base}`, { head, base: baseHead });
    return res;
  }
  pass(`HEAD exactly matches ${a.base}`, { head });

  const currentBranch = gitOut(a.root, ["symbolic-ref", "--quiet", "--short", "HEAD"]);
  if (currentBranch) {
    fail(`release start requires detached HEAD, found branch: ${currentBranch}`);
    return res;
  }
  pass("HEAD is detached");

  const prepareBranch = `release/prepare-${head.slice(0, 12)}`;
  res.prepareBranch = prepareBranch;
  if (gitOk(a.root, ["show-ref", "--verify", "--quiet", `refs/heads/${prepareBranch}`])) {
    fail(`temporary release branch already exists: ${prepareBranch}`);
    return res;
  }

  let createdPrepare = false;
  const rollback = () => {
    if (!createdPrepare) return;
    try {
      git(a.root, ["switch", "--detach", head]);
      git(a.root, ["update-ref", "-d", `refs/heads/${prepareBranch}`, head]);
      res.rolledBack = true;
      add("PASS", `removed temporary branch after failure: ${prepareBranch}`);
    } catch (error) {
      fail(`could not roll back temporary branch ${prepareBranch}`, { details: commandError(error) });
    }
  };

  try {
    git(a.root, ["switch", "-c", prepareBranch]);
    createdPrepare = true;
    pass(`created temporary branch: ${prepareBranch}`);

    const runCog = dependencies.runCog || defaultRunCog;
    let cogOutput = "";
    try {
      cogOutput = runCog(a.root);
    } catch (error) {
      fail("cog bump --auto --dry-run failed", { details: commandError(error) });
      rollback();
      return res;
    }

    const version = parseVersion(cogOutput);
    if (!version) {
      fail("could not parse SemVer from cog bump --auto --dry-run", { output: String(cogOutput || "").trim() });
      rollback();
      return res;
    }
    const tag = `v${version}`;
    const branch = `release/${tag}`;
    res.version = version;
    res.tag = tag;
    res.branch = branch;
    pass(`Cocogitto selected ${tag}`);

    if (gitOk(a.root, ["show-ref", "--verify", "--quiet", `refs/heads/${branch}`])) {
      fail(`local release branch already exists: ${branch}`);
      rollback();
      return res;
    }

    let remoteBranch = "";
    try {
      remoteBranch = git(a.root, ["ls-remote", "--heads", "origin", `refs/heads/${branch}`], 60000);
    } catch (error) {
      fail(`could not verify remote release branch: ${branch}`, { details: commandError(error) });
      rollback();
      return res;
    }
    if (remoteBranch) {
      fail(`remote release branch already exists: ${branch}`);
      rollback();
      return res;
    }

    try {
      git(a.root, ["branch", "-m", branch]);
    } catch (error) {
      fail(`could not rename temporary branch to ${branch}`, { details: commandError(error) });
      rollback();
      return res;
    }
    createdPrepare = false;
    pass(`release branch ready: ${branch}`);
    res.ok = true;
    return res;
  } catch (error) {
    fail("could not create temporary release branch", { details: commandError(error) });
    rollback();
    return res;
  }
}

function printResult(res, json) {
  if (json) {
    console.log(JSON.stringify(res));
    return;
  }
  const icon = { PASS: "+", FAIL: "X" };
  console.log(`release start: ${res.base}`);
  for (const result of res.results) console.log(`  ${icon[result.level] || "!"} ${result.msg}`);
  if (res.ok) {
    console.log(`\n${res.branch} is ready at ${res.head}.`);
    console.log(`Next: node hooks/release-manifest-bump.js --tag ${res.tag}`);
    console.log(`Then: cog bump --auto --annotated "${res.tag}"`);
  } else {
    console.log("\nrelease start failed.");
  }
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.errors.length) {
    const res = { ok: false, root: args.root, base: args.base, results: args.errors.map((msg) => ({ level: "FAIL", msg })) };
    printResult(res, args.json);
    process.exit(1);
  }
  const res = releaseStart(args);
  printResult(res, args.json);
  process.exit(res.ok ? 0 : 1);
}

module.exports = { parseArgs, parseVersion, run: releaseStart };
if (require.main === module) main();
