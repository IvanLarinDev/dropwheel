#!/usr/bin/env node
// new-mockups.js - scaffold DESIGN evidence that matches the UI change type.
//
// Usage:
//   node hooks/new-mockups.js <feature> --kind existing-ui --baseline <repo-path>
//   node hooks/new-mockups.js <feature> --kind new-ui
//   node hooks/new-mockups.js <feature> --kind animation --fidelity text|js --example <scenario>
//   node hooks/new-mockups.js <feature> --kind backend
//
// The command creates DESIGN.json, NOTES.md, and four mode-specific variants.
// It never creates APPROVED; approval is a separate user decision.

const fs = require("fs");
const path = require("path");

const ROOT = process.env.HARNESS_ROOT || path.join(__dirname, "..");
const VALID_KINDS = new Set(["existing-ui", "new-ui", "animation", "backend"]);

const LAYOUTS = [
  { id: "01-inline", name: "Inline", focus: "Place the new or changed element in the primary content flow." },
  { id: "02-toolbar", name: "Toolbar", focus: "Place the element in the existing command area." },
  { id: "03-side-panel", name: "Side panel", focus: "Give the element persistent space beside the main content." },
  { id: "04-contextual", name: "Contextual", focus: "Reveal the element next to the object or action that invokes it." },
];

const STYLES = [
  { id: "01-minimal-light", name: "Minimal / Light", bg: "#ffffff", panel: "#f5f6f8", fg: "#1c1e21", accent: "#2d6cdf", radius: "6px", font: "system-ui, 'Segoe UI', sans-serif" },
  { id: "02-dark-pro", name: "Dark / Pro", bg: "#14161a", panel: "#1e2128", fg: "#e6e8eb", accent: "#4fd1c5", radius: "8px", font: "'Segoe UI', Roboto, sans-serif" },
  { id: "03-high-contrast-a11y", name: "High-contrast / A11y", bg: "#000000", panel: "#0a0a0a", fg: "#ffffff", accent: "#ffd400", radius: "2px", font: "'Atkinson Hyperlegible', system-ui, sans-serif" },
  { id: "04-playful-rounded", name: "Playful / Rounded", bg: "#fef6f0", panel: "#fff0e6", fg: "#3b2b2b", accent: "#ff6b6b", radius: "18px", font: "'Nunito', 'Segoe UI', sans-serif" },
];

const MOTIONS = [
  {
    id: "01-direct-ease", name: "Direct ease", duration: 160, easing: "cubic-bezier(.2,.8,.2,1)", stagger: 0,
    focus: "Fast, restrained feedback with no overshoot.",
    sequence: ["The affected element starts immediately.", "It follows the shortest path to the new state.", "The final state holds without a secondary flourish."],
    tradeoff: "Clear and inexpensive, but intentionally subtle.",
    keyframes: [{ transform: "translate(0, 0) scale(1)" }, { transform: "translate(150px, 0) scale(1)" }],
  },
  {
    id: "02-soft-settle", name: "Soft settle", duration: 240, easing: "cubic-bezier(.2,.9,.3,1)", stagger: 0,
    focus: "A visible arrival with a small overshoot and settle.",
    sequence: ["The element accelerates quickly out of its old state.", "It passes the destination by a few pixels.", "It settles into the final position and scale."],
    tradeoff: "Feels more physical, but can be distracting when repeated often.",
    keyframes: [
      { transform: "translate(0, 0) scale(1)" },
      { transform: "translate(164px, 0) scale(1.04)", offset: 0.78 },
      { transform: "translate(150px, 0) scale(1)" },
    ],
  },
  {
    id: "03-staggered-reflow", name: "Staggered reflow", duration: 210, easing: "cubic-bezier(.25,.75,.25,1)", stagger: 55,
    focus: "Affected elements move in a short sequence so the reflow is easy to follow.",
    sequence: ["The directly manipulated element moves first.", "Neighbouring elements follow with a short stagger.", "All elements finish in stable positions."],
    tradeoff: "Explains a multi-element change well, but increases total completion time.",
    keyframes: [{ transform: "translate(0, 0) scale(1)" }, { transform: "translate(150px, 0) scale(1)" }],
  },
  {
    id: "04-guided-arc", name: "Guided arc", duration: 280, easing: "cubic-bezier(.22,.7,.2,1)", stagger: 20,
    focus: "A curved path makes the source and destination relationship explicit.",
    sequence: ["The element lifts away from the source state.", "It travels on a shallow arc above neighbouring content.", "It lands at the destination with a restrained settle."],
    tradeoff: "Highly legible for spatial changes, but too expressive for dense repeated actions.",
    keyframes: [
      { transform: "translate(0, 0) scale(1)" },
      { transform: "translate(75px, -30px) scale(1.05)", offset: 0.5 },
      { transform: "translate(150px, 0) scale(1)" },
    ],
  },
];

