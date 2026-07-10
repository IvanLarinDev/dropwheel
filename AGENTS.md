# AGENTS.md - Dev Loop

Every agent pass through code follows one loop. Anything that can be checked by
code belongs in hooks; this document keeps the behavior that hooks cannot fully
enforce.

## Loop

```text
1. EXPLORE          - read the codebase, patterns, and risks
2. PLAN             - plan -> user approval
2.5 DESIGN (GUI)    - >=4 mockups -> user approval -> APPROVED file
3. IMPLEMENT+TEST   - code and tests together; edge cases become tests
4. VERIFY           - node hooks/verify.js + read output + git diff self-review
   - failure/new warnings -> return to step 3
   - green              -> step 5
5. COMMIT on branch -> PR, never directly on main
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

**2.5 DESIGN (GUI).** GUI work matching `harness.config.json -> ui.globs` is
designed before code. New GUI work needs at least four stylistically distinct
mockups from `node hooks/new-mockups.js <feature>`, user selection, and
`design/mockups/<feature>/APPROVED`. GUI changes also need a mockup for the new
state. `hooks/design-gate.js` passes UI changes only when the approved set is
touched in the same branch diff; to reuse an old set, append a date/branch line
to its `APPROVED` file.

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

**6. REPORT.** Report what changed, what was verified with commands and results,
what remains, and how the user can test manually.

**7. USER DECISION.** The loop is complete only when the user accepts the result.

## Release Flow

Run this only on explicit request and after merge to `main`.

| Step | Action | Gate |
|---|---|---|
| R1 | Start from a clean worktree at `origin/main`; `node hooks/doctor.js` is green; read current version with `git describe --tags --abbrev=0`. | |
| R2 | Derive SemVer from Conventional Commits: first `cog bump --auto --dry-run`, then `cog bump --auto --annotated "vX.Y.Z"` to create an annotated tag and changelog. | Show tag, diff, and notes; wait for approval. |
| R2.5 | `node hooks/release-preflight.js --tag vX.Y.Z --base origin/main`: clean tree, tag points to HEAD, remote tag absent, project/package versions match the tag. | |
| R3 | `git push origin <branch> && git push origin vX.Y.Z`. | Only after an explicit yes. |
| R4 | If release workflow exists: `gh run watch`; release workflow is green and has no skipped steps. | |
| R5 | `gh release view vX.Y.Z`: release is published and artifacts exist. | |
| R6 | Download artifact, smoke-test it, and verify binary version equals the tag. | |
| R7 | Know rollback: before publication, recreate the tag; after publication, use `gh release delete` plus revert with approval. | |

Hotfix: branch from the previous tag, fix, PR to `main`, then tag through R2-R6.
A legitimate release commit on `main` can use `HARNESS_ALLOW_MAIN=1 git commit ...`.

`release-preflight.js` intentionally fails if the tag/changelog are ready but a
project manifest still reports an old version. If no version manifest exists, it
warns; R6 still verifies the binary version.

After `gh pr merge --delete-branch`, GitHub can merge the PR server-side even if
the local post-merge pull/rebase fails because of a dirty worktree. Verify with
`gh pr view <num> --json state,mergedAt,mergeCommit`; if `state` is `MERGED`, the
merge happened. Sync locally only from a clean tree with `git fetch origin` and
`git merge --ff-only origin/main`. For releases, prefer a new clean worktree from
`origin/main`.

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
