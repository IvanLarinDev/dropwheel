const crypto = require("crypto");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { execFileSync } = require("child_process");

function run(root, args) {
  return execFileSync("git", args, {
    cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
    timeout: 10000, killSignal: "SIGKILL",
  });
}

function stateRootKey(root) {
  let resolved = path.resolve(root);
  try { resolved = fs.realpathSync.native(resolved); } catch {}
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function stateFile(root) {
  const key = stateRootKey(root);
  const id = crypto.createHash("sha256").update(key).digest("hex").slice(0, 16);
  return path.join(os.tmpdir(), `harness-task-baseline-${id}.json`);
}

function eventFile(root) {
  const key = stateRootKey(root);
  const id = crypto.createHash("sha256").update(key).digest("hex").slice(0, 16);
  return path.join(os.tmpdir(), `harness-task-events-${id}.jsonl`);
}

function dirtyPaths(status) {
  const records = String(status).split("\0");
  const paths = [];
  for (let i = 0; i < records.length; i++) {
    const record = records[i];
    if (!record) continue;
    const code = record.slice(0, 2);
    paths.push(record.slice(3));
    // In porcelain v1 -z, rename/copy entries carry the original path as the
    // next NUL-delimited record. The first path is the current one to hash.
    if (/[RC]/.test(code) && i + 1 < records.length) i++;
  }
  return paths;
}

function capture(root) {
  const status = run(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"]);
  const working = run(root, ["diff", "--binary", "--no-ext-diff"]);
  const staged = run(root, ["diff", "--cached", "--binary", "--no-ext-diff"]);
  const files = [];
  for (const rel of dirtyPaths(status)) {
    const abs = path.resolve(root, rel);
    let hash = "missing";
    try {
      const stat = fs.statSync(abs);
      hash = stat.isFile() ? crypto.createHash("sha256").update(fs.readFileSync(abs)).digest("hex") : "directory";
    } catch {}
    files.push({ rel, hash });
  }
  const fingerprint = crypto.createHash("sha256")
    .update(JSON.stringify({ status, working, staged, files }))
    .digest("hex");
  return { root: path.resolve(root), status, fingerprint, capturedAt: new Date().toISOString() };
}

function gitOid(root, ref) {
  try { return run(root, ["rev-parse", "--verify", `${ref}^{commit}`]).trim(); }
  catch { return ""; }
}

function captureReceipt(root, base) {
  const worktree = capture(root);
  return {
    schemaVersion: 2,
    headOid: gitOid(root, "HEAD"),
    baseRef: String(base || "HEAD"),
    baseOid: gitOid(root, base || "HEAD"),
    fingerprint: worktree.fingerprint,
    capturedAt: worktree.capturedAt,
  };
}

function receiptFreshness(root, event, current) {
  const receipt = event && event.receipt;
  if (!receipt || receipt.schemaVersion !== 2) {
    const staleReasons = ["verification receipt is missing or unsupported"];
    return { freshness: "stale", stale: true, staleReasons, reasons: staleReasons };
  }
  let now = current;
  try { now = now || captureReceipt(root, receipt.baseRef); }
  catch {
    const staleReasons = ["current repository state could not be captured"];
    return { freshness: "stale", stale: true, staleReasons, reasons: staleReasons };
  }
  const staleReasons = [];
  if (receipt.headOid !== now.headOid) staleReasons.push("HEAD changed");
  if (receipt.baseRef !== now.baseRef || receipt.baseOid !== now.baseOid) staleReasons.push(`${receipt.baseRef || "base"} changed`);
  if (receipt.fingerprint !== now.fingerprint) staleReasons.push("working tree changed");
  return { freshness: staleReasons.length ? "stale" : "fresh", stale: staleReasons.length > 0, staleReasons, reasons: staleReasons };
}

function saveBaseline(root) {
  const value = capture(root);
  const file = stateFile(root);
  const temp = file + "." + process.pid + ".tmp";
  fs.writeFileSync(temp, JSON.stringify(value));
  fs.renameSync(temp, file);
  return value;
}

function loadBaseline(root) {
  try { return JSON.parse(fs.readFileSync(stateFile(root), "utf8")); }
  catch { return null; }
}

function clearBaseline(root) {
  try { fs.rmSync(stateFile(root), { force: true }); } catch {}
}

function recordEvent(root, event) {
  const value = { ts: new Date().toISOString(), root: path.resolve(root), ...event };
  fs.appendFileSync(eventFile(root), JSON.stringify(value) + "\n", "utf8");
  return value;
}

function loadEvents(root) {
  try {
    return fs.readFileSync(eventFile(root), "utf8")
      .split(/\r?\n/)
      .filter(Boolean)
      .flatMap((line) => {
        try {
          const event = JSON.parse(line);
          return event && typeof event === "object" ? [event] : [];
        } catch { return []; }
      });
  } catch {
    return [];
  }
}

function lastEvent(root, kind) {
  const events = loadEvents(root).filter((event) => !kind || event.kind === kind);
  return events.length ? events[events.length - 1] : null;
}

function unchangedFromBaseline(root) {
  const baseline = loadBaseline(root);
  if (!baseline) return false;
  try { return capture(root).fingerprint === baseline.fingerprint; }
  catch { return false; }
}

function remainingBaselineDirtyPaths(root) {
  const baseline = loadBaseline(root);
  if (!baseline) return [];
  const baselinePaths = new Set(dirtyPaths(baseline.status));
  if (!baselinePaths.size) return [];
  try {
    return dirtyPaths(capture(root).status).filter((rel) => baselinePaths.has(rel));
  } catch {
    return [...baselinePaths];
  }
}

module.exports = {
  capture,
  captureReceipt,
  receiptFreshness,
  saveBaseline,
  loadBaseline,
  clearBaseline,
  recordEvent,
  loadEvents,
  lastEvent,
  unchangedFromBaseline,
  remainingBaselineDirtyPaths,
  stateFile,
  eventFile,
  dirtyPaths,
};
