#!/usr/bin/env node
// branch-guard.js - Windows-safe pre-commit branch guard.
//
// Lefthook runs multi-line shell snippets through `sh -c "..."` on Windows. Any
// quoted echo inside that snippet can break command-line quoting before the shell
// even starts. Keep this guard as a small Node program instead.

const { execFileSync } = require("child_process");

function currentBranch() {
  try {
    return execFileSync("git", ["symbolic-ref", "--short", "HEAD"], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
      timeout: 5000,
      killSignal: "SIGKILL",
    }).trim();
  } catch {
    return "";
  }
}

const branch = currentBranch();
if (!["main", "master"].includes(branch)) process.exit(0);

if (String(process.env.HARNESS_ALLOW_MAIN || "").trim() === "1") process.exit(0);

console.error(`pre-commit: commits on protected branch "${branch}" are blocked.`);
console.error("Create a feature branch first: git checkout -b feat/...");
console.error("Release/hotfix exception: HARNESS_ALLOW_MAIN=1 git commit ...");
process.exit(1);
