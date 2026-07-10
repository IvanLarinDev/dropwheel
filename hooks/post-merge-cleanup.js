#!/usr/bin/env node
// Remove one merged development branch after its PR is confirmed MERGED and
// the resulting main CI succeeds. Release/hotfix branches use release cleanup
// after artifact smoke testing instead.
//
// Usage:
//   node hooks/post-merge-cleanup.js --branch feat/name [--root <dir>]
//     [--base origin/main] [--remote origin] [--no-fetch] [--apply] [--json]

const { main } = require("./release-cleanup");

main(process.argv.slice(2), {
  requireBranch: true,
  label: "post-merge cleanup",
});
