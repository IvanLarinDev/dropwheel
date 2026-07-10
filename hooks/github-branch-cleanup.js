#!/usr/bin/env node
// GitHub adapter for post-merge cleanup. It turns provider evidence from a
// successful main workflow into an exact-leased development-branch cleanup.

const path = require("path");
const { execFileSync } = require("child_process");
const { isPostMergeBranch, isRetainedBranch, loadBranchPolicy } = require("./release-cleanup");

function parseArgs(argv) {
  const out = {
    root: process.cwd(), mergeSha: process.env.MERGE_SHA || "",
    repository: process.env.GITHUB_REPOSITORY || "", remote: "origin",
    base: "main", requiredCheck: "verify", apply: false, json: false, errors: [],
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (["--root", "--merge-sha", "--repository", "--remote", "--base", "--required-check"].includes(arg)) {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) out.errors.push(`${arg} requires a value`);
      else {
        const value = argv[++i];
        if (arg === "--root") out.root = value;
        else if (arg === "--merge-sha") out.mergeSha = value;
        else if (arg === "--repository") out.repository = value;
        else if (arg === "--remote") out.remote = value;
        else if (arg === "--base") out.base = value;
        else out.requiredCheck = value;
      }
    } else if (arg === "--apply") out.apply = true;
    else if (arg === "--json") out.json = true;
    else out.errors.push(`unknown option: ${arg}`);
  }
  out.root = path.resolve(out.root);
  if (!/^[0-9a-f]{40}$/i.test(out.mergeSha)) out.errors.push("--merge-sha must be a full 40-character commit SHA");
  if (out.repository && !/^[^/]+\/[^/]+$/.test(out.repository)) out.errors.push("--repository must be owner/name");
  return out;
}

function run(root, file, args, options = {}) {
  return execFileSync(file, args, {
    cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"],
    timeout: options.remote ? 60000 : 15000, killSignal: "SIGKILL",
    env: process.env,
  }).trim();
}

function result(root, file, args, options) {
  try { return { ok: true, out: run(root, file, args, options) }; }
  catch (e) { return { ok: false, error: String((e && e.stderr) || (e && e.message) || "").trim(), stdout: String((e && e.stdout) || "").trim() }; }
}

function selectMergedPr(prs, mergeSha, base) {
  return (Array.isArray(prs) ? prs : []).filter((pr) =>
    pr && pr.merged_at && pr.merge_commit_sha === mergeSha &&
    pr.base && pr.base.ref === base
  );
}

function latestRequiredCheck(checks, name) {
  const matching = (Array.isArray(checks) ? checks : []).filter((check) =>
    check && check.name === name && check.app && check.app.slug === "github-actions"
  );
  matching.sort((a, b) => Number(b.id || 0) - Number(a.id || 0));
  return matching[0] || null;
}

function mismatchedBranchHeads(entries, expectedSha) {
  return (Array.isArray(entries) ? entries : []).filter((entry) =>
    entry && entry.exists && entry.oid !== expectedSha
  );
}

function apiJson(root, endpoint) {
  const response = result(root, "gh", ["api", endpoint, "-H", "Accept: application/vnd.github+json"], { remote: true });
  if (!response.ok) return { ok: false, value: null, error: response.error };
  try { return { ok: true, value: JSON.parse(response.out), error: "" }; }
  catch (e) { return { ok: false, value: null, error: `invalid GitHub API JSON: ${e.message}` }; }
}