function usage() {
  return [
    "usage:",
    "  node hooks/new-mockups.js <feature> --kind existing-ui --baseline <repo-path>",
    "  node hooks/new-mockups.js <feature> --kind new-ui",
    "  node hooks/new-mockups.js <feature> --kind animation --fidelity text|js --example <scenario>",
    "  node hooks/new-mockups.js <feature> --kind backend",
  ].join("\n");
}

function fail(message) {
  console.error(message);
  console.error(usage());
  process.exit(1);
}

function parseArgs(argv) {
  if (argv.includes("--help") || argv.includes("-h")) {
    console.log(usage());
    process.exit(0);
  }
  const rawFeature = String(argv[0] || "").trim();
  const feature = rawFeature.replace(/[^a-zA-Z0-9._-]/g, "-");
  if (!feature) fail("feature name is required.");

  const out = { feature, kind: "", fidelity: "", example: "", baselines: [] };
  for (let i = 1; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--kind") out.kind = String(argv[++i] || "").trim();
    else if (arg === "--fidelity") out.fidelity = String(argv[++i] || "").trim();
    else if (arg === "--example") out.example = String(argv[++i] || "").trim();
    else if (arg === "--baseline") out.baselines.push(String(argv[++i] || "").trim());
    else fail(`unknown argument: ${arg}`);
  }

  if (!VALID_KINDS.has(out.kind)) fail("--kind must be existing-ui, new-ui, animation, or backend.");
  if (out.kind === "existing-ui" && out.baselines.length === 0)
    fail("existing-ui mockups require at least one --baseline path from the current UI.");
  if (out.kind === "animation") {
    if (!new Set(["text", "js"]).has(out.fidelity)) fail("animation mockups require --fidelity text or js.");
    if (!out.example) fail("animation mockups require --example with a concrete user-visible scenario.");
  }
  return out;
}

function normalizeBaselines(values) {
  return values.map((value) => {
    if (!value) fail("--baseline cannot be empty.");
    const absolute = path.resolve(ROOT, value);
    const relative = path.relative(ROOT, absolute);
    if (!relative || relative.startsWith("..") || path.isAbsolute(relative))
      fail(`baseline must resolve inside the repository: ${value}`);
    if (!fs.existsSync(absolute)) fail(`baseline does not exist: ${value}`);
    return relative.replace(/\\/g, "/");
  });
}

