# AGENTS.md - Dev Loop

Every agent pass through code follows one loop. Anything that can be checked by
code belongs in hooks; this document keeps the behavior that hooks cannot fully
enforce.

## Loop

```text
1. EXPLORE          - read the codebase, patterns, and risks
2. PLAN             - plan -> user approval
2.5 DESIGN (GUI)    - classify UI impact -> matching variants -> approval
3. IMPLEMENT+TEST   - code and tests together; edge cases become tests
4. VERIFY           - node hooks/verify.js + read output + git diff self-review
   - failure/new warnings -> return to step 3
   - green              -> step 5
5. COMMIT on branch -> PR, never directly on main
5.5 MERGE+CLEANUP   - confirmed MERGED + green main -> exact branch cleanup
6. REPORT           - changed / verified / remaining / manual test notes
7. USER DECISION    - accept = DONE; revise -> 2 or 3; reject -> revert
```

Shortcut: a trivial typo or one-line fix can skip plan mode, but VERIFY and a
feature branch are always required.

## Bootstrap

Before requiring this loop from a target repository, commit the harness to
`main` through a separate bootstrap PR. Minimum set: `hooks/`, `AGENTS.md`,
`harness.config.json`, `lefthook.yml`, `cog.toml`, `.gitleaks.toml`,
`settings.example.json`, and `.github/` when CI/rulesets are enabled.

`node hooks/doctor.js` checks not only that these files exist, but also that they
are tracked in git. If harness files are untracked, a clean worktree from
`origin/main` cannot run `node hooks/verify.js`, `design-gate.js`, or release
through `cog bump --auto`. In that state, create the bootstrap PR first; release
flow is not fully enforceable.

## Stage Rules

**1. EXPLORE.** Do not assume structure. Verify it with `rg`, file reads, and
existing tests. Changes across more than two or three files, or behavior changes,
need a plan.

**2. PLAN.** Non-trivial work goes through plan mode and user approval before
implementation. The plan names the files, rationale, tests, risks, and edge
cases.

**2.5 DESIGN (GUI).** Classify the user-visible change before generating DESIGN
evidence. Do not use four unrelated visual themes for every kind of UI work.

- Backend-only work with no user-visible UI impact skips DESIGN. A mixed task
  designs only its UI slice. `design-gate.js` skips automatically when no changed
  file matches `harness.config.json -> ui.globs`.
- Animation uses one concrete scenario. The low-cost option is at least four
  written motion variants (`--fidelity text`); use executable HTML/JavaScript
  variants (`--fidelity js`) when timing, gesture, or physical feel cannot be
  judged from prose.
- Changes to an existing UI keep its real visual language and components while
  comparing layouts, placement, or interaction patterns. Pass at least one
  current UI source file through `--baseline`.
- UI created from scratch compares at least four stylistically distinct visual
  directions.

Use one explicit mode; the generator has no generic default:

```text
node hooks/new-mockups.js <feature> --kind existing-ui --baseline <repo-path>
node hooks/new-mockups.js <feature> --kind new-ui
node hooks/new-mockups.js <feature> --kind animation --fidelity text|js --example <scenario>
node hooks/new-mockups.js <feature> --kind backend
```

The generator writes `DESIGN.json`, mode-appropriate variants, and `NOTES.md`.
After user selection, create `design/mockups/<feature>/APPROVED` with a
`ui: <changed-ui-path-or-glob>` line. The gate checks the manifest and passes UI
changes only when that approved set is both touched and scoped to the changed UI
paths in the same branch diff. Legacy sets without `DESIGN.json` remain valid
only when `APPROVED` carries the same `ui:` scope. If the user explicitly waives
new mockups for a UI-path change, create `design/mockups/<feature>/WAIVER.json`
with `schemaVersion`, `feature`, `uiPaths`, `reason`, `date`, and
`approvedBy` or `approvalSource`.

**3. IMPLEMENT+TEST.** Code and tests move together. If a target project has no
test runner, report that and propose a minimal one.

**4. VERIFY before commit.** `node hooks/verify.js` auto-detects common stacks:
Python uses ruff and pytest; C# uses dotnet format, build with warnings as
errors, and test; Rust uses fmt, clippy with warnings as errors, and test; Node
uses npm lint/build/test. Overrides live in `harness.config.json -> verify`.
`--list` prints the plan without running it.

VERIFY also runs a debug audit on changed files. Hard markers such as
`debugger;`, `breakpoint()`, `pdb.set_trace()`, and `dbg!()` fail VERIFY. Soft
markers such as `console.log` and `print` are notes when configured. Beyond the
exit code, read build output for new warnings, deprecations, and fallbacks, and
run a git diff self-review for debug logs, commented-out code, and debris.

