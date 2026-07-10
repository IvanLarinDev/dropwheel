# Dropwheel Pipeline Orchestrator Prompt

Run the whole Dropwheel/agents harness pipeline as one coordinated workflow.
Parallelize read-only discovery and isolated fix work when multi-agent tools are
available; keep accepted-main mutation and merge commits serialized.

This replaces separate scheduled cards. Keep the UI simple: this automation is
the single recurring entry point, while the other prompt files are subroutines.

## Roots

- Pipeline config/scripts root:
  `C:\Users\poweruser\projects\csharp\dropwheel-release`
- Dropwheel main worktree for accepted/merged code:
  `C:\Users\poweruser\projects\csharp\dropwheel-release`
- Agents harness development/config root:
  `C:\Users\poweruser\projects\llms\agents`
- Agents harness accepted main worktree:
  `C:\Users\poweruser\projects\llms\agents-main`
- Automation state root for generated reports and memory:
  `C:\Users\poweruser\.codex\automations\dropwheel-pipeline-orchestrator`

Set the working directory to the pipeline config/scripts root before running
relative `scripts\...` commands. The versioned accepted-main checkout is the
source of truth for automation code; do not depend on untracked files in a
development branch.

When running canaries for the accepted state, use the scripts from the pipeline
root and pass `-AgentsRoot C:\Users\poweruser\projects\llms\agents-main`
and `-DropwheelRoot C:\Users\poweruser\projects\csharp\dropwheel-release`.
Also pass explicit `-ReportRoot` and `-WorktreeRoot` paths outside
`dropwheel-release`; generated reports and temporary worktrees must not make the
accepted main checkout dirty.

## Run Manifest

Every run creates one manifest under the automation state root. The manifest is
the coordinator/worker contract and the merge arbiter's evidence ledger.

```powershell
$automationRoot = "C:\Users\poweruser\.codex\automations\dropwheel-pipeline-orchestrator"
$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $automationRoot "runs\$runStamp"
$manifest = powershell -ExecutionPolicy Bypass -File .\scripts\pipeline-manifest.ps1 `
  -Mode New `
  -RunRoot $runRoot `
  -RunId $runStamp
