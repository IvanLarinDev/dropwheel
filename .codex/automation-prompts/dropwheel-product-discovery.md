# Dropwheel Product Discovery Prompt

Find real Dropwheel product risks and feed only actionable findings into the
existing auto-fix loop.

This automation is not a ritual review. It should either produce a concrete,
evidence-backed finding or state exactly what scope was checked and why no
substantial finding was found.

## Parallel Worker Mode

When invoked by the orchestrator as a parallel discovery worker, this prompt is
read-only:

- do not create `inbox\auto-fix\*.md` or `.json`;
- do not move `_processed` items;
- do not edit code, run installers, commit, merge, push, or release;
- return structured candidate findings only.

For each candidate include `owner`, `severity`, `confidence`, `category`,
`evidence`, `impact`, `recommendation`, `testToAdd`, `scope`, and a stable
`fingerprint` in the form:

```text
dropwheel|<subsystem>|<file-or-contract>|<invariant-or-failure-mode>
```

The orchestrator owns dedupe and creates canonical inbox items after all
parallel workers finish.

When the orchestrator passes or implies `sliceKind=broad`, return a broad-slice
worker record even if no finding is produced. Include:

- `sliceKind`: `broad`
- `coverageKey`
- `checkedSubsystems`
- `entrypoints`
- `testsReviewed`
- `invariantsChecked`
- `candidatesConsidered`
- `rejectedCandidates`
- `noFindingReason`
- `residualRisk`
- `confidence`

## Scope

Review Dropwheel product code and product tests:

- `src/**`
- `tests/**`
- product docs or packaging files only when they affect runtime behavior,
  compatibility, release safety, or developer workflow

Do not modify product code from this automation. The output of this discovery
task is an inbox item for `Dropwheel Auto Fix`.

Do not treat generated harness files as product findings:

- `hooks/**`
- `harness.config.json`
- `lefthook.yml`
- `.gitleaks.toml`
- `cog.toml`
- `.github/workflows/**`
- `.github/rulesets/**`
- `.github/CODEOWNERS`

If the evidence points to harness behavior, route it through
`.codex/automation-prompts/dropwheel-review-handoff.md`.

## Process

1. Inspect current state:

   ```powershell
   git status --short
   node hooks\verify.js --list
   ```

2. Choose the review slice:
   - if `sliceKind=broad`, review one bounded broad coverage key from the list
     below;
   - otherwise choose a narrow slice: changed product files first, then one
     product subsystem with tests, rotating across `Services`, `UI`, `Models`,
     and app startup/tray/drop flows;
   - include matching tests when they exist.

   Broad coverage keys:

   - `drop-flow`: `OverlayWindow*`, `FileOps`, undo/history, drag/drop edge
     cases.
   - `watcher-flow`: `WatcherService`, `SortService`, collision handling,
     watcher lifecycle, cancellation.
   - `target-config-flow`: target persistence, sorter rules, app config,
     corrupt config recovery, compatibility.
   - `runtime-flow`: startup/tray/hotkey lifecycle, shutdown, user-visible
     failure reporting.

   A broad slice should cover 2-3 related subsystems and their tests. It should
   not try to audit every file in `src/**` in one run.
3. Run an evidence-first Color Team Review on that slice:
   - prefer correctness, data loss, file operation safety, drag/drop edge
     cases, hotkey/watch lifecycle, configuration compatibility, silent
     failures, and missing regression tests;
   - do not invent findings to fill the format;
   - distinguish confirmed issues, likely issues, and hypotheses.
4. In normal serialized mode, for each confirmed or likely product finding that
   is P0/P1 and can be fixed without product judgment, create an inbox item
   pair:

   - `inbox\auto-fix\auto-fix-YYYYMMDD-HHMMSS-<slug>.md`
   - `inbox\auto-fix\auto-fix-YYYYMMDD-HHMMSS-<slug>.json`

5. The JSON item must include:

   ```json
   {
     "schema": "dropwheel-auto-fix/v1",
     "source": "dropwheel-product-discovery",
     "owner": "dropwheel",
     "severity": "High|Medium|Low",
     "confidence": "Confirmed|Likely",
     "category": "correctness|security|performance|architecture|maintainability|DX|compatibility|operations",
     "evidence": ["file:line or scenario"],
     "impact": "...",
     "recommendation": "...",
     "testToAdd": "...",
     "scope": ["src/...","tests/..."]
   }
   ```

6. The Markdown item should be short and actionable: evidence, impact,
   recommended fix, and the test to add.
7. For a broad no-finding result, report the coverage key, checked subsystems,
   entrypoints, tests reviewed, invariants checked, rejected candidates, and
   residual risk. This is required evidence, not optional prose.
8. Do not create inbox items for cosmetic notes, speculative ideas, or findings
   that need user product judgment. Report those as residual risk only.
9. Finish quietly when no actionable finding exists, but include the checked
   files/subsystem and residual risk in the final answer.

## Safety

- Never push, merge, release, reset, force-push, or bypass the harness.
- Never weaken tests or harness checks to manufacture a green result.
- Keep duplicate findings collapsed into one inbox item.
