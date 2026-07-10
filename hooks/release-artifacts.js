#!/usr/bin/env node
// release-artifacts.js - execute project-owned build/smoke/version contracts.
// Commands receive HARNESS_RELEASE_TAG, HARNESS_RELEASE_VERSION, and
// HARNESS_ARTIFACT_PATH. Artifact paths stay repository-confined.

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");
const { loadReleaseConfig } = require("./release-config.js");

function parseArgs(argv) {
  const out = { root: process.cwd(), tag: "", phase: "all", json: false, errors: [] };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (["--root", "--tag", "--phase"].includes(arg)) {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) out.errors.push(`${arg} requires a value`);
      else if (arg === "--root") out.root = argv[++i];
      else if (arg === "--tag") out.tag = argv[++i];
      else out.phase = argv[++i];
    } else if (arg === "--json") out.json = true;
    else out.errors.push(`unknown option: ${arg}`);
  }
  out.root = path.resolve(out.root);
  if (!/^v\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(out.tag)) out.errors.push("--tag must look like vX.Y.Z");
  if (!["build", "smoke", "version", "all"].includes(out.phase)) out.errors.push("--phase must be build, smoke, version, or all");
  return out;
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
  const report = { ok: true, root: args.root, tag: args.tag, version, phase: args.phase, artifacts: [], results: [] };
  let runnable = 0;

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
      add(report, "PASS", `artifact ${item.id} is verified by the project release workflow`);
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
  else if (!runnable) add(report, "WARN", "all artifact contracts are workflow-owned; no local artifact command was run");
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

module.exports = { parseArgs, artifactPath, versionMatches, runContract };
if (require.main === module) main();
