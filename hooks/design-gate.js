#!/usr/bin/env node
// design-gate.js - DESIGN-stage gate.
//
// Policy: user-visible GUI work must be preceded by design review. If a branch's
// changes touch UI paths, the SAME branch diff must also touch an APPROVED set of
// mode-appropriate DESIGN evidence. Legacy sets remain valid for compatibility.
//
// Usage:
//   node hooks/design-gate.js [--base <ref>] [--root <dir>] [--files a,b,c] [--json] [--strict]
//     --base   git ref to diff against (default: origin/main if available) [CI/local]
//     --files  explicit comma-separated changed files          [tests/CI]
//     --root   repo root to resolve config + mockups (default: cwd)
//     --strict diff errors fail closed (CI/server enforcement)
//
// Exit 0 = gate satisfied (or no UI change), exit 1 = UI changed without approved mockups.
// Internal error -> exit 0 locally (never wedge unrelated work), with a loud warning.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

// ---------- args ----------
function parseArgs(argv) {
  const a = { base: null, root: process.cwd(), files: null, json: false, strict: false };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--base") a.base = argv[++i];
    else if (argv[i] === "--root") a.root = argv[++i];
    else if (argv[i] === "--files") a.files = (argv[++i] || "").split(",").map((s) => s.trim()).filter(Boolean);
    else if (argv[i] === "--json") a.json = true;
    else if (argv[i] === "--strict") a.strict = true;
  }
  return a;
}

function refExists(root, ref) {
  try {
    execFileSync("git", ["rev-parse", "--verify", "--quiet", ref], {
      cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"],
      timeout: 5000, killSignal: "SIGKILL",
    });
    return true;
  } catch {
    return false;
  }
}
function defaultBase(root) {
  for (const ref of ["origin/main", "origin/HEAD", "main", "master"]) {
    if (refExists(root, ref)) return ref;
  }
  return "origin/main";
}

// ---------- config shared with guard.js and verify.js ----------
const { globToRe, loadConfig, changedFiles, normRel } = require(path.join(__dirname, "_lib.js"));

// ---------- DESIGN evidence scan ----------
function safeVariantFile(file) {
  return typeof file === "string" && file.length > 0 && !/[\\/]/.test(file) &&
    path.basename(file) === file && file !== "." && file !== "..";
}

function baselineCheck(root, references) {
  if (!Array.isArray(references) || references.length === 0)
    return { ok: false, reason: "existing-ui evidence requires baselineReferences from the current UI" };
  for (const reference of references) {
    if (typeof reference !== "string" || !reference.trim())
      return { ok: false, reason: "baselineReferences must contain repo-relative file paths" };
    const absolute = path.resolve(root, reference);
    const relative = path.relative(root, absolute);
    if (!relative || relative.startsWith("..") || path.isAbsolute(relative))
      return { ok: false, reason: `baseline reference is outside the repository: ${reference}` };
    try {
      if (!fs.statSync(absolute).isFile())
        return { ok: false, reason: `baseline reference is not a file: ${reference}` };
    } catch {
      return { ok: false, reason: `baseline reference does not exist: ${reference}` };
    }
  }
  return { ok: true };
}

