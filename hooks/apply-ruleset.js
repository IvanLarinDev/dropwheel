#!/usr/bin/env node
// apply-ruleset.js - install the versioned branch ruleset (the REAL, server-side gate).
//
// Local hooks (lefthook + gitleaks + cocogitto) are HYGIENE: fast, but an agent with
// write access can bypass them. Only a GitHub ruleset with a required status check
// actually blocks a merge. This applies .github/rulesets/main.json via `gh api`.
//
// Requires: gh CLI authenticated with repo-admin rights.
// Private repos need GitHub Pro/Team/Enterprise for rulesets (see BACKLOG P0-0).
//
//   node hooks/apply-ruleset.js            # create/replace the "protect-main" ruleset
//   node hooks/apply-ruleset.js --dry-run  # print what would be sent
//   node hooks/apply-ruleset.js --check    # read-only live drift check
//   node hooks/apply-ruleset.js --check --json
//
// Exit 0 = applied (or dry-run), 1 = failed.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const rulesetPath = path.join(__dirname, "..", ".github", "rulesets", "main.json");

function gh(args, opts = {}) {
  return execFileSync("gh", args, { encoding: "utf8", stdio: ["pipe", "pipe", "pipe"], ...opts }).trim();
}
function stable(value) {
  if (Array.isArray(value)) return value.map(stable);
  if (!value || typeof value !== "object") return value;
  return Object.fromEntries(Object.keys(value).sort().map((k) => [k, stable(value[k])]));
}
function same(a, b) {
  return JSON.stringify(stable(a)) === JSON.stringify(stable(b));
}
function parseRulesetList(text) {
  const raw = String(text || "").trim();
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.flat() : [parsed];
  } catch {}
  const out = [];
  for (const line of raw.split(/\r?\n/).map((s) => s.trim()).filter(Boolean)) {
    const parsed = JSON.parse(line);
    if (Array.isArray(parsed)) out.push(...parsed);
    else out.push(parsed);
  }
  return out;
}
function ruleByType(ruleset, type) {
  return ((ruleset && ruleset.rules) || []).find((r) => r.type === type) || null;
}
function requiredCheckKey(check) {
  return `${check.context || ""}\u0000${check.integration_id === undefined ? "" : check.integration_id}`;
}
function compareRuleset(expected, actual) {
  const mismatches = [];
  for (const key of ["name", "target", "enforcement"]) {
    if (expected[key] !== actual[key]) mismatches.push(`${key}: expected ${expected[key]}, got ${actual[key]}`);
  }
  if (!same(expected.conditions || {}, actual.conditions || {})) mismatches.push("conditions differ");
  for (const expRule of expected.rules || []) {
    const gotRule = ruleByType(actual, expRule.type);
    if (!gotRule) { mismatches.push(`missing rule: ${expRule.type}`); continue; }
    if (expRule.type === "required_status_checks") {
      const expParams = expRule.parameters || {};
      const gotParams = gotRule.parameters || {};
      if (expParams.strict_required_status_checks_policy !== gotParams.strict_required_status_checks_policy)
        mismatches.push("required_status_checks.strict_required_status_checks_policy differs");
      const gotChecks = new Set((gotParams.required_status_checks || []).map(requiredCheckKey));
      for (const check of expParams.required_status_checks || []) {
        if (!gotChecks.has(requiredCheckKey(check)))
          mismatches.push(`required status check missing: ${check.context} / ${check.integration_id}`);
      }
      continue;
    }
    for (const [key, value] of Object.entries(expRule.parameters || {})) {
      if (!same(value, ((gotRule.parameters || {})[key])))
        mismatches.push(`${expRule.type}.${key} differs`);
    }
  }
  return mismatches;
}

function parseArgs(argv) {
  const out = { dryRun: false, check: false, json: false, errors: [] };
  for (const arg of argv) {
    if (arg === "--dry-run") out.dryRun = true;
    else if (arg === "--check") out.check = true;
    else if (arg === "--json") out.json = true;
    else out.errors.push(`unknown option: ${arg}`);
  }
  if (out.dryRun && out.check) out.errors.push("--dry-run and --check are mutually exclusive");
  return out;
}