**5. COMMIT -> PR.** Work on a feature branch such as `feat/...`, `fix/...`, or
`docs/...`, never directly on `main`. Use Conventional Commits:
`<type>(<scope>): <subject>`. `feat:` means MINOR, `fix:` means PATCH, and `!`
or `BREAKING CHANGE:` means MAJOR. Lefthook rejects co-author and generated-by
trailers. `git push`, force-push, and `reset --hard` require an explicit user
request.

**5.5 MERGE -> CLEANUP.** Feature, fix, docs, chore, refactor, test, CI, and
other development branches do not wait for a release. After the PR is confirmed
server-side `MERGED` and the resulting `main` push CI is green, run from a
separate clean base worktree:

```powershell
node hooks/post-merge-cleanup.js --branch feat/example --base origin/main
node hooks/post-merge-cleanup.js --branch feat/example --base origin/main --apply
```

The helper requires both local and remote refs to be ancestors of the base,
removes only clean linked worktrees, and is idempotent when the branch was
already deleted. Dirty, diverged, or unmerged refs block exact cleanup and are
never forced. `release/*` and `hotfix/*` are deliberately ineligible: retain
them until their tag, published artifacts, and smoke tests succeed. The
release-wide cleanup remains a final audit for merged branches missed earlier.

Keep one persistent canonical project directory on clean `main` when idle.
Feature and release worktrees are temporary and must be removed after cleanup;
do not create a sibling `<repo>-main` accepted clone unless the user explicitly
configures an external two-root pipeline. Finish the merge/cleanup sequence with
a strict, read-only topology audit:

```powershell
node hooks/repo-state-audit.js --root <canonical-root> --base main --remote origin --fetch --strict
```

Completion requires matching local `main` SHAs, clean expected worktrees, and
no leftover local branches or linked worktrees. A mismatch means the pipeline
is still active or incomplete; synchronize and clean it explicitly, then rerun
the audit. Do not make the audit delete or reset work automatically.

**6. REPORT.** Report what changed, what was verified with commands and results,
what remains, and how the user can test manually.

**7. USER DECISION.** The loop is complete only when the user accepts the result.

## Release Flow

Run this only on an explicit release request. A request to make a **full
release** is standing authorization for the normal release actions below:
pushing feature/release branches, creating and merging their PRs, pushing the
computed SemVer tag, publishing the GitHub Release, and deleting merged
development/release branches after smoke testing. Do not pause for a second
approval after the SemVer preview.

Standing authorization does not allow bypassing hooks/rulesets, force-pushing,
continuing after a failed gate, deleting unmerged branches, or discarding a
dirty worktree. Unexpected history repair, rollback, or destructive recovery
still requires a new user decision.

| Step | Action | Gate |
|---|---|---|
| R0 | Merge all intended feature/fix work through PRs into `main`; verify each PR and the resulting `main` push are green, then run exact post-merge cleanup for each branch. | No release from an unmerged feature branch; merged development branches should not accumulate while releases are deferred. |
| R1 | Fetch/prune, then create a new clean release worktree from `origin/main`. Run `node hooks/doctor.js`, `node hooks/verify.js`, and `git describe --tags --abbrev=0`. | The latest tag must be an ancestor of `origin/main`; stop on a broken release graph. |
| R2 | Derive SemVer from merged Conventional Commits with `cog bump --auto --dry-run`. Report the computed tag/diff/notes, create `release/vX.Y.Z`, run `node hooks/release-manifest-bump.js --tag vX.Y.Z`, commit manifest changes as `chore(release): prepare vX.Y.Z`, then run `cog bump --auto --annotated "vX.Y.Z"`. | A full-release request continues without another approval. Stop if the bump is inconsistent with the merged commits or manifests. |
| R2.5 | Run prepare preflight: `node hooks/release-preflight.js --tag vX.Y.Z --base origin/main`. | Clean tree; annotated local tag points at release HEAD; remote tag absent; manifests and CHANGELOG match. |
| R3 | Push **only** `release/vX.Y.Z`, create its PR to `main`, wait for required checks, merge it with a merge commit, and verify server-side `MERGED`. | Do not squash/rebase the PR: the locally tagged release commit must remain in `main`. Do not push the tag yet. |
| R4 | Fetch `origin/main`, wait for its push CI, then run `node hooks/release-preflight.js --tag vX.Y.Z --base origin/main --require-tag-in-base`. | The tag commit must now be an ancestor of `origin/main`; remote tag must still be absent. |
| R5 | Push `vX.Y.Z`. Watch the tag-triggered release workflow and require every release step to pass. | The workflow must build from the exact tag, verify, create a source ZIP + SHA-256, smoke-test it, and publish the GitHub Release. |
| R6 | Run `gh release view vX.Y.Z`; download the published ZIP/checksum; compare SHA-256 and smoke-test the downloaded asset. | For this source-only harness, the tag plus matching CHANGELOG version is the version check. Binary/package projects must also verify their reported binary/package version. |
| R7 | From a separate clean base worktree run `node hooks/release-cleanup.js --base origin/main` and then `node hooks/release-cleanup.js --base origin/main --apply`. | Final audit: delete any remaining merged managed branches and clean linked worktrees. Dirty merged branches block cleanup; unmerged branches are skipped and reported. Tags are retained. |
| R8 | Report tag, merge SHAs, workflow URL, Release URL, asset hash, smoke results, cleanup results, and rollback boundary. | The release is complete only after R6 and R7. |