function normalizeScopePattern(root, value) {
  const rel = normRel(value, root).replace(/\\/g, "/");
  if (!rel || rel === "." || rel === ".." || rel.startsWith("../") || /^(?:[A-Za-z]:)?\//.test(rel)) return "";
  return rel;
}

function scopeValues(value) {
  return String(value || "").split(/[\n,]/).map((s) => s.trim()).filter(Boolean);
}

function approvalScopePatterns(root, dir, files, m) {
  if (!files.includes(m.approvalFile)) return [];
  let text = "";
  try { text = fs.readFileSync(path.join(dir, m.approvalFile), "utf8"); } catch { return []; }
  const patterns = [];
  for (const line of text.split(/\r?\n/)) {
    const scoped = line.match(/^\s*(?:ui|ui-paths?|scope)\s*:\s*(.+)$/i);
    if (scoped) patterns.push(...scopeValues(scoped[1]));
  }
  return patterns.map((p) => normalizeScopePattern(root, p)).filter(Boolean);
}

function manifestScopePatterns(root, manifest) {
  const patterns = [];
  if (manifest && manifest.scope && Array.isArray(manifest.scope.uiPaths))
    patterns.push(...manifest.scope.uiPaths);
  if (manifest && Array.isArray(manifest.uiPaths))
    patterns.push(...manifest.uiPaths);
  if (manifest && manifest.kind === "existing-ui" && Array.isArray(manifest.baselineReferences))
    patterns.push(...manifest.baselineReferences);
  return patterns.map((p) => normalizeScopePattern(root, p)).filter(Boolean);
}

function scopeMatches(file, pattern) {
  const p = String(pattern || "").replace(/\\/g, "/");
  if (!p) return false;
  if (p.endsWith("/")) return file.startsWith(p);
  if (/[*?[\]{}]/.test(p)) return globToRe(p).test(file);
  return file === p || file.startsWith(p + "/");
}

function scopeCoversUi(root, uiChanged, patterns) {
  const normalized = [...new Set((patterns || []).map((p) => normalizeScopePattern(root, p)).filter(Boolean))];
  if (!normalized.length)
    return { ok: false, reason: "approval scope is missing; add `ui: <changed-ui-path-or-glob>` to APPROVED or use WAIVER.json" };
  const uncovered = uiChanged.filter((file) => !normalized.some((pattern) => scopeMatches(file, pattern)));
  return uncovered.length
    ? { ok: false, reason: `approval scope does not cover UI file(s): ${uncovered.slice(0, 6).join(", ")}` }
    : { ok: true, patterns: normalized };
}

function validateManifestSet(root, dir, files, m) {
  const manifestFile = m.manifestFile || "DESIGN.json";
  const approvalScopes = approvalScopePatterns(root, dir, files, m);
  if (!files.includes(manifestFile)) {
    const mockups = files.filter((file) => m.mockupExtensions.includes(path.extname(file).toLowerCase()));
    return mockups.length >= m.min
      ? { ok: true, count: mockups.length, kind: "legacy", legacy: true, scopePatterns: approvalScopes }
      : { ok: false, reason: `legacy set has ${mockups.length}/${m.min} visual mockups` };
  }

  let manifest;
  try { manifest = JSON.parse(fs.readFileSync(path.join(dir, manifestFile), "utf8")); }
  catch { return { ok: false, reason: `${manifestFile} is not valid JSON` }; }
  if (manifest.schemaVersion !== 1)
    return { ok: false, reason: `${manifestFile} schemaVersion must be 1` };
  if (!new Set(["existing-ui", "new-ui", "animation"]).has(manifest.kind))
    return { ok: false, reason: `${manifestFile} kind must be existing-ui, new-ui, or animation` };
  if (!Array.isArray(manifest.variants) || manifest.variants.length < m.min)
    return { ok: false, reason: `${manifestFile} declares fewer than ${m.min} variants` };

  const variantFiles = manifest.variants.map((variant) => variant && variant.file);
  if (variantFiles.some((file) => !safeVariantFile(file)))
    return { ok: false, reason: `${manifestFile} variants must be direct child files` };
  if (new Set(variantFiles).size !== variantFiles.length)
    return { ok: false, reason: `${manifestFile} variants must be unique` };
  const missing = variantFiles.filter((file) => !files.includes(file));
  if (missing.length)
    return { ok: false, reason: `${manifestFile} references missing variant(s): ${missing.join(", ")}` };

  if (manifest.kind === "existing-ui") {
    const baseline = baselineCheck(root, manifest.baselineReferences);
    if (!baseline.ok) return baseline;
  }

  if (manifest.kind === "animation") {
    if (!new Set(["text", "js"]).has(manifest.fidelity))
      return { ok: false, reason: "animation evidence requires fidelity text or js" };
    if (typeof manifest.example !== "string" || !manifest.example.trim())
      return { ok: false, reason: "animation evidence requires a concrete example" };
    const expectedExtension = manifest.fidelity === "text" ? ".md" : ".html";
    const wrongType = variantFiles.find((file) => path.extname(file).toLowerCase() !== expectedExtension);
    if (wrongType)
      return { ok: false, reason: `animation/${manifest.fidelity} variant has the wrong file type: ${wrongType}` };
    if (manifest.fidelity === "js") {
      for (const file of variantFiles) {
        let source = "";
        try { source = fs.readFileSync(path.join(dir, file), "utf8"); } catch {}
        if (!/<script(?:\s|>)/i.test(source) || !/(?:\.animate\s*\(|requestAnimationFrame\s*\()/i.test(source))
          return { ok: false, reason: `animation/js variant is not an executable motion prototype: ${file}` };
      }
    }
  } else {
    const wrongType = variantFiles.find((file) => !m.mockupExtensions.includes(path.extname(file).toLowerCase()));
    if (wrongType)
      return { ok: false, reason: `${manifest.kind} variant has an unsupported visual file type: ${wrongType}` };
  }

  return {
    ok: true,
    count: variantFiles.length,
    kind: manifest.kind,
    scopePatterns: [...manifestScopePatterns(root, manifest), ...approvalScopes],
    ...(manifest.fidelity ? { fidelity: manifest.fidelity } : {}),
  };
}

function validateWaiverSet(root, dir, files, m, uiChanged) {
  const waiverFile = m.waiverFile || "WAIVER.json";
  if (!files.includes(waiverFile)) return null;
  let waiver;
  try { waiver = JSON.parse(fs.readFileSync(path.join(dir, waiverFile), "utf8")); }
  catch { return { ok: false, reason: `${waiverFile} is not valid JSON` }; }
  if (waiver.schemaVersion !== 1)
    return { ok: false, reason: `${waiverFile} schemaVersion must be 1` };
  if (!Array.isArray(waiver.uiPaths) || waiver.uiPaths.length === 0)
    return { ok: false, reason: `${waiverFile} requires uiPaths` };
  if (typeof waiver.reason !== "string" || !waiver.reason.trim())
    return { ok: false, reason: `${waiverFile} requires a reason` };
  if (typeof (waiver.approvedBy || waiver.approvalSource) !== "string" || !(waiver.approvedBy || waiver.approvalSource).trim())
    return { ok: false, reason: `${waiverFile} requires approvedBy or approvalSource` };
  if (typeof waiver.date !== "string" || !/^\d{4}-\d{2}-\d{2}/.test(waiver.date))
    return { ok: false, reason: `${waiverFile} requires an ISO date` };
  const scoped = scopeCoversUi(root, uiChanged, waiver.uiPaths);
  return scoped.ok ? { ok: true, count: 0, kind: "waiver", waiver: true, scopePatterns: scoped.patterns } : scoped;
}

// An approved set counts only if that same set is touched in this branch diff.
// Otherwise one old approval would unlock future UI work forever.
function hasApprovedMockups(root, m, changed, uiChanged) {
  const base = path.join(root, m.dir);
  const mockRoot = m.dir.replace(/\\/g, "/").replace(/\/$/, "");
  const waiverFile = m.waiverFile || "WAIVER.json";
  let dirs;
  try { dirs = fs.readdirSync(base, { withFileTypes: true }).filter((d) => d.isDirectory()); }
  catch { return { ok: false, reason: `missing directory ${m.dir}/` }; }

  const stale = [];
  const invalid = [];
  for (const d of dirs) {
    const dir = path.join(base, d.name);
    let files;
    try { files = fs.readdirSync(dir); } catch { continue; }
    const approved = files.includes(m.approvalFile);
    const touched = changed.some((c) => c.startsWith(`${mockRoot}/${d.name}/`));
    const waiverTouched = changed.includes(`${mockRoot}/${d.name}/${waiverFile}`);
    if (waiverTouched) {
      const waiver = validateWaiverSet(root, dir, files, m, uiChanged);
      if (waiver && waiver.ok) return { ok: true, feature: d.name, ...waiver };
      if (waiver) invalid.push(`${d.name}: ${waiver.reason}`);
    }
    if (!approved) continue;
    const check = validateManifestSet(root, dir, files, m);
    if (!check.ok) {
      if (touched) invalid.push(`${d.name}: ${check.reason}`);
      continue;
    }
    if (touched) {
      const scoped = scopeCoversUi(root, uiChanged, check.scopePatterns);
      if (scoped.ok) return { ok: true, feature: d.name, ...check, scopePatterns: scoped.patterns };
      invalid.push(`${d.name}: ${scoped.reason}`);
      continue;
    }
    stale.push(d.name);
  }
  return {
    ok: false,
    reason: invalid.length
      ? `invalid DESIGN evidence (${invalid.join("; ")})`
      : stale.length
      ? `approved set(s) (${stale.join(", ")}) are valid but not scoped/touched for this branch diff`
      : `no valid scoped DESIGN approval or WAIVER.json touched in this branch`,
  };
}

// ---------- main ----------
(function main() {
  const a = parseArgs(process.argv.slice(2));
  if (!a.base) a.base = defaultBase(a.root);
  const cfg = loadConfig(a.root);
  const res = { ok: true, base: a.base, uiChanged: [], mockups: null };

  const uiRes = cfg.uiGlobs.map(globToRe);
  const mockRoot = cfg.mockups.dir.replace(/\\/g, "/").replace(/\/$/, "");
  const cf = changedFiles(a.base, a.root, a.files);
  if (cf.base) res.base = cf.base;
  if (cf.error) {
    // Local default = fail-open loudly; strict/CI = fail-closed.
    const warn = `design-gate: ${cf.error}; ${a.strict ? "gate cannot verify UI changes" : "gate skipped and UI changes were not checked"}. Pass --base <ref>.`;
    if (a.json) console.log(JSON.stringify({ ...res, ok: !a.strict, skipped: true, warn }));
    else console.error(warn);
    process.exit(a.strict ? 1 : 0);
  }
  const files = cf.files.map((f) => f.replace(/\\/g, "/"));

  res.uiChanged = files.filter(
    (f) => !f.startsWith(mockRoot + "/") && uiRes.some((re) => re.test(f))
  );

  if (res.uiChanged.length === 0) {
    if (a.json) console.log(JSON.stringify({ ...res, note: "no UI-path changes" }));
    else console.log("OK design-gate: no UI-path changes; gate not required.");
    process.exit(0);
  }

  const mk = hasApprovedMockups(a.root, cfg.mockups, files, res.uiChanged);
  res.mockups = mk;
  if (mk.ok) {
    if (a.json) console.log(JSON.stringify(res));
    else {
      const mode = mk.kind ? `, ${mk.kind}${mk.fidelity ? `/${mk.fidelity}` : ""}` : "";
      console.log(`OK design-gate: UI changes have scoped DESIGN approval touched in this branch (${mk.feature}, ${mk.count}${mode}).`);
    }
    process.exit(0);
  }

  if (a.json) { console.log(JSON.stringify({ ...res, ok: false })); process.exit(1); }
  console.error(
    `BLOCK design-gate: GUI changes require DESIGN approval.\n` +
      `   UI files: ${res.uiChanged.slice(0, 8).join(", ")}${res.uiChanged.length > 8 ? " ..." : ""}\n` +
      `   Required: ${mk.reason}.\n` +
      `   Existing UI: node hooks/new-mockups.js <feature> --kind existing-ui --baseline <repo-path>.\n` +
      `   Motion: use --kind animation --fidelity text|js --example <scenario>; new UI: use --kind new-ui.\n` +
      `   Existing set: touch its ${cfg.mockups.approvalFile} and include a ui: scope covering the changed UI path(s).\n` +
      `   Waiver: create ${cfg.mockups.dir}/<feature>/${cfg.mockups.waiverFile || "WAIVER.json"} with uiPaths, reason, date, and approvedBy.\n` +
      `   Policy: DESIGN evidence must match the UI change type; backend-only diffs outside ui.globs need none.`
  );
  process.exit(1);
})();