function driftReport(expected, listed, actual, repo = "") {
  const found = (listed || []).find((r) => r.name === expected.name);
  if (!found) return { ok: false, mode: "check", repo, name: expected.name, id: "", mismatches: ["ruleset is missing on server"] };
  const mismatches = compareRuleset(expected, actual || found);
  return { ok: mismatches.length === 0, mode: "check", repo, name: expected.name, id: String(found.id || ""), mismatches };
}

function emit(report, json) {
  if (json) console.log(JSON.stringify(report));
  else if (report.ok) {
    console.log(`OK ruleset "${report.name}" ${report.mode === "check" ? "matches" : report.action} on ${report.repo || "target repository"}.`);
    if (report.mode === "check") console.log("   Live readback matches PR policy, required checks, conditions, and enforcement.");
  } else {
    console.error(`FAIL ruleset "${report.name || "unknown"}" ${report.mode === "check" ? "drift check" : "operation"} failed${report.repo ? ` on ${report.repo}` : ""}:`);
    for (const mismatch of report.mismatches || []) console.error(`   - ${mismatch}`);
    if (report.error) console.error(`   - ${report.error}`);
  }
}

function main(argv = process.argv.slice(2)) {
  const args = parseArgs(argv);
  if (args.errors.length) {
    const report = { ok: false, mode: "invalid", name: "", mismatches: args.errors };
    emit(report, args.json);
    process.exit(1);
  }
  try {
    const raw = JSON.parse(fs.readFileSync(rulesetPath, "utf8"));
    delete raw._comment; // strip the doc comment before sending
    const body = JSON.stringify(raw);

    if (args.dryRun) {
      if (args.json) console.log(JSON.stringify({ ok: true, mode: "dry-run", name: raw.name, ruleset: raw }));
      else {
        console.log("would apply this ruleset to repos/<owner>/<repo>/rulesets:\n");
        console.log(JSON.stringify(raw, null, 2));
      }
      process.exit(0);
    }

    const repo = gh(["repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"]);

    // Replace an existing "protect-main" ruleset if present (idempotent).
    const list = parseRulesetList(gh(["api", "--paginate", `repos/${repo}/rulesets`]));
    const found = list.find((r) => r.name === raw.name);
    const existingId = found ? String(found.id) : "";

    if (args.check) {
      const actual = existingId ? JSON.parse(gh(["api", `repos/${repo}/rulesets/${existingId}`])) : null;
      const report = driftReport(raw, list, actual, repo);
      emit(report, args.json);
      process.exit(report.ok ? 0 : 1);
    }

    const apiArgs = existingId
      ? ["api", "-X", "PUT", `repos/${repo}/rulesets/${existingId}`, "--input", "-"]
      : ["api", "-X", "POST", `repos/${repo}/rulesets`, "--input", "-"];

    const written = JSON.parse(gh(apiArgs, { input: body }));
    const id = String(written.id || existingId || "");
    const applied = id ? JSON.parse(gh(["api", `repos/${repo}/rulesets/${id}`])) : written;
    const mismatches = compareRuleset(raw, applied);
    if (mismatches.length) {
      emit({ ok: false, mode: "apply", repo, name: raw.name, id, mismatches }, args.json);
      process.exit(1);
    }
    emit({ ok: true, mode: "apply", action: existingId ? "updated" : "created", repo, name: raw.name, id, mismatches: [] }, args.json);
    process.exit(0);
  } catch (e) {
    const report = { ok: false, mode: args.check ? "check" : "apply", name: "", mismatches: [], error: e.message };
    emit(report, args.json);
    if (!args.json) {
      console.error("   Need: gh CLI (authenticated, repo admin) and a plan that supports rulesets");
      console.error("   (private repos need Pro/Team/Enterprise; or make the repo public). See BACKLOG P0-0.");
    }
    process.exit(1);
  }
}

module.exports = { parseArgs, parseRulesetList, compareRuleset, driftReport };
if (require.main === module) main();