The source repository's `.github/workflows/release.yml` implements the source
ZIP path. Installed target repositories must provide a release workflow suited
to their own binary/package artifacts; when none exists, reproduce R5-R6
manually from the exact tag and do not claim the release is complete early.

Hotfix: branch from the previous tag, fix, PR to `main`, then follow R1-R8.
Do not make a release commit directly on `main`; the version/changelog commit
uses the release PR in R3.

`release-manifest-bump.js` synchronizes known project manifests before the tag is
created. `release-preflight.js` intentionally fails if the tag/changelog are
ready but a project manifest still reports an old version. If no version
manifest exists, it warns; R6 still verifies the binary version.

After `gh pr merge --delete-branch`, GitHub can merge the PR server-side even if
the local post-merge pull/rebase fails because of a dirty worktree. Verify with
`gh pr view <num> --json state,mergedAt,mergeCommit`; if `state` is `MERGED`, the
merge happened. Sync locally only from a clean tree with `git fetch origin` and
`git merge --ff-only origin/main`. For releases, prefer a new clean worktree from
`origin/main`.

Rollback remains a separate decision: before publication, delete/recreate the
tag only with approval; after publication, use `gh release delete` plus a revert
PR with approval. Branch cleanup never deletes tags, so published release
anchors remain available.

## Harness Layers

**Layer 0 - server ruleset.** The only real enforcement layer. Versioned in
`.github/rulesets/main.json` and applied with `node hooks/apply-ruleset.js`.
It requires PRs, the `verify` required check, and blocks force-push/delete on
main. It cannot be bypassed locally. Private repositories need a plan that
supports rulesets, or the repository must be public.
This source repository uses a solo-maintainer variant: approving/code-owner
review is advisory, while target installs keep regular approving review by
default through `install.js`.

**Layer 1 - lefthook.** Local hygiene for humans and agents:
commit-msg runs `cog verify` and rejects co-author trailers; pre-commit runs
gitleaks and branch guard; pre-push runs `verify.js` and `design-gate.js`.

On Windows, diagnose commands through `.cmd` to avoid PowerShell ExecutionPolicy:

```powershell
lefthook.cmd run pre-commit --command branch-guard --force --verbose
$msg = Join-Path $env:TEMP "commit-msg.txt"; Set-Content $msg "fix(hooks): test"; lefthook.cmd run commit-msg $msg --command no-coauthor --force --verbose
```

Use `--command`, not `--commands`.

**Layer 2 - agent adapter.** Optional per-runtime hooks. `hooks/agent/guard.js`
runs on PreToolUse and `hooks/agent/stop-reminder.js` runs on Stop. Input is
normalized JSON through `hooks/agent/_input.js`. Guard logic is exported as
`run(ctx, env) -> {exitCode, stdout, stderr}` so tests and dispatchers can call
it in-process; the CLI wrapper is for runners.

Stop-reminder is a reminder, not hard enforcement. First Stop with a dirty tree
blocks and shows status; a second Stop with the same status passes so intentional
uncommitted bootstrap/local files do not trap the user.

Strictness:

- `HARNESS_PROFILE=minimal|standard|strict`; minimal keeps only anti-bypass and
  harness-file protection, strict halves loop thresholds.
- `HARNESS_DISABLED_CHECKS=<id,id>` disables targeted checks. These are human
  runner env knobs; agent shell commands should not change hook env.

Contract: exit 0 allows, exit 2 blocks. Notes are emitted through
`hookSpecificOutput.additionalContext` plus a top-level duplicate for simpler
runners. Stop communicates only through `{"decision":"block","reason":"..."}`.
Install by copying the `hooks` block from `settings.example.json` to
`.claude/settings.json`.

