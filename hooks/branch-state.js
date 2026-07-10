#!/usr/bin/env node
// Shared branch classification for cleanup and terminal topology audits.

const { execFileSync } = require("child_process");

function runGit(root, args, options = {}) {
  return execFileSync("git", args, {
    cwd: root,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    timeout: options.remote ? 60000 : 10000,
    killSignal: "SIGKILL",
  }).trim();
}

function gitResult(root, args, options) {
  try { return { ok: true, out: runGit(root, args, options) }; }
  catch (e) { return { ok: false, error: String((e && e.stderr) || (e && e.message) || "").trim() }; }
}

function lines(text) {
  return String(text || "").split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
}

function count(root, args) {
  const value = gitResult(root, args);
  if (!value.ok || !/^\d+$/.test(value.out)) return null;
  return Number(value.out);
}

function classifyRef(root, ref, base) {
  const oid = gitResult(root, ["rev-parse", "--verify", ref]);
  if (!oid.ok) return { state: "missing", oid: "", reason: `ref not found: ${ref}` };
  if (gitResult(root, ["merge-base", "--is-ancestor", ref, base]).ok) {
    return { state: "merged", oid: oid.out, reason: "ref is an ancestor of base" };
  }

  const range = `${base}..${ref}`;
  const mergeCount = count(root, ["rev-list", "--merges", "--count", range]);
  const commitCount = count(root, ["rev-list", "--no-merges", "--count", range]);
  const cherry = gitResult(root, ["cherry", base, ref]);
  if (mergeCount === 0 && commitCount !== null && commitCount > 0 && cherry.ok) {
    const marks = lines(cherry.out);
    if (marks.length === commitCount && marks.every((line) => line.startsWith("- "))) {
      return {
        state: "equivalent",
        oid: oid.out,
        reason: "every non-merge patch already exists in base",
        commitCount,
      };
    }
  }

  const unique = cherry.ok ? lines(cherry.out).filter((line) => line.startsWith("+ ")).length : null;
  return {
    state: "unique",
    oid: oid.out,
    reason: mergeCount > 0
      ? "branch contains merge commits outside base; patch equivalence is ambiguous"
      : unique === null
        ? "branch patch equivalence could not be determined"
        : `${unique} patch(es) are not present in base`,
    mergeCount,
    uniquePatchCount: unique,
  };
}

function listRemoteBranches(root, remote, baseBranch) {
  const prefix = `refs/remotes/${remote}/`;
  const refs = gitResult(root, [
    "for-each-ref",
    "--format=%(refname)%09%(objectname)",
    `refs/remotes/${remote}`,
  ]);
  if (!refs.ok) return { ok: false, branches: [], error: refs.error };
  const branches = [];
  for (const line of lines(refs.out)) {
    const [ref, oid] = line.split("\t");
    if (!ref || !ref.startsWith(prefix)) continue;
    const name = ref.slice(prefix.length);
    if (!name || name === "HEAD" || name === baseBranch) continue;
    branches.push({ name, ref, oid });
  }
  return { ok: true, branches, error: "" };
}

function listOrphanedRemoteRefs(root) {
  const configured = gitResult(root, ["remote"]);
  const refs = gitResult(root, [
    "for-each-ref",
    "--format=%(refname)%09%(objectname)",
    "refs/remotes",
  ]);
  if (!configured.ok || !refs.ok) {
    return { ok: false, refs: [], error: configured.ok ? refs.error : configured.error };
  }
  const remotes = lines(configured.out).sort((a, b) => b.length - a.length);
  const prefix = "refs/remotes/";
  const orphaned = [];
  for (const line of lines(refs.out)) {
    const [ref, oid] = line.split("\t");
    if (!ref || !ref.startsWith(prefix)) continue;
    const relative = ref.slice(prefix.length);
    const owner = remotes.find((remote) => relative.startsWith(`${remote}/`));
    if (!owner) orphaned.push({ ref, oid, relative });
  }
  return { ok: true, refs: orphaned, error: "" };
}

module.exports = { classifyRef, gitResult, lines, listOrphanedRemoteRefs, listRemoteBranches, runGit };