function html(value) {
  return String(value).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

function layoutMarkup(id) {
  if (id === "01-inline") return `
    <section class="content">
      <h2>Current screen</h2><p class="muted">Keep the existing hierarchy and visual tokens.</p>
      <div class="surface"><b>Primary content</b><p>The changed element sits in the normal content flow.</p>
        <div class="control-row"><span>Changed UI element</span><button>Apply</button></div></div>
    </section>`;
  if (id === "02-toolbar") return `
    <section class="content">
      <div class="toolbar"><b>Current screen</b><span class="spacer"></span><button>Changed action</button></div>
      <div class="surface"><h2>Primary content</h2><p>The content stays quiet while the changed element uses the command area.</p></div>
    </section>`;
  if (id === "03-side-panel") return `
    <section class="content split">
      <div class="surface"><h2>Primary content</h2><p>The main work area keeps its current structure.</p></div>
      <aside><b>Changed UI element</b><p class="muted">Persistent controls and state.</p><button>Apply</button></aside>
    </section>`;
  return `
    <section class="content contextual">
      <div class="surface"><h2>Primary content</h2><p>The changed element appears only near its trigger.</p>
        <button class="anchor">Open</button><div class="popover"><b>Changed UI element</b><p>Contextual controls.</p><button>Apply</button></div>
      </div>
    </section>`;
}

function existingUiHtml(feature, layout, baselines) {
  const baselineLabel = baselines.map(html).join(", ");
  return `<!doctype html>
<meta charset="utf-8">
<title>${html(feature)} - ${html(layout.name)}</title>
<!-- Existing UI baseline: ${baselineLabel}. Match its real tokens and components before review. -->
<style>
  :root { --bg:#f3f4f6; --panel:#ffffff; --fg:#20242a; --muted:#667085; --line:#d7dbe2; --accent:#2563eb; --r:6px; }
  * { box-sizing:border-box; }
  body { margin:0; min-height:100vh; background:var(--bg); color:var(--fg); font:14px system-ui, 'Segoe UI', sans-serif; }
  header { height:52px; display:flex; align-items:center; gap:12px; padding:0 18px; background:var(--panel); border-bottom:1px solid var(--line); }
  header .meta { margin-left:auto; color:var(--muted); font-size:12px; }
  main { min-height:calc(100vh - 52px); display:grid; grid-template-columns:190px 1fr; }
  nav { padding:16px 12px; background:var(--panel); border-right:1px solid var(--line); }
  nav a { display:block; padding:8px 10px; color:inherit; text-decoration:none; border-radius:var(--r); }
  nav a.active { background:#e8eefc; color:#1747a6; }
  .content { padding:24px; min-width:0; }
  .surface, aside, .popover { padding:18px; background:var(--panel); border:1px solid var(--line); border-radius:var(--r); }
  .surface { min-height:220px; }
  h2 { margin:0 0 8px; font-size:20px; } p { line-height:1.5; } .muted { color:var(--muted); }
  button { min-height:34px; padding:7px 12px; border:1px solid #1747a6; border-radius:var(--r); background:var(--accent); color:white; cursor:pointer; }
  .control-row, .toolbar { display:flex; align-items:center; gap:12px; }
  .control-row { margin-top:24px; padding:12px; border:1px solid var(--line); border-radius:var(--r); }
  .control-row button, .spacer { margin-left:auto; }
  .toolbar { min-height:52px; margin-bottom:12px; padding:8px 12px; background:var(--panel); border:1px solid var(--line); border-radius:var(--r); }
  .split { display:grid; grid-template-columns:minmax(0, 1fr) 260px; gap:14px; } aside { min-height:220px; }
  .contextual .surface { position:relative; } .anchor { margin-top:20px; }
  .popover { position:absolute; left:140px; top:92px; width:240px; box-shadow:0 10px 28px rgba(16,24,40,.14); }
</style>
<header><b>${html(feature)}</b><span>${html(layout.name)} layout</span><span class="meta">Same visual language, different placement</span></header>
<main><nav><a class="active">Current view</a><a>Items</a><a>Settings</a></nav>${layoutMarkup(layout.id)}</main>
`;
}

function newUiHtml(feature, style) {
  return `<!doctype html>
<meta charset="utf-8">
<title>${html(feature)} - ${html(style.name)}</title>
<!-- New UI style direction: ${html(style.name)}. Replace this scaffold with the real product concept. -->
<style>
  :root { --bg:${style.bg}; --panel:${style.panel}; --fg:${style.fg}; --accent:${style.accent}; --r:${style.radius}; }
  * { box-sizing:border-box; }
  body { margin:0; height:100vh; display:flex; flex-direction:column; background:var(--bg); color:var(--fg); font-family:${style.font}; }
  header { padding:12px 16px; background:var(--panel); border-bottom:1px solid rgba(128,128,128,.25); display:flex; align-items:center; gap:12px; }
  header b { font-size:14px; } header .badge { margin-left:auto; font-size:11px; opacity:.7; }
  main { flex:1; display:flex; min-height:0; }
  nav { width:200px; background:var(--panel); padding:12px; border-right:1px solid rgba(128,128,128,.25); }
  nav a { display:block; padding:8px 10px; border-radius:var(--r); color:inherit; text-decoration:none; font-size:13px; }
  nav a.active { background:var(--accent); color:var(--bg); }
  section { flex:1; padding:20px; overflow:auto; }
  .card { background:var(--panel); border-radius:var(--r); padding:16px; margin-bottom:14px; }
  button { background:var(--accent); color:var(--bg); border:0; border-radius:var(--r); padding:9px 16px; font-size:13px; cursor:pointer; }
</style>
<header><b>${html(feature)}</b><span>${html(style.name)}</span><span class="badge">NEW UI DIRECTION</span></header>
<main><nav><a class="active">Overview</a><a>Items</a><a>Settings</a><a>About</a></nav>
  <section><div class="card"><h3 style="margin-top:0">Primary area</h3><p>Develop the new ${html(feature)} experience in the ${html(style.name)} direction.</p><button>Primary action</button></div>
  <div class="card"><h3 style="margin-top:0">Supporting area</h3><p>Use this concept to compare a genuinely different visual language.</p></div></section></main>
`;
}

function animationText(feature, motion, example, baselines) {
  const baseline = baselines.length ? baselines.map((x) => `\`${x}\``).join(", ") : "Current product state";
  return `# ${motion.name}

Feature: ${feature}
Concrete example: ${example}
Visual baseline: ${baseline}

## Sequence

${motion.sequence.map((step, i) => `${i + 1}. ${step}`).join("\n")}

## Motion spec

- Duration: ${motion.duration}ms
- Easing: \`${motion.easing}\`
- Stagger: ${motion.stagger}ms
- Intent: ${motion.focus}

## Tradeoff

${motion.tradeoff}
`;
}

function animationJsHtml(feature, motion, example, baselines) {
  const baseline = baselines.length ? baselines.join(", ") : "current product state";
  const spec = JSON.stringify({ keyframes: motion.keyframes, duration: motion.duration, easing: motion.easing, stagger: motion.stagger });
  return `<!doctype html>
<meta charset="utf-8">
<title>${html(feature)} - ${html(motion.name)}</title>
<style>
  * { box-sizing:border-box; }
  body { margin:0; min-height:100vh; display:grid; place-items:center; background:#f3f4f6; color:#20242a; font:14px system-ui, 'Segoe UI', sans-serif; }
  main { width:min(760px, calc(100vw - 32px)); }
  header { display:flex; align-items:end; gap:12px; margin-bottom:16px; }
  h1 { margin:0; font-size:22px; } .meta { margin-left:auto; color:#667085; font-size:12px; }
  .stage { height:260px; overflow:hidden; position:relative; padding:44px; background:white; border:1px solid #d7dbe2; border-radius:6px; }
  .track { display:flex; gap:14px; align-items:center; height:100%; }
  .tile { width:92px; height:92px; display:grid; place-items:center; border:1px solid #b7c2d4; border-radius:6px; background:#e8eefc; color:#1747a6; font-weight:700; box-shadow:0 4px 12px rgba(16,24,40,.08); }
  .tile:nth-child(2) { background:#eef0f3; color:#344054; } .tile:nth-child(3) { background:#f4f0e8; color:#57482f; }
  footer { display:flex; align-items:center; gap:14px; margin-top:14px; color:#667085; }
  button { min-height:36px; padding:8px 14px; border:1px solid #1747a6; border-radius:6px; background:#2563eb; color:white; cursor:pointer; }
</style>
<main>
  <header><div><h1>${html(motion.name)}</h1><div>${html(example)}</div></div><span class="meta">Baseline: ${html(baseline)}</span></header>
  <div class="stage" id="stage"><div class="track"><div class="tile">A</div><div class="tile">B</div><div class="tile">C</div></div></div>
  <footer><button id="replay" type="button">Replay</button><span>${html(motion.focus)} ${motion.duration}ms.</span></footer>
</main>
<script>
  const spec = ${spec};
  let running = [];
  function play() {
    running.forEach((animation) => animation.cancel());
    running = [...document.querySelectorAll('.tile')].map((tile, index) => tile.animate(spec.keyframes, {
      duration: spec.duration,
      easing: spec.easing,
      delay: index * spec.stagger,
    }));
  }
  document.getElementById('replay').addEventListener('click', play);
  document.getElementById('stage').addEventListener('click', play);
  setTimeout(play, 120);
</script>
`;
}

function notes(args, variants) {
  const details = [];
  if (args.baselines.length) details.push(`Baseline references: ${args.baselines.map((x) => `\`${x}\``).join(", ")}.`);
  if (args.example) details.push(`Concrete example: ${args.example}.`);
  let guidance = "Keep the four concepts stylistically distinct because this UI is being created from scratch.";
  if (args.kind === "existing-ui")
    guidance = "Match the referenced UI's real components, spacing, type, and colours. Compare layout and placement only; do not turn the variants into unrelated themes.";
  if (args.kind === "animation" && args.fidelity === "text")
    guidance = "Review the written motion sequences first. Build an interactive prototype only if timing or physical feel remains ambiguous.";
  if (args.kind === "animation" && args.fidelity === "js")
    guidance = "Each HTML file must remain an executable JavaScript motion prototype for the same concrete scenario and visual baseline.";
  return `# Mockups - ${args.feature}

DESIGN classification: \`${args.kind}\`${args.kind === "animation" ? ` / \`${args.fidelity}\`` : ""}.
${details.join("\n")}

