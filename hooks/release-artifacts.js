#!/usr/bin/env node
// release-artifacts.js - execute project-owned build/smoke/version contracts.
// Commands receive HARNESS_RELEASE_TAG, HARNESS_RELEASE_VERSION, and
// HARNESS_ARTIFACT_PATH. Artifact paths stay repository-confined. Workflow-owned
// phase-all checks consume downloaded release-evidence.json plus asset/checksum.

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const { spawnSync } = require("child_process");
const { loadReleaseConfig } = require("./release-config.js");

function parseArgs(argv) {
  const out = { root: process.cwd(), tag: "", phase: "all", evidence: "", json: false, errors: [] };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (["--root", "--tag", "--phase", "--evidence"].includes(arg)) {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) out.errors.push(`${arg} requires a value`);
      else if (arg === "--root") out.root = argv[++i];
      else if (arg === "--tag") out.tag = argv[++i];
      else if (arg === "--phase") out.phase = argv[++i];
      else out.evidence = argv[++i];
    } else if (arg === "--json") out.json = true;
    else out.errors.push(`unknown option: ${arg}`);
  }
  out.root = path.resolve(out.root);
  if (out.evidence) out.evidence = path.resolve(out.root, out.evidence);
  if (!/^v\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(out.tag)) out.errors.push("--tag must look like vX.Y.Z");
  if (!["build", "smoke", "version", "all"].includes(out.phase)) out.errors.push("--phase must be build, smoke, version, or all");
  return out;
}

function loadEvidence(file, tag) {
  if (!file) return { ok: false, error: "workflow-owned artifacts require --evidence <release-evidence.json> for phase all", dir: "", entries: new Map() };
  let value;
  try { value = JSON.parse(fs.readFileSync(file, "utf8")); }
  catch (e) { return { ok: false, error: `cannot read release evidence: ${e.message}`, dir: path.dirname(file), entries: new Map() }; }
  if (!value || value.schemaVersion !== 1 || value.tag !== tag || !Array.isArray(value.artifacts)) {
    return { ok: false, error: "release evidence requires schemaVersion 1, the exact tag, and an artifacts array", dir: path.dirname(file), entries: new Map() };
  }
  const entries = new Map();
  for (const entry of value.artifacts) {
    if (!entry || typeof entry.id !== "string" || !entry.id || entries.has(entry.id)) {
      return { ok: false, error: "release evidence artifact ids must be non-empty and unique", dir: path.dirname(file), entries: new Map() };
    }
    entries.set(entry.id, entry);
  }
  return { ok: true, error: "", dir: path.dirname(file), entries };
}

function isHttpsUrl(value) {
  try { return new URL(String(value)).protocol === "https:"; } catch { return false; }
}

