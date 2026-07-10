// release-config.js - shared project-owned release contract loading.

const fs = require("fs");
const path = require("path");
const { globToRe, normRel } = require("./_lib.js");

function loadReleaseConfig(root) {
  let config = {};
  try { config = JSON.parse(fs.readFileSync(path.join(root, "harness.config.json"), "utf8")); } catch {}
  const release = config.release || {};
  const versioning = release.versioning || {};
  const explicit = Array.isArray(versioning.manifests);
  const invalid = [];
  const manifests = [];
  for (const value of explicit ? versioning.manifests : []) {
    const rel = normRel(value, root);
    const abs = path.resolve(root, rel);
    const inside = abs === path.resolve(root) || abs.startsWith(path.resolve(root) + path.sep);
    if (!rel || rel === "." || rel.startsWith("../") || path.isAbsolute(rel) || !inside || /[*?[\]{}]/.test(rel)) {
      invalid.push(String(value));
    } else if (!manifests.includes(rel)) {
      manifests.push(rel);
    }
  }
  const exclude = Array.isArray(versioning.exclude) ? versioning.exclude.map(String) : [];
  return {
    release,
    provider: String(release.provider || (config.capabilities && config.capabilities.release) || "cocogitto"),
    versioning: {
      explicit,
      manifests,
      invalid,
      exclude,
      excludeMatchers: exclude.map(globToRe),
      allowMissing: versioning.allowMissing === true,
    },
    artifacts: Array.isArray(release.artifacts) ? release.artifacts : [],
    changelog: release.changelog !== false,
  };
}

function includesAutoManifest(rel, versioning) {
  return !(versioning.excludeMatchers || []).some((re) => re.test(rel));
}

module.exports = { loadReleaseConfig, includesAutoManifest };
