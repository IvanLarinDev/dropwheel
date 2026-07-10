# Dropwheel Regression Mining Prompt

Look for missing regression coverage and repeated failure patterns in Dropwheel.

This task is different from normal product review: it mines recent evidence,
not the whole codebase.

## Parallel Worker Mode

When invoked by the orchestrator as a parallel discovery worker, this prompt is
read-only:

- do not create `inbox\auto-fix` items;
- do not create agents handoff files;
- do not move `_processed` items;
- do not edit code, run installers, commit, merge, push, or release;
- return structured candidate findings only.

For each candidate include `owner`, `severity`, `confidence`, `evidence`,
`impact`, `recommendation`, `testToAdd`, checked reports/commits, and a stable
`fingerprint`:

```text
<owner>|regression|<file-or-contract>|<missing-test-or-failure-mode>
```

The orchestrator owns dedupe and creates canonical inbox or handoff items after
parallel workers finish.

## Inputs

Newest first:

- `git status --short`
- `git log --oneline --decorate -20`
- `git diff --stat`
- `reports\harness-canary\**\report.json`
- `inbox\auto-fix\**`
- `tests\Dropwheel.Tests\**`

Ignore `_processed` items unless checking whether the same issue already has a
fix or a newer duplicate.

## Process

1. Identify candidate regressions:
   - product files changed without corresponding tests;
   - recurring canary or verify failures with the same signature;
   - code paths with high impact but no focused test for failure modes;
   - TODO/FIXME comments only when they point to runtime risk, data loss,
     compatibility, or silent failure.
2. For each candidate, look for concrete evidence in code, tests, reports, or a
   reproducible scenario.
3. Classify ownership:
   - `dropwheel`: product code or product test gap;
   - `agents-harness`: false positive/false negative or harness behavior;
   - `dropwheel-harness-update`: generated target harness files need to be
     accepted in Dropwheel.
4. In normal serialized mode, for confirmed or likely Dropwheel-owned P0/P1
   gaps, create an `inbox\auto-fix\auto-fix-*.md` and matching `.json` using
   schema `dropwheel-auto-fix/v1`.
5. In normal serialized mode, for true harness findings, use
   `.codex/automation-prompts/dropwheel-review-handoff.md`.
6. Do not edit product code directly. `Dropwheel Auto Fix` owns implementation.
7. If no actionable item exists, report:
   - which reports/commits/tests were checked;
   - why the candidates were rejected or deferred;
   - one residual risk if confidence is low.

## Quality Bar

Do not create an item just because a file changed. The item must say what could
break, why current tests would miss it, and what regression test would catch it.
