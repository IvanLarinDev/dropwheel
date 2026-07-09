#!/usr/bin/env node
// new-mockups.js - scaffold N stylistically-distinct single-file HTML mockups for a
// GUI feature, satisfying the DESIGN stage (BACKLOG P1-5).
//
// Usage:  node hooks/new-mockups.js <feature-name>
// Creates design/mockups/<feature>/ with 4 openable HTML mockups (4 different style
// directions) + NOTES.md. Does NOT create APPROVED; you add that after picking a
// direction, which is what the gate checks.

const fs = require("fs");
const path = require("path");

const ROOT = process.env.HARNESS_ROOT || path.join(__dirname, "..");

const STYLES = [
  { id: "01-minimal-light", name: "Minimal / Light", bg: "#ffffff", panel: "#f5f6f8", fg: "#1c1e21", accent: "#2d6cdf", radius: "6px", font: "system-ui, 'Segoe UI', sans-serif" },
  { id: "02-dark-pro", name: "Dark / Pro", bg: "#14161a", panel: "#1e2128", fg: "#e6e8eb", accent: "#4fd1c5", radius: "8px", font: "'Segoe UI', Roboto, sans-serif" },
  { id: "03-high-contrast-a11y", name: "High-contrast / A11y", bg: "#000000", panel: "#0a0a0a", fg: "#ffffff", accent: "#ffd400", radius: "2px", font: "'Atkinson Hyperlegible', system-ui, sans-serif" },
  { id: "04-playful-rounded", name: "Playful / Rounded", bg: "#fef6f0", panel: "#fff0e6", fg: "#3b2b2b", accent: "#ff6b6b", radius: "18px", font: "'Nunito', 'Segoe UI', sans-serif" },
];

function mockupHtml(feature, s) {
  return `<!doctype html>
<meta charset="utf-8">
<title>${feature} - ${s.name}</title>
<!-- Style direction: ${s.name}. Replace this placeholder with the real mockup. -->
<style>
  :root { --bg:${s.bg}; --panel:${s.panel}; --fg:${s.fg}; --accent:${s.accent}; --r:${s.radius}; }
  * { box-sizing: border-box; }
  body { margin:0; height:100vh; display:flex; flex-direction:column;
         background:var(--bg); color:var(--fg); font-family:${s.font}; }
  header { padding:12px 16px; background:var(--panel); border-bottom:1px solid rgba(128,128,128,.25);
           display:flex; align-items:center; gap:12px; }
  header b { font-size:14px; } header .badge { margin-left:auto; font-size:11px; opacity:.7; }
  main { flex:1; display:flex; min-height:0; }
  nav { width:200px; background:var(--panel); padding:12px; border-right:1px solid rgba(128,128,128,.25); }
  nav a { display:block; padding:8px 10px; border-radius:var(--r); color:inherit; text-decoration:none; font-size:13px; }
  nav a.active { background:var(--accent); color:var(--bg); }
  section { flex:1; padding:20px; overflow:auto; }
  .card { background:var(--panel); border-radius:var(--r); padding:16px; margin-bottom:14px; }
  button { background:var(--accent); color:var(--bg); border:0; border-radius:var(--r);
           padding:9px 16px; font-size:13px; cursor:pointer; }
</style>
<header><b>${feature}</b><span>${s.name}</span><span class="badge">MOCKUP - ${s.id}</span></header>
<main>
  <nav>
    <a class="active">Overview</a><a>Items</a><a>Settings</a><a>About</a>
  </nav>
  <section>
    <div class="card"><h3 style="margin-top:0">Panel A</h3>
      <p>Replace this skeleton with the real ${feature} screen in the ${s.name} direction.</p>
      <button>Primary action</button></div>
    <div class="card"><h3 style="margin-top:0">Panel B</h3>
      <p>Use distinct directions to compare the design before implementation.</p></div>
  </section>
</main>
`;
}

const NOTES = (feature, styles) => `# Mockups - ${feature}

DESIGN stage for the GUI feature "${feature}". Rule: >=${styles.length} stylistically distinct
mockups plus approval before GUI implementation.

## Variants
${styles.map((s) => `- \`${s.id}.html\` - ${s.name}`).join("\n")}

## Closing the gate
1. Turn the skeletons into real screen mockups.
2. Review and choose a direction.
3. Create an \`APPROVED\` file in this directory.
4. Implement GUI code only after approval.
`;

function main() {
  const feature = (process.argv[2] || "").trim().replace(/[^a-zA-Z0-9._-]/g, "-");
  if (!feature) { console.error("usage: node hooks/new-mockups.js <feature-name>"); process.exit(1); }
  const dir = path.join(ROOT, "design", "mockups", feature);
  if (fs.existsSync(dir)) { console.error(`already exists: design/mockups/${feature}/; not overwriting.`); process.exit(1); }
  fs.mkdirSync(dir, { recursive: true });
  for (const s of STYLES) fs.writeFileSync(path.join(dir, s.id + ".html"), mockupHtml(feature, s));
  fs.writeFileSync(path.join(dir, "NOTES.md"), NOTES(feature, STYLES));
  console.log(`created ${STYLES.length} mockups: design/mockups/${feature}/`);
  STYLES.forEach((s) => console.log(`   - ${s.id}.html (${s.name})`));
  console.log(`Next: refine mockups, get approval, then create design/mockups/${feature}/APPROVED`);
}
main();
