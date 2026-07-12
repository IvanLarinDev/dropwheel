# Releasing Dropwheel

The release entrypoint is [`scripts/release.ps1`](../scripts/release.ps1). It
prepares changes in an isolated worktree from the latest `origin/main`, runs the
same test and publish checks as CI, pushes a fast-forward release commit, waits
for CI, creates an annotated tag, and verifies the GitHub Release assets.

## Usage

Run a complete patch release:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Bump patch
```

`-Bump` accepts `patch`, `minor`, `major`, or an exact stable version such as
`0.16.0`. Preview the generated version and changelog without committing or
publishing anything:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Bump minor -DryRun
```

If a release stopped after its commit reached `main`, resume it by passing the
exact version already present on `main`:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Bump 0.16.0
```

The resume path locates the exact `chore(release): Dropwheel X.Y.Z` commit. It
never retags the current tip of `main`, moves an existing tag, force-pushes, or
deletes a published release.

## Release invariants

- `src/Dropwheel/Dropwheel.csproj` is the version source of truth.
- The current project version must have a matching tag reachable from `main`.
- Only `CHANGELOG.md` and the project file may change in the release commit.
- Both tests and framework-dependent/self-contained publish smoke checks must
  pass before the commit is pushed.
- The release tag is created only after CI succeeds for the release commit.
- The tag must point to a commit contained in `origin/main`.
- A release is complete only after both ZIP files and the SHA-256 checksum file
  are present and non-empty.
- Failed release workflows are rerun against the existing immutable tag.

## Agent prompt

Use this as the task prompt for a coding agent:

```text
Release Dropwheel with BUMP=<patch|minor|major|X.Y.Z>.

Read docs/RELEASING.md, then use scripts/release.ps1 as the only release
entrypoint. Do not reproduce its git/release operations manually. First run it
with -DryRun. If the dry run succeeds, run it again without -DryRun using the
same BUMP. Prefix shell commands with `rtk` as required by the repository.

If the script reports that the release commit is already on main, resume using
that exact X.Y.Z version. Never force-push, move/delete a tag, bypass failed CI,
or include unrelated working-tree files. Do not report success until the script
has verified the GitHub Release URL and all three non-empty assets.

Return the old/new versions, release commit SHA, tag, CI and Release workflow
results, release URL, and asset names/sizes. On failure, return the failed state
and the exact safe resume command; do not improvise destructive recovery.
```

## Recovery

The release is intentionally a resumable state machine rather than a single
transaction:

1. Before the main push, failure has no remote side effects.
2. After the main push but before the tag, rerun with the exact new version.
3. After the tag, the script reuses the tag and reruns or dispatches
   `.github/workflows/release.yml` if needed.
4. A tag pointing at a different commit is a hard stop requiring manual review.

`workflow_dispatch` is also available for repairing a tag-triggered release:

```powershell
rtk gh workflow run release.yml --ref v0.16.0 -f tag=v0.16.0
```

This repair path rebuilds from the existing tag and does not move it.