```

Workers must write structured results into the manifest with
`scripts\pipeline-manifest.ps1` before the coordinator decides to fix or merge:

- `AddWorker`: worker started/completed/skipped metadata and evidence summary.
- `AddFinding`: normalized finding with owner, evidence, fingerprint, and
  recommendation in `DataJson`.
- `UpdateFindingDisposition`: terminal `fixed`, `open`, or `deferred` status
  plus non-empty evidence for one finding id or fingerprint.
- `AddFix`: branch/worktree/commit metadata for a fix.
- `AddVerification`: verify/canary evidence, including report path and SHA.
- `AddMerge`: accepted-main merge metadata.
- `Complete`: final status.

Use `DataJson` for task-specific details. Keep large logs in report files and
store only report paths plus pass/fail summaries in the manifest.
Do not write empty `{}` worker data. A completed discovery worker must include:

```json
{
  "checkedScope": ["src/..."],
  "evidenceSources": ["git diff", "tests/..."],
  "candidatesConsidered": 0,
  "rejectedCandidates": [],
  "noFindingReason": "No product invariant or regression gap survived evidence review.",
  "residualRisk": "Narrow risk statement, or none.",
  "confidence": "High|Medium|Low"
}
```

If a worker finds an actionable candidate, also write `AddFinding` with
`fingerprint`, `severity`, `confidence`, `category`, `evidence`, `impact`,
`recommendation`, and `testToAdd`.

The manifest script serializes concurrent JSON updates with a per-manifest
lock, so parallel workers may append records without overwriting each other.
When JSON is generated dynamically, write it under
`$runRoot\manifest-data\<record-id>.json` and pass `-DataJsonPath`; this avoids
PowerShell argument quoting loss through nested `powershell -File` calls. Use
`-DataJson` only for short single-quoted literals.
`Complete` is terminal: write all worker results, findings, verification
records, merge records, dispositions, and the coordinator summary before
calling it. The manifest rejects completion until every finding has received
`UpdateFindingDisposition`. Do not append records after `Complete`.

## Multi-Agent Model

- Coordinator: owns the manifest, budgets, dedupe, routing, canonical
  inbox/handoff writes, `_processed` movement, and final summary.
- Health/negative workers: may run in parallel after the initial accepted-root
  snapshot when they only touch isolated temp worktrees.
- Discovery workers: read-only and safe to parallelize. Run independent slices
  for Dropwheel regression mining, Dropwheel product discovery, agents harness
  regression mining, and agents harness discovery when budget allows. If tools
  are unavailable, run the same slices serially and still write manifest records.
- Fix workers: one finding per isolated worktree/branch. A fix worker owns only
  its assigned files, must not touch accepted main directly, and must not move
  shared inbox items into `_processed`.
- Verification workers: verify a committed fix branch and write report SHA,
  branch SHA, command results, and canary paths into the manifest.
- Merge arbiter: the only role allowed to mutate `dropwheel-release/main` or
  `agents-main/main`. It processes verified fixes one at a time.

Finding dedupe uses a stable fingerprint:

```text
<owner>|<subsystem>|<file-or-contract>|<invariant-or-failure-mode>
```

Merge duplicate findings before creating fix branches. Do not run two fix
workers for the same fingerprint in one run.

Run budgets:

- Maximum Dropwheel fixes per run: 2.
- Maximum Agents fixes per run: 1.
- Maximum actionable findings accepted per run: 6.
- Stop after post-merge canary fails; route that failure before continuing.

## Product Discovery Rotation

Dropwheel product discovery alternates between narrow and broad slices. The
goal is to avoid overfitting discovery to only the latest changed files.

Run a broad product slice when any of these are true:

- the previous accepted-state run had no actionable product finding;
- two consecutive runs focused only on recent commits or post-fix validation;
- automation memory shows no broad product slice in the last 7 days.

A broad product slice is still bounded. Review 2-3 related subsystems plus
their focused tests, not the whole repository. Prefer one of these rotating
coverage keys:

- `drop-flow`: `OverlayWindow*`, `FileOps`, undo/history, drag/drop edge cases.
- `watcher-flow`: `WatcherService`, `SortService`, file collision and lifecycle
  tests.
- `target-config-flow`: target persistence, sorter rules, app config, corrupt
  config recovery, compatibility.
- `runtime-flow`: startup/tray/hotkey lifecycle, cancellation, shutdown,
  user-visible failure reporting.

The product discovery worker must record `sliceKind`, `coverageKey`,
`checkedSubsystems`, `entrypoints`, `testsReviewed`, `invariantsChecked`,
`candidatesConsidered`, `rejectedCandidates`, `noFindingReason`, and
`residualRisk` in manifest `DataJson`.

## Subroutines

- `.codex/automation-prompts/dropwheel-harness-canary.md`
- `.codex/automation-prompts/dropwheel-negative-canary.md`
- `.codex/automation-prompts/dropwheel-auto-fix.md`
- `.codex/automation-prompts/dropwheel-review-handoff.md`
- `.codex/automation-prompts/dropwheel-regression-mining.md`
- `.codex/automation-prompts/dropwheel-product-discovery.md`
- `C:\Users\poweruser\projects\llms\agents-main\.codex\automation-prompts\agents-dropwheel-inbox-triage.md`
- `C:\Users\poweruser\projects\llms\agents-main\.codex\automation-prompts\agents-harness-regression-mining.md`
- `C:\Users\poweruser\projects\llms\agents-main\.codex\automation-prompts\agents-harness-discovery-review.md`

## Sequence

1. Set location to `C:\Users\poweruser\projects\csharp\dropwheel-release`,
   create the run manifest as described above, then capture the accepted-root
   snapshot:
   - `git status --short --branch` and `git rev-parse HEAD` for
     `dropwheel-release`.
   - `git status --short --branch` and `git rev-parse HEAD` for `agents-main`.
   Accepted roots must be clean except for generated artifacts explicitly moved
   into the automation state root.

2. Run the Dropwheel health canary:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\harness-canary.ps1 `
     -AgentsRoot C:\Users\poweruser\projects\llms\agents-main `
     -DropwheelRoot C:\Users\poweruser\projects\csharp\dropwheel-release `
     -ReportRoot (Join-Path $runRoot "health\reports") `
     -WorktreeRoot (Join-Path ([System.IO.Path]::GetTempPath()) "dw-canary") `
     -MirrorToAgentsInbox
   ```

3. If it fails, read `report.md` and `report.json`, write an `AddVerification`
   manifest record, then route immediately:
   - `dropwheel-harness-update`: run the Dropwheel auto-fix prompt now. Accept
     only generated installer output after staged `doctor --json` and `verify`
     pass.
   - `dropwheel-or-contract`: inspect evidence. If Dropwheel-owned, create or
     use an `inbox\auto-fix` item and run the Dropwheel auto-fix prompt now.
     If harness-owned, create an agents handoff.
   - `agents-harness` or `needs-triage`: make sure the report is mirrored into
     `C:\Users\poweruser\projects\llms\agents\inbox\dropwheel`, then run the
     agents inbox triage prompt now.

4. If health is green, run the negative canary and discovery workers. The
   negative canary may run in parallel with read-only discovery workers because
   it uses isolated temp worktrees:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\harness-negative-canary.ps1 `
     -AgentsRoot C:\Users\poweruser\projects\llms\agents-main `
     -DropwheelRoot C:\Users\poweruser\projects\csharp\dropwheel-release `
     -ReportRoot (Join-Path $runRoot "negative\reports") `
     -WorktreeRoot (Join-Path ([System.IO.Path]::GetTempPath()) "dw-neg-canary") `
     -MirrorToAgentsInbox
   ```

   If it reports `agents-harness`, immediately run the agents inbox triage
   prompt before starting merges.

5. Run discovery:
   - first, process existing active inbox/handoff items;
   - otherwise run the next rotation slice, selecting a broad Dropwheel product
     slice according to the Product Discovery Rotation rules above when due;
   - when multi-agent tools are available and budget allows, run independent
     read-only discovery slices in parallel:
     - Dropwheel regression mining;
     - Dropwheel product discovery, using `sliceKind=broad` when the rotation
       rules say a broad slice is due;
     - agents harness regression mining;
     - agents harness discovery review.

   Each worker writes `AddWorker` with non-empty `DataJson` and either
   `AddFinding` or a no-finding `AddEvent` with checked scope and residual
   risk. The coordinator dedupes by fingerprint and selects fixes within
   budget. A no-finding worker is only useful if the manifest says what scope
   was checked, what evidence was read, why candidates were rejected, and what
   residual risk remains.

6. If discovery or routing creates a local branch, finish the fix commit before running
   the health canary against that fix root. The canary creates an isolated
   worktree from `HEAD`, so a dirty fix root would otherwise verify the previous
   commit instead of the fix. Treat `ownerHint: pipeline-precondition` as a
   workflow error: commit the intended fix on the local branch, remove generated
   artifacts, or otherwise make the fix root clean, then rerun the canary. The
   current main checkout may still report an expected `dropwheel-harness-update`
   until the branch is accepted.

7. Auto-merge verified local fixes into the Dropwheel main worktree when all of
   these are true:
   - the fix branch is local;
   - `doctor --json`, `verify`, and canary are green for the fix root;
   - the canary report's Dropwheel SHA equals the verified fix branch `HEAD`;
   - the target main worktree is clean;
   - the merge does not require product judgment.

   Merge into `C:\Users\poweruser\projects\csharp\dropwheel-release` with
   hooks enabled. If branch-guard blocks a direct main commit, use the harness
   main-commit exception only for this merge commit:

   ```powershell
   $env:HARNESS_ALLOW_MAIN = "1"
   git commit -m "<conventional merge message>"
   Remove-Item Env:\HARNESS_ALLOW_MAIN
   ```

   Do not use `--no-verify`, do not push, and do not release. If conflicts
   occur, preserve current main release metadata unless the fix explicitly
   changes it, then rerun `doctor --json`, `verify`, and canary before
   committing.

   Before deciding that the target main worktree is dirty, check whether the
   only changes are generated pipeline artifacts such as `reports/`. Move or
   remove those generated artifacts after preserving the report under the
   automation state root; do not commit generated reports into Dropwheel.

8. Auto-merge verified local fixes into the Agents accepted main worktree when
   all of these are true:
   - the fix branch is local and clean;
   - `node hooks/verify.js` is green for the fix root;
   - a Dropwheel health canary using `-AgentsRoot <fix root>` is green;
   - the canary report's Agents SHA equals the verified fix branch `HEAD`;
   - `C:\Users\poweruser\projects\llms\agents-main` is clean;
   - the merge is a test/harness maintenance fix and does not require product
     judgment.

   Merge into `C:\Users\poweruser\projects\llms\agents-main` with hooks
   enabled. If branch-guard blocks a direct main commit, use the same scoped
   main-commit exception only for this merge commit:

   ```powershell
   $env:HARNESS_ALLOW_MAIN = "1"
   git commit -m "<conventional merge message>"
   Remove-Item Env:\HARNESS_ALLOW_MAIN
   ```

   Do not use `--no-verify`, do not push, and do not release. After the merge,
   rerun `node hooks/verify.js` in `agents-main` and a Dropwheel health canary
   using `-AgentsRoot C:\Users\poweruser\projects\llms\agents-main`; the canary
   report's Agents SHA must equal the new `agents-main` `HEAD`.

   After verification, synchronize the development `main` without rewriting
   history, then clean the exact pipeline-owned fix branch:

   ```powershell
   git -C C:\Users\poweruser\projects\llms\agents fetch C:\Users\poweruser\projects\llms\agents-main main
   git -C C:\Users\poweruser\projects\llms\agents checkout main
   git -C C:\Users\poweruser\projects\llms\agents merge --ff-only FETCH_HEAD
   node C:\Users\poweruser\projects\llms\agents-main\hooks\post-merge-cleanup.js `
     --root C:\Users\poweruser\projects\llms\agents `
     --branch <verified-fix-branch> --base main --no-fetch --apply
   ```

   Run these commands only from a clean development checkout. If it contains
   unrelated user work or cannot fast-forward, leave the finding open and do
   not complete the run; never reset or discard it.

9. If discovery creates a Dropwheel auto-fix item, run Dropwheel auto-fix in the
   same orchestrator run. If discovery creates an agents handoff or agents
   regression fix, run agents inbox triage or agents auto-fix in the same
   orchestrator run, then apply the Agents accepted-main auto-merge rule above
   when verification is green.

10. Give every finding an `UpdateFindingDisposition` record. Then run the
    terminal topology gate:

    ```powershell
    node C:\Users\poweruser\projects\llms\agents-main\hooks\repo-state-audit.js `
      --root C:\Users\poweruser\projects\llms\agents `
      --accepted-root C:\Users\poweruser\projects\llms\agents-main `
      --base main --strict
    ```

    The run cannot complete while main SHAs differ, a worktree is dirty, or an
    extra branch/worktree remains. Preserve unmerged or dirty user work and
    report the run as blocked instead of deleting it.

