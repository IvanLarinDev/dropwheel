#!/usr/bin/env node
// uninstall.js - safely remove the harness-managed runtime from a target.
//
// By default only files whose current SHA-256 matches .harness/installation.json
// are removed. Project-owned policy, workflows, changelog, and product files are
// always preserved. Use --remove-modified only after reviewing managed conflicts.
//
// Usage: node hooks/uninstall.js [--target <dir>] [--dry-run]
//        [--remove-modified] [--keep-git-hooks] [--json]


const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const { spawnSync } = require("child_process");

const MANIFEST_REL = ".harness/installation.json";
const HARNESS_COMMANDS = new Set([
  "node hooks/agent/guard.js",
  "node hooks/agent/stop-reminder.js",
]);

function parseArgs(argv) {
  const a = { target: process.cwd(), dryRun: false, removeModified: false, keepGitHooks: false, json: false, errors: [] };
  let explicitTarget = "";
  let positionalTarget = "";
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--target") {
      if (!argv[i + 1] || argv[i + 1].startsWith("--")) a.errors.push("--target requires a directory");
      else if (explicitTarget) { a.errors.push("--target may only be provided once"); i++; }
      else explicitTarget = argv[++i];
    } else if (arg === "--dry-run") a.dryRun = true;
    else if (arg === "--remove-modified") a.removeModified = true;
    else if (arg === "--keep-git-hooks") a.keepGitHooks = true;
    else if (arg === "--json") a.json = true;
    else if (arg.startsWith("-")) a.errors.push(`unknown option: ${arg}`);
    else if (positionalTarget) a.errors.push(`unexpected positional argument: ${arg}`);
    else positionalTarget = arg;
  }
  if (positionalTarget && explicitTarget)
    a.errors.push("target must be provided either positionally or with --target, not both");
  a.target = path.resolve(explicitTarget || positionalTarget || a.target);
  return a;
}