| guard.js catches | Type |
|---|---|
| Harness bypass: `--no-verify`, `commit -n`, `core.hooksPath`, `LEFTHOOK=0`, `lefthook uninstall`, writes under `.git/hooks`. | block |
| Edits to harness files: `hooks/`, `lefthook.yml`, configs, workflows, `.claude/settings.json`; both file tools and shell writes are covered, including quoted paths. | block |
| Edits to existing lint/format configs such as `.eslintrc*`, `ruff.toml`, `biome.json`, `clippy.toml`, `pytest.ini`. Mixed files such as `pyproject.toml`, `package.json`, and `tsconfig.json` are intentionally excluded. | block |
| Degenerate loops: trivial command streaks, same action repeated N times, A-B-A-B alternation, for shell and file tools. | block |
| Tool markup debris, low-entropy commands, and unreadable non-empty hook payloads. | block |
| git commit/merge/push or edits while on `main`/`master`. | note |
| UI file edit requiring DESIGN stage. | note |
| fact-force: editing an existing file before reading it in the session. | note |

Approved bypass of a guard block: `HARNESS_ACK_BYPASS=1`, with an audit note in
context. Guard does not catch subtle meaningful-looking loops or adversarial
string construction; Todo progress, user decision, and the server ruleset cover
that boundary.

## Env

Human runner knobs:

| Variable | Purpose | Default |
|---|---|---|
| `HARNESS_ALLOW_MAIN=1` | Legitimate release/hotfix/bootstrap commit on `main`; bypasses branch guard in pre-commit. | unset |
| `HARNESS_ACK_BYPASS=1` | User-approved bypass of a guard block; emits an audit note. | unset |
| `HARNESS_PROFILE` | Guard strictness: `minimal`, `standard`, `strict`. | `standard` |
| `HARNESS_DISABLED_CHECKS` | Disable targeted checks: `loops,entropy,lintconfig,design-note,fact-force,...`. | unset |
| `HARNESS_LOOP_THRESHOLD` | Shell loop threshold. | `5` |
| `HARNESS_TOOLLOOP_THRESHOLD` | File-tool loop threshold. | `12` |
| `HARNESS_SESSION_ID` / `HARNESS_PROJECT_DIR` | Guard state key when the runner did not provide one. | unset |
| `HARNESS_ROOT` | Root for `new-mockups.js` when scaffolding mockups. | repo root |
| `LEFTHOOK=0` | Skip lefthook for humans only; guard blocks agent use. | unset |

## Dropwheel Canary Inbox

Dropwheel at `C:\Users\poweruser\projects\csharp\dropwheel` is the canary
target for this harness.

When a report arrives under `inbox/dropwheel` or from a Dropwheel Codex thread,
use `$harness-triage` and follow `docs/dropwheel-harness-inbox.md`.

Routing rule:

- installer, doctor, harness syntax, guard, design gate, verify runner, or
  generated harness file failure caused by source harness behavior belongs in
  this repo;
- installer-created files that only need to be accepted/tracked in Dropwheel are
  `dropwheel-harness-update` and should be routed back to Dropwheel auto-fix;
- Dropwheel build/test failure after a green install and doctor belongs in
  Dropwheel unless the report proves a bad harness contract;
- unclear owner starts here as triage, then routes to the right project.

For valid harness bugs, reproduce against a disposable Dropwheel canary
worktree, fix the smallest harness behavior, add regression coverage, run
`node hooks\verify.js`, and rerun:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\poweruser\projects\csharp\dropwheel\scripts\harness-canary.ps1
```

The Dropwheel inbox automation is allowed to perform these harness fixes
automatically. If the main checkout is dirty, make code changes in an isolated
local worktree under `.codex\auto-fix-worktrees` and keep inbox bookkeeping in
the main checkout. After `node hooks\verify.js` and the Dropwheel canary pass,
create a local feature-branch commit with a Conventional Commit message. Do not
push, merge, release, reset, force-push, or bypass hooks without an explicit
user request.

## Color Team Review

When the user asks for Color Team Review, use `$color-team-review` instead of
expanding a long prompt inline. Keep the compact format: verdict, evidence-based
findings, what is good, priority fixes, minimal safe plan, useful tests, and
verdict-changing questions only.

For Dropwheel handoffs, read `docs/dropwheel-harness-inbox.md` and process
`inbox/dropwheel/review-handoff-*` files as first-class harness triage inputs,
even when the related canary report is green.