11. Finish with a compact summary, write the coordinator discovery/run summary,
    then mark the manifest complete:
   - reports generated;
   - owner classifications;
   - worker scopes and no-finding reasons;
   - branches/commits created;
   - merge commits created on local main;
   - verification commands and pass/fail counts;
   - active inbox items left open;
   - whether current main checkout is blocked only because a local branch has
     not been accepted.

   ```powershell
   $summaryJson = @{
     reports = @("...")
     findings = @()
     fixes = @()
     merges = @()
     inboxOpen = @()
     residualRisk = "..."
   } | ConvertTo-Json -Depth 8 -Compress
   $summaryPath = Join-Path $runRoot "manifest-data\discovery-summary.json"
   New-Item -ItemType Directory -Force -Path (Split-Path -Parent $summaryPath) | Out-Null
   Set-Content -LiteralPath $summaryPath -Value $summaryJson -Encoding utf8
   $completePath = Join-Path $runRoot "manifest-data\complete.json"
   Set-Content -LiteralPath $completePath `
     -Value '{"summary":"Run closed after all worker/coordinator records were written."}' `
     -Encoding utf8

   powershell -ExecutionPolicy Bypass -File .\scripts\pipeline-manifest.ps1 `
     -Mode AddEvent `
     -ManifestPath $manifest `
     -WorkerId discovery-summary `
     -Role coordinator `
     -Status no-findings `
     -DataJsonPath $summaryPath

   powershell -ExecutionPolicy Bypass -File .\scripts\pipeline-manifest.ps1 `
     -Mode Complete `
     -ManifestPath $manifest `
     -Status complete `
     -DataJsonPath $completePath
   ```

## Safety

- Do not push, release, reset, force-push, weaken harness checks, or use bypass
  environment variables except the scoped `HARNESS_ALLOW_MAIN=1` main-commit
  exception described above for verified local auto-merges.
- If the current checkout is dirty, use isolated local worktrees for code
  changes. Keep inbox bookkeeping in the main checkout.
- Do not manufacture findings. A discovery slice can end with no actionable
  item, but must state checked scope and residual risk.
- Never allow worker agents to merge, push, release, reset, or mutate accepted
  main. Workers may only write in their assigned isolated worktree or write
  manifest/report artifacts under the automation state root.
