#!/usr/bin/env node
// no-coauthor.js - Windows-safe commit-msg policy for AI/co-author trailers.
//
// Keep this out of a multi-line shell snippet. Lefthook wraps commands through a
// shell on Windows, and quoted echo/grep snippets can fail before policy runs.

const fs = require("fs");

const FORBIDDEN_RE = /co-authored-by:|(generated|created|written|authored) (with|by)[^|]*(claude|copilot|chatgpt|gpt|cursor|gemini|codex|aider|an? ?(ai|llm))|\u{1F916} generated/iu;

function hasForbiddenTrailer(message) {
  return FORBIDDEN_RE.test(String(message || ""));
}

function main(argv) {
  const file = argv[2];
  if (!file) {
    console.error("commit-msg: missing commit message file path.");
    return 1;
  }

  let message;
  try {
    message = fs.readFileSync(file, "utf8");
  } catch (e) {
    console.error(`commit-msg: cannot read commit message file: ${e.message}`);
    return 1;
  }

  if (!hasForbiddenTrailer(message)) return 0;

  console.error("commit-msg: forbidden co-author or AI-generated trailer.");
  console.error("Remove Co-Authored-By / Generated with ... from the commit message.");
  return 1;
}

if (require.main === module) process.exit(main(process.argv));

module.exports = { FORBIDDEN_RE, hasForbiddenTrailer, main };
