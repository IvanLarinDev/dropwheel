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

function main() {
  const dryRun = process.argv.includes("--dry-run");
  try {
    const raw = JSON.parse(fs.readFileSync(rulesetPath, "utf8"));
    delete raw._comment; // strip the doc comment before sending
    const body = JSON.stringify(raw);

    if (dryRun) {
      console.log("would apply this ruleset to repos/<owner>/<repo>/rulesets:\n");
      console.log(JSON.stringify(raw, null, 2));
      process.exit(0);
    }

    const repo = gh(["repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"]);

    // Replace an existing "protect-main" ruleset if present (idempotent).
    const list = parseRulesetList(gh(["api", "--paginate", `repos/${repo}/rulesets`]));
    const found = list.find((r) => r.name === raw.name);
    const existingId = found ? String(found.id) : "";

    const apiArgs = existingId
      ? ["api", "-X", "PUT", `repos/${repo}/rulesets/${existingId}`, "--input", "-"]
      : ["api", "-X", "POST", `repos/${repo}/rulesets`, "--input", "-"];

    const written = JSON.parse(gh(apiArgs, { input: body }));
    const id = String(written.id || existingId || "");
    const applied = id ? JSON.parse(gh(["api", `repos/${repo}/rulesets/${id}`])) : written;
    const mismatches = compareRuleset(raw, applied);
    if (mismatches.length) {
      console.error(`FAIL ruleset "${raw.name}" was written but readback does not match .github/rulesets/main.json:`);
      for (const m of mismatches) console.error(`   - ${m}`);
      process.exit(1);
    }
    console.log(`OK ruleset "${raw.name}" ${existingId ? "updated" : "created"} on ${repo} (branches: main/master).`);
    console.log("   Readback verified: PR policy, required check \"verify\", force-push/delete protection.");
    process.exit(0);
  } catch (e) {
    console.error("FAIL apply-ruleset failed:", e.message);
    console.error("   Need: gh CLI (authenticated, repo admin) and a plan that supports rulesets");
    console.error("   (private repos need Pro/Team/Enterprise; or make the repo public). See BACKLOG P0-0.");
    process.exit(1);
  }
}

module.exports = { parseRulesetList, compareRuleset };
if (require.main === module) main();
