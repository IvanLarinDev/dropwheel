#!/usr/bin/env node
// Remove one merged development branch after its PR is confirmed MERGED and
// the resulting main CI succeeds. Release/hotfix branches use release cleanup
// after artifact smoke testing instead.
//
// Usage:
//   node hooks/post-merge-cleanup.js --branch feat/name [--root <dir>]
//     [--base origin/main] [--remote origin] [--no-fetch]
//     [--include-equivalent] [--apply] [--json]
//
// --include-equivalent is reserved for provider-confirmed squash/rebase heads;
// use github-branch-cleanup.js instead of asserting that evidence by hand.

const { main } = require("./release-cleanup");

main(process.argv.slice(2), {
  requireBranch: true,
  label: "post-merge cleanup",
});
