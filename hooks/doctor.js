#!/usr/bin/env node
// doctor.js - environment self-check (BACKLOG P2-12). Catches the classes of problem we
// hit in development: hooks not wired, CRLF, NUL bytes, bad config, missing git identity.
// Checks the migrated stack (lefthook + gitleaks + cocogitto). Run: node hooks/doctor.js
//
// [--root <dir>] [--json].  Exit 0 = no FAIL (WARN allowed), 1 = at least one FAIL.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

function arg(name, def) { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : def; }
const ROOT = arg("--root", process.cwd());
const results = [];
function ok(msg) { results.push({ level: "PASS", msg }); }
function warn(msg) { results.push({ level: "WARN", msg }); }
function fail(msg) { results.push({ level: "FAIL", msg }); }
function git(args) { return execFileSync("git", args, { cwd: ROOT, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"], timeout: 5000, killSignal: "SIGKILL" }).trim(); }
function gitSafe(args) { try { return git(args); } catch { return null; } }
function inPath(bin) { try { execFileSync(process.platform === "win32" ? "where" : "which", [bin], { stdio: ["ignore", "pipe", "ignore"], timeout: 5000, killSignal: "SIGKILL" }); return true; } catch { return false; } }
function tracked(rel) { return gitSafe(["ls-files", "--error-unmatch", rel]) !== null; }
function readText(rel) { try { return fs.readFileSync(path.join(ROOT, rel), "utf8"); } catch { return ""; } }
function escapeRe(s) { return String(s).replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); }
function tomlSection(text, name) {
  const re = new RegExp(`^\\s*\\[${escapeRe(name)}\\]\\s*$`, "m");
  const m = re.exec(text);
  if (!m) return "";
  const rest = text.slice(m.index + m[0].length);
  const next = rest.search(/^\s*\[[^\]]+\]\s*$/m);
  return next >= 0 ? rest.slice(0, next) : rest;
}
function tomlString(section, key) {
  const re = new RegExp(`^\\s*${escapeRe(key)}\\s*=\\s*"([^"]*)"\\s*$`, "m");
  const m = re.exec(section);
  return m ? m[1] : null;
}
function githubRepoFromUrl(url) {
  const value = String(url || "").trim();
  const m = value.match(/^(?:https:\/\/github\.com\/|git@github\.com:|ssh:\/\/git@github\.com\/)([^/:]+)\/([^/]+?)(?:\.git)?\/?$/i);
  return m ? { owner: m[1], repository: m[2] } : null;
}
function checkTextFile(rel) {
  const p = path.join(ROOT, rel);
  let buf;
  try { buf = fs.readFileSync(p); } catch { fail(rel + " is missing"); return; }
  const text = buf.toString("utf8");
  if (!Buffer.from(text, "utf8").equals(buf)) fail(rel + ": invalid UTF-8 or truncated multibyte character");
  else if (buf.includes(0)) fail(rel + " contains NUL bytes");
  else if (buf.includes(13)) fail(rel + ": CRLF/CR line endings (LF required)");
  else ok(rel + ": LF, UTF-8, no NUL");
}
function yamlKeyLine(line) {
  const m = String(line).match(/^(\s*)(?:"([^"]+)"|'([^']+)'|([A-Za-z0-9_-]+))\s*:.*$/);
  if (!m) return null;
  return { indent: m[1].length, key: m[2] || m[3] || m[4] };
}
function workflowJobs(rel) {
  const text = readText(rel);
  const lines = text.split(/\r?\n/);
  const jobs = [];
  let jobsIndent = null, jobIndent = null;
  let current = null;
  for (const line of lines) {
    if (/^\s*(?:#.*)?$/.test(line)) {
      if (current) current.body.push(line);
      continue;
    }
    const key = yamlKeyLine(line);
    if (jobsIndent === null) {
      if (key && key.key === "jobs") jobsIndent = key.indent;
      continue;
    }
    if (key && key.indent <= jobsIndent) break;
    if (key && (jobIndent === null || key.indent === jobIndent)) {
      if (jobIndent === null) jobIndent = key.indent;
      current = { id: key.key, body: [line] };
      jobs.push(current);
      continue;
    }
    if (current) current.body.push(line);
  }
  return jobs;
}
function workflowJobIds(rel) {
  return workflowJobs(rel).map((j) => j.id);
}
function workflowJobBody(rel, id) {
  const job = workflowJobs(rel).find((j) => j.id === id);
  return job ? job.body.join("\n") : "";
}
function rulesetRequiredChecks(rel) {
  let ruleset = {};
  try { ruleset = JSON.parse(readText(rel)); } catch { return []; }
  const rsc = (ruleset.rules || []).find((r) => r.type === "required_status_checks");
  return (((rsc || {}).parameters || {}).required_status_checks || []).map((c) => c.context).filter(Boolean);
}
function codeownersInfo(rel = ".github/CODEOWNERS") {
  const abs = path.join(ROOT, rel);
  const exists = fs.existsSync(abs);
  const text = exists ? readText(rel) : "";
  const ownerLines = text.split(/\r?\n/)
    .map((line) => line.replace(/\s+#.*$/, "").trim())
    .filter((line) => line && !line.startsWith("#") && /\s@[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)?\b/.test(line));
  return { rel, exists, hasOwner: ownerLines.length > 0, tracked: !inRepo || tracked(rel) };
}
function checkRulesetPrReview(rel) {
  let ruleset = {};
  try { ruleset = JSON.parse(readText(rel)); } catch { return; }
  const pr = (ruleset.rules || []).find((r) => r.type === "pull_request");
  const p = (pr && pr.parameters) || {};
  const codeowners = codeownersInfo();
  if (Number(p.required_approving_review_count || 0) < 1)
    warn("ruleset: pull_request does not require approving review; this is only appropriate for a solo-maintainer source repo");
  else ok("ruleset: pull_request requires approving review");
  if (p.require_code_owner_review === true) {
    ok("ruleset: code-owner review required");
    if (!codeowners.exists) fail("ruleset: code-owner review requires .github/CODEOWNERS");
    else if (!codeowners.tracked) fail("ruleset: .github/CODEOWNERS must be tracked");
    else if (!codeowners.hasOwner) fail("ruleset: code-owner review is enabled but .github/CODEOWNERS has no owner entries");
    else ok("ruleset: CODEOWNERS has owner entries and is tracked");
  } else {
    if (codeowners.exists && codeowners.hasOwner)
      warn("ruleset: CODEOWNERS has owner entries but required code-owner review is disabled");
    else
      ok("ruleset: code-owner review disabled and CODEOWNERS has no required owner configured");
  }
}
function checkVerifyJobContract(workflowPath, required) {
  if (!required.includes("verify")) return;
  const body = workflowJobBody(workflowPath, "verify");
  if (!body) { fail("CI job verify is required by ruleset but its workflow body was not found"); return; }
  const checks = [
    { name: "doctor", re: /node\s+hooks\/doctor\.js\b/ },
    { name: "verify", re: /node\s+hooks\/verify\.js\b/ },
    { name: "design-gate strict", re: /node\s+hooks\/design-gate\.js\b[^\n]*--strict\b/ },
    { name: "secret scan", re: /gitleaks/i },
  ];
  const missing = checks.filter((c) => !c.re.test(body)).map((c) => c.name);
  if (missing.length) fail(`CI job verify does not run required harness step(s): ${missing.join(", ")}`);
  else ok("CI job verify runs doctor, verify.js, design-gate --strict and secret scan");
}
function checkWorkflowSupplyChain(workflowPath) {
  const text = readText(workflowPath);
  const unpinned = [];
  for (const m of text.matchAll(/uses:\s*([^\s#]+)/g)) {
    const spec = m[1];
    if (!/@[0-9a-f]{40}$/i.test(spec)) unpinned.push(spec);
  }
  if (unpinned.length) fail(`CI action(s) not pinned to full SHA: ${unpinned.join(", ")}`);
  else ok("CI actions are pinned to full commit SHAs");

  if (/ecc-agentshield@/.test(text)) {
    /AGENTSHIELD_INTEGRITY:\s*["']sha512-/.test(text) && /NPM_CONFIG_IGNORE_SCRIPTS:\s*["']true["']/.test(text)
      ? ok("CI AgentShield npm package has integrity pin and install scripts disabled")
      : fail("CI AgentShield npm package must pin dist.integrity and set NPM_CONFIG_IGNORE_SCRIPTS=true");
  }
}
function checkReleaseWorkflowContract(workflowPath) {
  const text = readText(workflowPath);
  const checks = [
    { name: "v* tag trigger", re: /tags:\s*\[[^\]]*["']?v\*/i },
    { name: "contents write permission", re: /contents:\s*write/i },
    { name: "post-merge preflight", re: /release-preflight\.js[^\n]*--require-tag-in-base[^\n]*--allow-remote-tag/i },
    { name: "VERIFY", re: /node\s+hooks\/verify\.js\b/i },
    { name: "exact-tag git archive", re: /git\s+archive\b/i },
    { name: "SHA-256", re: /Get-FileHash[^\n]*SHA256/i },
    { name: "archive smoke test", re: /Expand-Archive/i },
    { name: "GitHub Release publication", re: /gh\s+release\s+(?:create|upload)/i },
  ];
  const missing = checks.filter((check) => !check.re.test(text)).map((check) => check.name);
  if (missing.length) fail(`release workflow is missing required step(s): ${missing.join(", ")}`);
  else ok("release workflow validates merged tags, builds/checksums/smokes source ZIP, and publishes GitHub Release");
}

// node / git
ok("node " + process.version);
const gv = gitSafe(["--version"]);
gv ? ok(gv) : fail("git not found in PATH");

// repo
const inRepo = gitSafe(["rev-parse", "--is-inside-work-tree"]) === "true";
if (!inRepo) {
  fail("not a git repository; run inside a repository");
} else {
  const name = gitSafe(["config", "--get", "user.name"]);
  const email = gitSafe(["config", "--get", "user.email"]);
  (name && email) ? ok("git identity: " + name + " <" + email + ">") : warn("git user.name/email are not configured");

  // lefthook wired into .git/hooks? (lefthook install writes a stub referencing lefthook)
  const hooksDir = gitSafe(["rev-parse", "--git-path", "hooks"]) || ".git/hooks";
  const hooksDirAbs = path.isAbsolute(hooksDir) ? hooksDir : path.join(ROOT, hooksDir);
  let wired = false;
  for (const h of ["pre-commit", "commit-msg", "pre-push"]) {
    try {
      if (/lefthook/i.test(fs.readFileSync(path.join(hooksDirAbs, h), "utf8"))) { wired = true; break; }
    } catch {}
  }
  wired ? ok("lefthook wired into .git/hooks") : warn("hooks are not installed; run: lefthook install");

  // .git must support the full lock-file lifecycle (write + unlink): git updates
  // the index and refs through <name>.lock -> rename/unlink. On filesystems that
  // cannot delete files (some network/container/FUSE mounts), commit/checkout/rebase
  // fails with "index.lock: File exists". Probe the real behavior instead of guessing.
  const gitDir = gitSafe(["rev-parse", "--git-dir"]) || ".git";
  const gitDirAbs = path.isAbsolute(gitDir) ? gitDir : path.join(ROOT, gitDir);
  const probe = path.join(gitDirAbs, ".doctor-lock-probe-" + process.pid);
  try {
    fs.writeFileSync(probe, "x");
    try {
      fs.unlinkSync(probe);
      ok(".git supports atomic lock operations (write + unlink)");
    } catch {
    fail(".git cannot delete files; git cannot clean up lock files and commit/checkout/rebase will fail. Check mount or permissions.");
    }
  } catch {
    fail(".git is not writable; git add/commit/checkout will not work. Check permissions or mount.");
  }
  try {
    if (fs.existsSync(path.join(gitDirAbs, "index.lock")))
      warn("stale .git/index.lock detected; remove it only if no git process is running");
  } catch {}
}

// runner + delegated tools in PATH (WARN, not FAIL; CI provides them)
const tools = [
  ["lefthook", "git hook runner (lefthook install)"],
  ["gitleaks", "secret scanning (pre-commit + CI)"],
  ["cog", "cocogitto: conventional commits + release"],
];
for (const t of tools) {
  inPath(t[0]) ? ok(t[0] + " found") : warn(t[0] + " not in PATH - " + t[1]);
}

const requiredHarnessFiles = [
  "hooks/verify.js",
  "hooks/verify-core.js",
  "hooks/design-gate.js",
  "hooks/release-manifest-bump.js",
  "hooks/release-preflight.js",
  "hooks/post-merge-cleanup.js",
  "hooks/release-cleanup.js",
  "hooks/repo-state-audit.js",
  "hooks/new-mockups.js",
  "hooks/doctor.js",
  "hooks/apply-ruleset.js",
  "hooks/_lib.js",
  "hooks/branch-guard.js",
  "hooks/no-coauthor.js",
  "hooks/agent/guard.js",
  "hooks/agent/_input.js",
  "hooks/agent/stop-reminder.js",
  "harness.config.json",
  "lefthook.yml",
  "cog.toml",
  "CHANGELOG.md",
  ".gitleaks.toml",
  "AGENTS.md",
  "settings.example.json",
  ".github/rulesets/main.json",
  ".github/workflows/ci.yml",
  ".github/CODEOWNERS",
];
const missingHarness = [];
const untrackedHarness = [];
for (const f of requiredHarnessFiles) {
  if (!fs.existsSync(path.join(ROOT, f))) missingHarness.push(f);
  else if (inRepo && !tracked(f)) untrackedHarness.push(f);
}
if (missingHarness.length || untrackedHarness.length) {
  const parts = [];
  if (missingHarness.length) parts.push("missing: " + missingHarness.join(", "));
  if (untrackedHarness.length) parts.push("untracked: " + untrackedHarness.join(", "));
  fail("harness not bootstrapped into repository main - " + parts.join("; ") +
    ". Create a bootstrap PR and commit these files before the dev/release loop.");
} else {
  ok("harness bootstrap files present and tracked");
}

// Critical harness files must be portable across Windows/macOS/Linux checkouts.
const textCritical = requiredHarnessFiles.concat([
  ".gitattributes",
  ".gitignore",
  "README.md",
  "CLAUDE.md",
  "BACKLOG.md",
  ".github/workflows/ci.yml",
  ".github/workflows/release.yml",
  ".github/CODEOWNERS",
  ".github/dependabot.yml",
  "install.cmd",
  "install.sh",
]).filter((f, i, a) => a.indexOf(f) === i && fs.existsSync(path.join(ROOT, f)));
for (const f of textCritical) checkTextFile(f);

// harness.config.json valid JSON
const cfgPath = path.join(ROOT, "harness.config.json");
let harnessConfig = {};
if (fs.existsSync(cfgPath)) {
  try {
    const cfg = JSON.parse(fs.readFileSync(cfgPath, "utf8"));
    harnessConfig = cfg;
    ok("harness.config.json is valid JSON");
    const hasSelfTest = fs.existsSync(path.join(ROOT, "hooks", "test.js"));
    const stacks = cfg.verify && Array.isArray(cfg.verify.stacks) ? cfg.verify.stacks : null;
    if (hasSelfTest && stacks) {
      const harness = stacks.find((s) => s && s.id === "harness");
      const steps = (harness && Array.isArray(harness.steps)) ? harness.steps : [];
      const runsSelfTest = steps.some((s) => /node\s+test\.js\b/.test(String(s && s.run || "")));
      runsSelfTest ? ok("harness.config.json: VERIFY includes harness self-test") :
        fail("harness.config.json: verify.stacks is set but required harness self-test (node test.js) is missing");
    }
  }
  catch (e) { fail("harness.config.json is invalid: " + e.message); }
}

const cogPath = path.join(ROOT, "cog.toml");
if (fs.existsSync(cogPath)) {
  const cog = fs.readFileSync(cogPath, "utf8");
  const changelogSection = tomlSection(cog, "changelog");
  const changelogTemplate = tomlString(changelogSection, "template");
  const changelogOwner = tomlString(changelogSection, "owner");
  const changelogRepository = tomlString(changelogSection, "repository");
  /from_latest_tag\s*=\s*true/.test(cog) ? ok("cog.toml: from_latest_tag=true") : fail("cog.toml: from_latest_tag=true is required for release bumps from the latest v* tag");
  /ignore_merge_commits\s*=\s*true/.test(cog) ? ok("cog.toml: ignore_merge_commits=true") : fail("cog.toml: ignore_merge_commits=true is required");
  /tag_prefix\s*=\s*"v"/.test(cog) ? ok("cog.toml: tag_prefix=\"v\"") : fail("cog.toml: tag_prefix=\"v\" is required");
  /branch_whitelist\s*=\s*\[[^\]]*"release\/\*\*"/s.test(cog)
    ? ok("cog.toml: branch_whitelist includes release/**")
    : fail("cog.toml: branch_whitelist must include release/** for release worktrees");
  changelogTemplate === "remote"
    ? ok("cog.toml: changelog.template=\"remote\"")
    : fail("cog.toml: changelog.template=\"remote\" is required for github.com remote changelog generation");
  changelogOwner
    ? ok("cog.toml: changelog.owner set")
    : fail("cog.toml: changelog.owner is required for remote changelog generation");
  changelogRepository
    ? ok("cog.toml: changelog.repository set")
    : fail("cog.toml: changelog.repository is required for remote changelog generation");
  if (changelogTemplate === "remote" && (changelogOwner || changelogRepository)) {
    const origin = githubRepoFromUrl(gitSafe(["remote", "get-url", "origin"]));
    if (!origin) {
      fail("cog.toml: remote changelog generation requires a GitHub origin remote");
    } else {
      const ownerMatches = changelogOwner && changelogOwner.toLowerCase() === origin.owner.toLowerCase();
      const repoMatches = changelogRepository && changelogRepository.toLowerCase() === origin.repository.toLowerCase();
      ownerMatches
        ? ok("cog.toml: changelog.owner matches origin")
        : fail(`cog.toml: changelog.owner must match origin owner (${origin.owner})`);
      repoMatches
        ? ok("cog.toml: changelog.repository matches origin")
        : fail(`cog.toml: changelog.repository must match origin repository (${origin.repository})`);
    }
  }
  const changelogPath = path.join(ROOT, "CHANGELOG.md");
  let changelog = "";
  try { changelog = fs.readFileSync(changelogPath, "utf8"); } catch {}
  changelog
    ? ok("CHANGELOG.md: present")
    : fail("CHANGELOG.md is required for cog bump changelog generation");
  /^- - -\s*$/m.test(changelog)
    ? ok("CHANGELOG.md: contains Cocogitto separator - - -")
    : fail("CHANGELOG.md must contain Cocogitto separator line - - -");
}

const workflowPath = ".github/workflows/ci.yml";
const releaseWorkflowPath = ".github/workflows/release.yml";
const rulesetPath = ".github/rulesets/main.json";
if (fs.existsSync(path.join(ROOT, rulesetPath))) {
  const jobs = workflowJobIds(workflowPath);
  const required = rulesetRequiredChecks(rulesetPath);
  if (required.length && !fs.existsSync(path.join(ROOT, workflowPath))) {
    fail(`ruleset required check(s) cannot run because ${workflowPath} is missing: ${required.join(", ")}`);
  } else if (fs.existsSync(path.join(ROOT, workflowPath))) {
    const missing = required.filter((ctx) => !jobs.includes(ctx));
    if (missing.length) fail(`ruleset required check(s) not published by CI workflow: ${missing.join(", ")} (jobs: ${jobs.join(", ") || "none"})`);
    else if (required.length) {
      ok("ruleset required checks match CI workflow job ids");
      checkVerifyJobContract(workflowPath, required);
    }
    checkWorkflowSupplyChain(workflowPath);
  }
  checkRulesetPrReview(rulesetPath);
}
if (fs.existsSync(path.join(ROOT, releaseWorkflowPath))) {
  if (harnessConfig.release && harnessConfig.release.sourceZip === true) {
    checkReleaseWorkflowContract(releaseWorkflowPath);
  } else {
    ok("release workflow uses a target-specific artifact contract");
  }
  checkWorkflowSupplyChain(releaseWorkflowPath);
}

// report
const fails = results.filter((r) => r.level === "FAIL").length;
if (process.argv.includes("--json")) {
  console.log(JSON.stringify({ ok: fails === 0, results }));
} else {
  console.log("harness doctor:");
  const icon = { PASS: "OK", WARN: "WARN", FAIL: "FAIL" };
  for (const r of results) console.log("  " + icon[r.level] + " " + r.msg);
  console.log(fails ? "\ndoctor: " + fails + " FAIL - fix before work." : "\ndoctor: environment is ready.");
}
process.exit(fails ? 1 : 0);
