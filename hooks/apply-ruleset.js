#!/usr/bin/env node
// apply-ruleset.js — install the versioned branch ruleset (the REAL, server-side gate).
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

const dryRun = process.argv.includes("--dry-run");
const rulesetPath = path.join(__dirname, "..", ".github", "rulesets", "main.json");

function gh(args, opts = {}) {
  return execFileSync("gh", args, { encoding: "utf8", stdio: ["pipe", "pipe", "pipe"], ...opts }).trim();
}

try {
  const raw = JSON.parse(fs.readFileSync(rulesetPath, "utf8"));
  delete raw._comment; // strip the doc comment before sending
  const body = JSON.stringify(raw);

  if (dryRun) {
    console.log("would POST this ruleset to repos/<owner>/<repo>/rulesets:\n");
    console.log(JSON.stringify(raw, null, 2));
    process.exit(0);
  }

  const repo = gh(["repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"]);

  // Replace an existing "protect-main" ruleset if present (idempotent).
  let existingId = "";
  try {
    const list = JSON.parse(gh(["api", `repos/${repo}/rulesets`]));
    const found = (Array.isArray(list) ? list : []).find((r) => r.name === raw.name);
    if (found) existingId = String(found.id);
  } catch {}

  const apiArgs = existingId
    ? ["api", "-X", "PUT", `repos/${repo}/rulesets/${existingId}`, "--input", "-"]
    : ["api", "-X", "POST", `repos/${repo}/rulesets`, "--input", "-"];

  gh(apiArgs, { input: body });
  console.log(`✅ ruleset "${raw.name}" ${existingId ? "updated" : "created"} on ${repo} (branches: main/master).`);
  console.log("   Enforced: require PR, required check «verify», block force-push & deletion.");
  process.exit(0);
} catch (e) {
  console.error("❌ apply-ruleset failed:", e.message);
  console.error("   Need: gh CLI (authenticated, repo admin) and a plan that supports rulesets");
  console.error("   (private repos → Pro/Team/Enterprise; or make the repo public). See BACKLOG P0-0.");
  process.exit(1);
}