function sha256File(file) {
  return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

function verifyWorkflowEvidence(item, evidence, version) {
  const entry = evidence.entries.get(String(item.id));
  if (!entry) return { ok: false, error: `release evidence is missing artifact ${item.id}` };
  if (!isHttpsUrl(entry.workflowUrl) || !isHttpsUrl(entry.releaseUrl)) {
    return { ok: false, error: `artifact ${item.id} evidence requires HTTPS workflowUrl and releaseUrl` };
  }
  if (entry.smokePassed !== true || entry.version !== version) {
    return { ok: false, error: `artifact ${item.id} evidence must attest smokePassed=true and version ${version}` };
  }
  const asset = path.resolve(evidence.dir, String(entry.assetPath || ""));
  const checksum = path.resolve(evidence.dir, String(entry.checksumPath || ""));
  if (!entry.assetPath || !entry.checksumPath || !fs.existsSync(asset) || !fs.existsSync(checksum)) {
    return { ok: false, error: `artifact ${item.id} downloaded asset or checksum file is missing` };
  }
  let expected = "";
  let listedName = "";
  try {
    const match = fs.readFileSync(checksum, "utf8").trim().match(/^([0-9a-f]{64})\s+\*?(.+)$/i);
    if (match) [, expected, listedName] = match;
  } catch {}
  if (!expected || path.basename(listedName.trim()) !== path.basename(asset)) {
    return { ok: false, error: `artifact ${item.id} checksum file is invalid or names a different asset` };
  }
  let actual;
  try { actual = sha256File(asset); }
  catch (e) { return { ok: false, error: `cannot hash artifact ${item.id}: ${e.message}` }; }
  if (actual.toLowerCase() !== expected.toLowerCase()) {
    return { ok: false, error: `artifact ${item.id} SHA-256 does not match its published checksum` };
  }
  return {
    ok: true, id: String(item.id), assetPath: asset, checksumPath: checksum,
    sha256: actual, workflowUrl: entry.workflowUrl, releaseUrl: entry.releaseUrl,
    smokePassed: true, version,
  };
}

function add(report, level, message, extra = {}) {
  report.results.push({ level, message, ...extra });
}

function artifactPath(root, configured, tag, version) {
  const rel = String(configured || "").replaceAll("{tag}", tag).replaceAll("{version}", version).replace(/\\/g, "/");
  const abs = path.resolve(root, rel);
  const base = path.resolve(root);
  if (!rel || path.isAbsolute(rel) || rel.startsWith("../") || (abs !== base && !abs.startsWith(base + path.sep))) return null;
  return { rel, abs };
}

function runCommand(command, root, env, timeoutMs) {
  const result = spawnSync(String(command), {
    cwd: root, shell: true, encoding: "utf8", env: { ...process.env, ...env },
    timeout: timeoutMs, killSignal: "SIGKILL", maxBuffer: 10 * 1024 * 1024,
  });
  return {
    ok: !result.error && result.status === 0,
    code: result.status,
    output: (String(result.stdout || "") + String(result.stderr || "")).trim().slice(-4000),
    error: result.error ? result.error.message : "",
  };
}

function versionMatches(output, version, pattern) {
  try {
    const source = pattern
      ? String(pattern).replaceAll("{version}", version.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"))
      : `(^|[^0-9A-Za-z])${version.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}([^0-9A-Za-z]|$)`;
    return new RegExp(source, "m").test(String(output || ""));
  } catch { return false; }
}

function runContract(args) {
  const version = args.tag.slice(1);
  const contract = loadReleaseConfig(args.root);
  const report = { ok: true, root: args.root, tag: args.tag, version, phase: args.phase, evidence: args.evidence || "", artifacts: [], results: [] };
  let runnable = 0;
  const workflowEvidence = args.phase === "all" ? loadEvidence(args.evidence, args.tag) : null;

  if (contract.provider === "none") {
    add(report, "FAIL", "release capability is disabled in harness.config.json");
    report.ok = false;
    return report;
  }

  for (const item of contract.artifacts) {
    if (!item || typeof item !== "object" || !item.id) {
      add(report, "FAIL", "release artifact entry must be an object with id");
      continue;
    }
    if (item.workflowOwned === true) {
      if (args.phase !== "all") {
        add(report, "WARN", `artifact ${item.id} is workflow-owned; verification is deferred to phase all`);
        continue;
      }
      if (!workflowEvidence.ok) {
        add(report, "FAIL", workflowEvidence.error);
        continue;
      }
      const verified = verifyWorkflowEvidence(item, workflowEvidence, version);
      if (!verified.ok) add(report, "FAIL", verified.error);
      else {
        report.artifacts.push(verified);
        add(report, "PASS", `artifact ${item.id} published evidence passed with SHA-256 ${verified.sha256}`);
      }
      continue;
    }
    runnable++;
    const location = artifactPath(args.root, item.path, args.tag, version);
    if (!location) {
      add(report, "FAIL", `artifact ${item.id} has an invalid or repository-external path`);
      continue;
    }
    const timeoutMs = Number.isFinite(Number(item.timeoutMs)) ? Math.max(1000, Number(item.timeoutMs)) : 120000;
    const env = {
      HARNESS_RELEASE_TAG: args.tag,
      HARNESS_RELEASE_VERSION: version,
      HARNESS_ARTIFACT_PATH: location.abs,
      HARNESS_ARTIFACT_ID: String(item.id),
    };
    const artifact = { id: String(item.id), path: location.rel, phases: [] };
    report.artifacts.push(artifact);

    if (["build", "all"].includes(args.phase) && item.build) {
      const result = runCommand(item.build, args.root, env, timeoutMs);
      artifact.phases.push({ phase: "build", ...result });
      result.ok ? add(report, "PASS", `artifact ${item.id} build command passed`) :
        add(report, "FAIL", `artifact ${item.id} build command failed`, result);
    }
    if (!fs.existsSync(location.abs)) {
      item.required === false ? add(report, "WARN", `optional artifact ${item.id} is missing: ${location.rel}`) :
        add(report, "FAIL", `artifact ${item.id} is missing: ${location.rel}`);
      continue;
    }
    add(report, "PASS", `artifact ${item.id} exists: ${location.rel}`);

    if (["smoke", "all"].includes(args.phase)) {
      if (!item.smoke) add(report, "FAIL", `artifact ${item.id} has no smoke command`);
      else {
        const result = runCommand(item.smoke, args.root, env, timeoutMs);
        artifact.phases.push({ phase: "smoke", ...result });
        result.ok ? add(report, "PASS", `artifact ${item.id} smoke command passed`) :
          add(report, "FAIL", `artifact ${item.id} smoke command failed`, result);
      }
    }
    if (["version", "all"].includes(args.phase)) {
      if (!item.versionCommand) add(report, "FAIL", `artifact ${item.id} has no versionCommand`);
      else {
        const result = runCommand(item.versionCommand, args.root, env, timeoutMs);
        const matches = result.ok && versionMatches(result.output, version, item.versionPattern);
        artifact.phases.push({ phase: "version", ...result, matches });
        matches ? add(report, "PASS", `artifact ${item.id} reports version ${version}`) :
          add(report, "FAIL", `artifact ${item.id} version output does not match ${version}`, result);
      }
    }
  }

  if (!contract.artifacts.length) add(report, "FAIL", "release.artifacts is empty; project artifact evidence is not configured");
  else if (!runnable && args.phase !== "all") add(report, "WARN", "all artifact contracts are workflow-owned; published evidence is checked in phase all");
  report.ok = !report.results.some((result) => result.level === "FAIL");
  return report;
}

function print(report, json) {
  if (json) return console.log(JSON.stringify(report));
  console.log(`release artifacts: ${report.tag || "(missing tag)"} [${report.phase}]`);
  const icon = { PASS: "+", WARN: "!", FAIL: "X" };
  for (const result of report.results) console.log(`  ${icon[result.level]} ${result.message}`);
  console.log(report.ok ? "\nrelease artifact contract passed." : "\nrelease artifact contract failed.");
}

function main(argv = process.argv.slice(2)) {
  const args = parseArgs(argv);
  if (args.errors.length) {
    const report = { ok: false, root: args.root, tag: args.tag, phase: args.phase, artifacts: [], results: args.errors.map((message) => ({ level: "FAIL", message })) };
    print(report, args.json);
    process.exit(1);
  }
  const report = runContract(args);
  print(report, args.json);
  process.exit(report.ok ? 0 : 1);
}

module.exports = { artifactPath, loadEvidence, parseArgs, runContract, verifyWorkflowEvidence, versionMatches };
if (require.main === module) main();