## Review rule

${guidance}

## Variants

${variants.map((variant) => `- \`${variant.file}\` - ${variant.focus}`).join("\n")}

## Closing the gate

1. Refine every variant into credible evidence for this feature.
2. Review the alternatives and choose a direction.
3. Create an \`APPROVED\` file in this directory with a \`ui: <changed-ui-path-or-glob>\` line.
4. Implement the user-visible change only after approval.
`;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  args.baselines = normalizeBaselines(args.baselines);

  if (args.kind === "backend") {
    console.log(`SKIP mockups: ${args.feature} is backend-only with no user-visible UI impact.`);
    console.log("design-gate skips automatically only when the changed files do not match ui.globs.");
    return;
  }

  const dir = path.join(ROOT, "design", "mockups", args.feature);
  if (fs.existsSync(dir)) fail(`already exists: design/mockups/${args.feature}/; not overwriting.`);
  fs.mkdirSync(dir, { recursive: true });

  let variants;
  if (args.kind === "existing-ui") {
    variants = LAYOUTS.map((layout) => ({ file: `${layout.id}.html`, focus: layout.focus, content: existingUiHtml(args.feature, layout, args.baselines) }));
  } else if (args.kind === "new-ui") {
    variants = STYLES.map((style) => ({ file: `${style.id}.html`, focus: style.name, content: newUiHtml(args.feature, style) }));
  } else if (args.fidelity === "text") {
    variants = MOTIONS.map((motion) => ({ file: `${motion.id}.md`, focus: motion.focus, content: animationText(args.feature, motion, args.example, args.baselines) }));
  } else {
    variants = MOTIONS.map((motion) => ({ file: `${motion.id}.html`, focus: motion.focus, content: animationJsHtml(args.feature, motion, args.example, args.baselines) }));
  }

  for (const variant of variants) fs.writeFileSync(path.join(dir, variant.file), variant.content);
  const manifest = {
    schemaVersion: 1,
    feature: args.feature,
    kind: args.kind,
    ...(args.kind === "animation" ? { fidelity: args.fidelity, example: args.example } : {}),
    ...(args.baselines.length ? { baselineReferences: args.baselines } : {}),
    variants: variants.map(({ file, focus }) => ({ file, focus })),
  };
  fs.writeFileSync(path.join(dir, "DESIGN.json"), JSON.stringify(manifest, null, 2) + "\n");
  fs.writeFileSync(path.join(dir, "NOTES.md"), notes(args, manifest.variants));

  console.log(`created ${variants.length} ${args.kind}${args.kind === "animation" ? `/${args.fidelity}` : ""} variants: design/mockups/${args.feature}/`);
  variants.forEach((variant) => console.log(`   - ${variant.file}`));
  console.log(`Next: refine variants, get approval, then create design/mockups/${args.feature}/APPROVED with a ui: scope`);
}

main();