function sha256File(file) {
  try { return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex"); }
  catch { return ""; }
}

function safeManagedRel(rel) {
  const normalized = String(rel || "").replace(/\\/g, "/");
  if (!normalized || path.posix.isAbsolute(normalized) || path.posix.normalize(normalized) !== normalized || normalized.includes("../")) return false;
  return normalized.startsWith("hooks/") || normalized === "lefthook.yml" || normalized === "settings.example.json";
}

function readManifest(target) {
  try {
    const value = JSON.parse(fs.readFileSync(path.join(target, MANIFEST_REL), "utf8"));
    if (!value || value.schemaVersion !== 1 || !value.managed || typeof value.managed !== "object" || Array.isArray(value.managed))
      return { error: "installation manifest has an unsupported shape" };
    return { value };
  } catch (e) {
    return { error: e.code === "ENOENT" ? "installation manifest is missing; nothing can be removed safely" : `installation manifest is unreadable: ${e.message}` };
  }
}

function normalizedCommand(command) {
  return String(command || "").replace(/\\/g, "/").replace(/\s+/g, " ").trim().toLowerCase();
}

function cleanSettings(target, dryRun) {
  const file = path.join(target, ".claude", "settings.json");
  let value;
  try { value = JSON.parse(fs.readFileSync(file, "utf8")); }
  catch (e) {
    if (e.code === "ENOENT") return { status: "missing", removed: 0 };
    return { status: "error", removed: 0, reason: "existing .claude/settings.json is invalid; harness hook entries were preserved" };
  }
  if (!value.hooks || typeof value.hooks !== "object") return { status: "already", removed: 0 };
  let removed = 0;
  for (const event of Object.keys(value.hooks)) {
    if (!Array.isArray(value.hooks[event])) continue;
    const entries = [];
    for (const entry of value.hooks[event]) {
      if (!entry || !Array.isArray(entry.hooks)) { entries.push(entry); continue; }
      const hooks = entry.hooks.filter((hook) => {
        const match = HARNESS_COMMANDS.has(normalizedCommand(hook && hook.command));
        if (match) removed++;
        return !match;
      });
      if (hooks.length) entries.push({ ...entry, hooks });
    }
    if (entries.length) value.hooks[event] = entries;
    else delete value.hooks[event];
  }
  if (Object.keys(value.hooks).length === 0) delete value.hooks;
  if (!dryRun && removed) fs.writeFileSync(file, JSON.stringify(value, null, 2) + "\n");
  return { status: removed ? (dryRun ? "plan" : "cleaned") : "already", removed };
}

function cleanGitignore(target, dryRun) {
  const file = path.join(target, ".gitignore");
  let text;
  try { text = fs.readFileSync(file, "utf8"); }
  catch (e) { return { status: e.code === "ENOENT" ? "missing" : "error", removed: 0, ...(e.code === "ENOENT" ? {} : { reason: e.message }) }; }
  const lines = text.split(/\r?\n/);
  let removed = 0;
  const out = [];
  for (let i = 0; i < lines.length; i++) {
    if (lines[i] === "# agent runtime (local runner settings; do not commit)" && lines[i + 1] === ".claude/settings.local.json") {
      removed += 2;
      i++;
      if (out.length && out[out.length - 1] === "") out.pop();
      continue;
    }
    out.push(lines[i]);
  }
  if (!dryRun && removed) {
    let next = out.join("\n").replace(/\n{3,}/g, "\n\n");
    if (next && !next.endsWith("\n")) next += "\n";
    fs.writeFileSync(file, next);
  }
  return { status: removed ? (dryRun ? "plan" : "cleaned") : "already", removed };
}

function uninstallLefthook(target, dryRun, keep) {
  if (keep) return { status: "kept" };
  if (dryRun) return { status: "plan" };
  const inside = spawnSync("git", ["rev-parse", "--is-inside-work-tree"], { cwd: target, encoding: "utf8" });
  if (inside.status !== 0) return { status: "skipped", reason: "target is not a git repository" };
  const run = spawnSync("lefthook", ["uninstall"], { cwd: target, encoding: "utf8", shell: true });
  return run.status === 0
    ? { status: "removed" }
    : { status: "pending", reason: String(run.stderr || run.stdout || run.error && run.error.message || `exit ${run.status}`).trim() };
}

function removeManaged(target, rel, expectedHash, removeModified, dryRun) {
  if (!safeManagedRel(rel) || !/^[0-9a-f]{64}$/i.test(String(expectedHash || "")))
    return { rel, action: "conflict", reason: "unsafe path or invalid manifest hash" };
  const abs = path.resolve(target, ...rel.replace(/\\/g, "/").split("/"));
  const root = path.resolve(target) + path.sep;
  if (!abs.toLowerCase().startsWith(root.toLowerCase()))
    return { rel, action: "conflict", reason: "managed path escapes target" };
  let stat;
  try { stat = fs.lstatSync(abs); }
  catch (e) { return e.code === "ENOENT" ? { rel, action: "missing" } : { rel, action: "conflict", reason: e.message }; }
  if (stat.isDirectory()) return { rel, action: "conflict", reason: "managed entry is a directory; recursive removal is refused" };
  const actualHash = sha256File(abs);
  if (actualHash !== expectedHash && !removeModified)
    return { rel, action: "conflict", reason: "locally modified since install; review and rerun with --remove-modified" };
  if (!dryRun) fs.rmSync(abs, { force: true });
  return { rel, action: dryRun ? "plan" : "remove", modified: actualHash !== expectedHash };
}

function pruneEmptyManagedDirs(target, removedFiles) {
  const candidates = new Set();
  for (const rel of removedFiles) {
    if (!rel.startsWith("hooks/")) continue;
    let dir = path.dirname(path.join(target, ...rel.split("/")));
    const stop = path.join(target, "hooks");
    while (dir.toLowerCase().startsWith(stop.toLowerCase())) {
      candidates.add(dir);
      if (dir.toLowerCase() === stop.toLowerCase()) break;
      dir = path.dirname(dir);
    }
  }
  for (const dir of [...candidates].sort((a, b) => b.length - a.length)) {
    try { fs.rmdirSync(dir); } catch {}
  }
  try { fs.rmdirSync(path.join(target, ".harness")); } catch {}
}

const a = parseArgs(process.argv.slice(2));

(function main() {
  const out = {
    ok: true, target: a.target, dryRun: a.dryRun, removeModified: a.removeModified,
    files: [], settings: null, gitignore: null, lefthook: null, manifest: null,
    projectOwnedPreserved: [], notes: [], argumentErrors: a.errors,
  };
  if (a.errors.length) return finish(out, false, `invalid arguments: ${a.errors.join("; ")}`);
  try { if (!fs.statSync(a.target).isDirectory()) throw new Error(); }
  catch { return finish(out, false, `target directory does not exist: ${a.target}`); }

  const read = readManifest(a.target);
  if (read.error) return finish(out, false, read.error);
  const manifest = read.value;
  out.projectOwnedPreserved = Array.isArray(manifest.ownership && manifest.ownership.projectOwned)
    ? manifest.ownership.projectOwned.slice() : [];

  out.lefthook = uninstallLefthook(a.target, a.dryRun, a.keepGitHooks);
  if (out.lefthook.status === "pending") out.notes.push(`lefthook cleanup pending: ${out.lefthook.reason}`);
  out.settings = cleanSettings(a.target, a.dryRun);
  out.gitignore = cleanGitignore(a.target, a.dryRun);

  const selfRel = path.relative(a.target, __filename).replace(/\\/g, "/");
  const entries = Object.entries(manifest.managed);
  for (const [rel, hash] of entries) {
    if (rel.replace(/\\/g, "/") === selfRel) continue;
    out.files.push(removeManaged(a.target, rel, hash, a.removeModified, a.dryRun));
  }

  let conflicts = out.files.filter((file) => file.action === "conflict");
  if (out.settings.status === "error") conflicts.push({ rel: ".claude/settings.json", reason: out.settings.reason });
  if (out.lefthook.status === "pending") conflicts.push({ rel: ".git/hooks", reason: out.lefthook.reason });
  if (!conflicts.length && Object.prototype.hasOwnProperty.call(manifest.managed, selfRel)) {
    out.files.push(removeManaged(a.target, selfRel, manifest.managed[selfRel], a.removeModified, a.dryRun));
    conflicts = out.files.filter((file) => file.action === "conflict");
  } else if (conflicts.length && Object.prototype.hasOwnProperty.call(manifest.managed, selfRel)) {
    out.files.push({ rel: selfRel, action: "preserve", reason: "kept so partial uninstall can be resumed" });
  }

  if (!conflicts.length) {
    out.manifest = { rel: MANIFEST_REL, action: a.dryRun ? "plan" : "remove" };
    if (!a.dryRun) fs.rmSync(path.join(a.target, MANIFEST_REL), { force: true });
  } else {
    out.manifest = { rel: MANIFEST_REL, action: "preserve", reason: "managed conflicts remain" };
  }
  if (!a.dryRun) pruneEmptyManagedDirs(a.target, out.files.filter((file) => file.action === "remove").map((file) => file.rel));

  if (out.projectOwnedPreserved.length)
    out.notes.push(`project-owned files were preserved: ${out.projectOwnedPreserved.join(", ")}`);
  if (conflicts.length)
    out.notes.push(`${conflicts.length} conflict(s) remain; review them and rerun with --remove-modified only if deletion is intended.`);
  return finish(out, conflicts.length === 0 && out.settings.status !== "error",
    conflicts.length ? "uninstall incomplete: managed conflicts remain" : null);
})();

function finish(out, ok, reason) {
  out.ok = ok;
  if (reason) out.reason = reason;
  if (a.json) { console.log(JSON.stringify(out)); process.exit(ok ? 0 : 1); }
  console.log(`\nllm-dev-harness uninstall -> ${out.target}${out.dryRun ? " [dry-run]" : ""}`);
  for (const file of out.files) console.log(`  ${file.action.padEnd(8)} ${file.rel}${file.reason ? ` - ${file.reason}` : ""}`);
  if (out.settings) console.log(`  settings ${out.settings.status} (${out.settings.removed || 0} harness hook(s))`);
  if (out.lefthook) console.log(`  lefthook ${out.lefthook.status}${out.lefthook.reason ? ` - ${out.lefthook.reason}` : ""}`);
  for (const note of out.notes) console.log(`  note: ${note}`);
  if (reason) console.error(`\n${reason}`);
  else console.log("\nuninstall complete; project-owned policy was preserved.");
  process.exit(ok ? 0 : 1);
}