function main(argv = process.argv.slice(2)) {
  const args = parseArgs(argv);
  const report = {
    ok: false, apply: args.apply, mergeSha: args.mergeSha, repository: args.repository,
    base: args.base, remote: args.remote, requiredCheck: args.requiredCheck,
    pr: null, branch: "", skipped: false, skipReason: "", cleanup: null, errors: [...args.errors],
  };
  if (report.errors.length) return finish(report, args.json);

  if (!args.repository) {
    const repo = result(args.root, "gh", ["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"], { remote: true });
    if (!repo.ok || !repo.out) report.errors.push(`cannot resolve GitHub repository: ${repo.error}`);
    else report.repository = args.repository = repo.out;
  }
  if (report.errors.length) return finish(report, args.json);

  const fetched = result(args.root, "git", [
    "fetch", args.remote, "--prune", `+refs/heads/*:refs/remotes/${args.remote}/*`,
  ], { remote: true });
  if (!fetched.ok) report.errors.push(`cannot fetch branch refs: ${fetched.error}`);
  const baseRef = `refs/remotes/${args.remote}/${args.base}`;
  if (!result(args.root, "git", ["merge-base", "--is-ancestor", args.mergeSha, baseRef]).ok) {
    report.errors.push(`${args.mergeSha} is not included in ${baseRef}`);
  }
  if (report.errors.length) return finish(report, args.json);

  const prsResponse = apiJson(args.root, `repos/${args.repository}/commits/${args.mergeSha}/pulls`);
  if (!prsResponse.ok) report.errors.push(`cannot read associated pull requests: ${prsResponse.error}`);
  const prs = prsResponse.ok ? selectMergedPr(prsResponse.value, args.mergeSha, args.base) : [];
  if (prs.length !== 1) report.errors.push(`expected exactly one MERGED PR for ${args.mergeSha} into ${args.base}; found ${prs.length}`);
  if (report.errors.length) return finish(report, args.json);

  const pr = prs[0];
  const branchPolicy = loadBranchPolicy(args.root, args.base);
  report.pr = { number: pr.number, url: pr.html_url || "", headSha: pr.head && pr.head.sha || "" };
  report.branch = pr.head && pr.head.ref || "";
  const headRepo = pr.head && pr.head.repo && pr.head.repo.full_name || "";
  if (headRepo !== args.repository) {
    report.ok = true;
    report.skipped = true;
    report.skipReason = `fork branch belongs to ${headRepo || "an unavailable repository"}`;
    return finish(report, args.json);
  }
  if (!/^[0-9a-f]{40}$/i.test(report.pr.headSha)) report.errors.push("merged PR is missing a full reviewed head SHA");
  if (!isPostMergeBranch(report.branch, branchPolicy)) {
    if (isRetainedBranch(report.branch, branchPolicy)) {
      report.ok = true;
      report.skipped = true;
      report.skipReason = "branch matches a retained prefix and is kept until artifact smoke testing";
      return finish(report, args.json);
    }
    report.errors.push(`PR head branch is outside the managed development prefixes: ${report.branch}`);
  }

  const checksResponse = apiJson(args.root, `repos/${args.repository}/commits/${args.mergeSha}/check-runs?per_page=100`);
  if (!checksResponse.ok) report.errors.push(`cannot read merge checks: ${checksResponse.error}`);
  const check = checksResponse.ok ? latestRequiredCheck(checksResponse.value && checksResponse.value.check_runs, args.requiredCheck) : null;
  if (!check || check.status !== "completed" || check.conclusion !== "success") {
    report.errors.push(`required GitHub Actions check is not successful on merge commit: ${args.requiredCheck}`);
  }

  const branchHeads = [
    { label: "local", ref: `refs/heads/${report.branch}` },
    { label: "remote", ref: `refs/remotes/${args.remote}/${report.branch}` },
  ].map((entry) => {
    const resolved = result(args.root, "git", ["rev-parse", "--verify", entry.ref]);
    return { ...entry, exists: resolved.ok, oid: resolved.ok ? resolved.out : "" };
  });
  for (const moved of mismatchedBranchHeads(branchHeads, report.pr.headSha)) {
    report.errors.push(`${moved.label} branch moved after PR merge: expected ${report.pr.headSha}, found ${moved.oid}`);
  }
  if (report.errors.length) return finish(report, args.json);

  const cleanupArgs = [
    path.join(__dirname, "post-merge-cleanup.js"),
    "--root", args.root,
    "--base", baseRef,
    "--remote", args.remote,
    "--branch", report.branch,
    "--no-fetch",
    "--include-equivalent",
    "--json",
  ];
  if (args.apply) cleanupArgs.push("--apply");
  const cleaned = result(args.root, process.execPath, cleanupArgs, { remote: true });
  try { report.cleanup = JSON.parse(cleaned.ok ? cleaned.out : cleaned.stdout || "{}"); }
  catch { report.cleanup = null; }
  if (!cleaned.ok || !report.cleanup || report.cleanup.ok !== true) {
    report.errors.push(`post-merge cleanup failed: ${cleaned.error || "invalid cleanup report"}`);
  }
  report.ok = report.errors.length === 0;
  return finish(report, args.json);
}

function finish(report, json) {
  if (json) console.log(JSON.stringify(report));
  else {
    console.log(`GitHub branch lifecycle: ${report.mergeSha}`);
    if (report.pr) console.log(`  PR: #${report.pr.number} ${report.pr.url}`);
    if (report.branch) console.log(`  branch: ${report.branch}`);
    if (report.skipped) console.log(`  skipped: ${report.skipReason}`);
    if (report.cleanup) console.log(`  cleanup: ${report.cleanup.ok ? "passed" : "failed"}`);
    for (const error of report.errors) console.log(`  error: ${error}`);
    console.log(report.ok ? "\nBranch lifecycle passed." : "\nBranch lifecycle failed.");
  }
  process.exitCode = report.ok ? 0 : 1;
  return report;
}

if (require.main === module) main();

module.exports = { latestRequiredCheck, main, mismatchedBranchHeads, parseArgs, selectMergedPr };
