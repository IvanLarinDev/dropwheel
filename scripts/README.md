# scripts — maintenance scripts

This directory contains one script: `release.ps1`, the only entry point for
publishing a release. Do not create tags or GitHub releases manually; the
script performs every step in the required order and verifies the result.

## Publishing a release

Run the script from the repository root in Windows PowerShell (`pwsh` is not
installed on this machine).

You need `git`, the SDK selected by `global.json` (currently .NET SDK 10.0.302),
and an authenticated GitHub CLI session (`gh auth status`). The NuGet graph is
restored only from tracked lock files, while `verify-runtime-baseline.ps1`
checks the runtime baseline separately.

    ./scripts/release.ps1 -Bump minor -DryRun   # rehearse first
    ./scripts/release.ps1 -Bump minor           # then publish for real

`-Bump` accepts `patch`, `minor`, `major`, or an exact `X.Y.Z` version. Use
`patch` for cosmetic and demo changes and `minor` for new application features.

Push every commit before starting. The script builds a fresh `origin/main` in
an isolated temporary directory, not the current working tree.

## What the script does

The script checks tools and GitHub authentication, creates an isolated checkout
from `origin/main`, changes only the project version and `CHANGELOG.md`, runs the
tests and both framework-dependent and self-contained publishes, pushes the
release commit, waits for a green CI run, creates the `vX.Y.Z` tag, waits for the
GitHub release build, verifies its assets, and fast-forwards local `main` to the
release commit.

Every GitHub release contains **five required assets**: two portable ZIP files
(framework-dependent and self-contained), a provenance manifest, an SPDX SBOM,
and a checksum file:

- `Dropwheel-vX.Y.Z-win-x64.zip`;
- `Dropwheel-vX.Y.Z-win-x64-self-contained.zip`;
- `Dropwheel-vX.Y.Z-PROVENANCE.json` — the commit and tag, exact SDK, TFM, RID,
  runtime packs, and SHA-256 digests of both NuGet lock files;
- `Dropwheel-vX.Y.Z-SBOM.spdx.json` — the SPDX software bill of materials for
  the contents of both distributions;
- `Dropwheel-vX.Y.Z-SHA256SUMS.txt` — SHA-256 digests for both ZIP files, the
  provenance manifest, and the SBOM.

The Release workflow generates metadata before the checksum file. SBOM
component detection is limited to the production project in `src/Dropwheel`,
and the manifest passes standard validation with an empty package list rejected
before publication.

After publication, `release.ps1` downloads all five assets and runs
`verify-release-assets.ps1`. The verifier requires the exact filename set,
recomputes SHA-256 for all four content assets, and matches the provenance `tag`
and `commit` to the release. Any missing, duplicate, empty, corrupted, or stale
asset fails the release. The pipeline does not yet include an installer or code
signing.

## Resuming an interrupted release

Run the script again with the exact version already present on `main`:

    ./scripts/release.ps1 -Bump 0.20.0

The script finds the existing release commit and completes only the missing
steps, such as the tag, build, or verification. Network operations retry up to
three times, so a transient network failure no longer aborts the entire run.

## Prohibited actions

- Do not create `v*` tags or GitHub releases manually outside the script.
- Do not run two releases concurrently.
- Do not edit the top of `CHANGELOG.md` while a release is running; the script
  inserts the new section itself.
