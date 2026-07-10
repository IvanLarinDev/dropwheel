# Dropwheel Auto-Fix Automation Prompt

Apply Dropwheel-owned fixes when the harness allows it.

## Assigned Fix Worker Mode

When invoked by the orchestrator as an assigned fix worker, process only the
single finding, fingerprint, or inbox item named by the coordinator.

In this mode:

- create a unique isolated local worktree under `.codex\auto-fix-worktrees`;
- create or use a feature branch inside that isolated worktree;
- do not edit the shared Dropwheel checkout directly;
- do not move inbox items into `_processed`;
- write fix and verification metadata back to the run manifest;
- do not merge, push, release, reset, or force-push.

The coordinator moves inbox items only after the branch is verified and either
accepted or explicitly routed away.

## Inputs

Check these sources, newest first:

- `C:\Users\poweruser\projects\csharp\dropwheel\inbox\auto-fix\*.md`
- `C:\Users\poweruser\projects\csharp\dropwheel\inbox\auto-fix\*.json`
- failed canary reports under
  `C:\Users\poweruser\projects\csharp\dropwheel\reports\harness-canary`
  when their owner is `dropwheel-or-contract` or `dropwheel-harness-update`

Ignore files already moved under `inbox\auto-fix\_processed`.
For canary reports, ignore older failed reports when a newer report for the
same target SHA has already passed or the same finding was already committed.

## Ownership

Auto-fix only applies to Dropwheel product work:

- `src/**`
- `tests/**`
- product docs/assets that are directly related to the fix
- build or packaging files only when the failure is clearly Dropwheel-owned

Auto-fix may also accept generated harness updates in Dropwheel when the owner
is `dropwheel-harness-update`. In that case, only run the agents installer and
commit its generated output; do not manually patch harness files.

Do not auto-fix harness-owned areas here:

- `hooks/**`
- `harness.config.json`
- `lefthook.yml`
- `.gitleaks.toml`
- `cog.toml`
- `.github/workflows/**`
- `.github/rulesets/**`
- `.github/CODEOWNERS`
- `AGENTS.md` harness loop behavior

If a finding is harness-owned behavior rather than generated target install
output, create or update an agents handoff using
`.codex/automation-prompts/dropwheel-review-handoff.md`.

## Process

1. Read the newest unprocessed item and classify it:
   - `dropwheel`: proceed with auto-fix.
   - `dropwheel-harness-update`: proceed by accepting installer output in
     Dropwheel.
   - `agents-harness`: route to agents; do not patch Dropwheel.
   - `needs-triage`: stop with a concise question only if local evidence cannot
     classify ownership.
2. Before editing, run:

   ```powershell
   node hooks\doctor.js
   git status --short
   ```

3. If `doctor` fails because the harness is missing, inconsistent, or blocked,
   stop and route to agents. Do not weaken or bypass the harness.
4. If the checkout is dirty before the run, create an isolated local worktree
   under `.codex\auto-fix-worktrees` and make code changes there. Keep inbox
   bookkeeping in the main checkout.
5. For `dropwheel-harness-update`, run:

   ```powershell
   node C:\Users\poweruser\projects\llms\agents-main\install.js --target <fix-root> --force --json
   git add -A
   node hooks\doctor.js --json
   node hooks\verify.js
   ```

   The installer may return a non-zero exit before `git add -A` because its
   built-in doctor sees installer-created files as untracked. Continue only when
   the installer JSON reason is the expected bootstrap/untracked state. After
   staging, `node hooks\doctor.js --json` and `node hooks\verify.js` must pass.
   If they are green, create a local feature-branch commit with a Conventional
   Commit message such as `chore(harness): update generated harness`.
6. For `dropwheel`, make the smallest product fix and add or update focused
   tests.
7. If UI files are touched, obey design-gate. Create mockups if needed, but do
   not mark a new direction as approved without user approval.
8. Verify with:

   ```powershell
   node hooks\verify.js
   ```

   Also run narrower tests first when useful.
9. Read the verify output, not just the exit code. Fix new warnings or explain
   why they are unrelated.
10. If the tree was clean before the run and verify is green, create a local
   feature branch and local commit with a conventional commit message. Do not
   push, merge, release, reset, or force-push.
11. If the tree was dirty before the run and no isolated worktree was used,
    leave verified changes uncommitted and report exactly what was changed.
12. In normal serialized mode, when an inbox item is closed or routed away,
    move its `.md` and `.json` pair into `inbox\auto-fix\_processed`. In
    assigned fix-worker mode, leave inbox movement to the coordinator.

## Safety Rules

- Never use `--no-verify`, `LEFTHOOK=0`, `HARNESS_ACK_BYPASS=1`, or
  `HARNESS_DISABLED_CHECKS` from this automation.
- Never manually edit harness files to make a Dropwheel product fix pass.
- For `dropwheel-harness-update`, only accept generated installer output after
  `doctor` and `verify` pass.
- Prefer a small failing test before the fix when the bug is reproducible.
- If the minimal fix needs product judgment, leave a narrow proposal instead of
  guessing.
