# Dropwheel Review Handoff Prompt

Use this after any Dropwheel review, especially `$color-team-review`.

If the review finds issues in harness-owned files or behavior, create a handoff
for `agents` even when the Dropwheel canary passes.

Harness-owned areas include:

- `hooks/**`
- `harness.config.json`
- `lefthook.yml`
- `.gitleaks.toml`
- `cog.toml`
- `.github/workflows/**`
- `.github/rulesets/**`
- `.github/CODEOWNERS`
- `AGENTS.md` rules about the harness loop
- installer, doctor, verify, design-gate, guard, release-preflight behavior

Write the handoff under:

```text
C:\Users\poweruser\projects\llms\agents\inbox\dropwheel
```

Use filenames:

```text
review-handoff-YYYYMMDD-HHMMSS.md
review-handoff-YYYYMMDD-HHMMSS.json
```

The Markdown handoff must include:

- source Dropwheel thread URL, if known
- source Dropwheel repo path and branch
- related canary report path, if any
- each harness finding with severity, confidence, evidence, impact,
  recommendation, and suggested regression test
- explicit note not to edit Dropwheel app code from `agents`

If a known agents inbox thread is available, also send the same summary there.
If no thread is known, the file handoff is still required and sufficient.

Do not create an agents handoff for pure Dropwheel application findings. For
those, create a local Dropwheel auto-fix item under:

```text
C:\Users\poweruser\projects\csharp\dropwheel\inbox\auto-fix
```

Then use `.codex/automation-prompts/dropwheel-auto-fix.md`.
