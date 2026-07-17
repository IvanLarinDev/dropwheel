# Contributing to Dropwheel

Thanks for your interest in contributing to Dropwheel! This document explains how
to propose changes so they can be reviewed and merged smoothly.

By participating in this project you agree to abide by our
[Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to contribute

- **Report a bug** — open a [bug report](https://github.com/IvanLarinDev/dropwheel/issues/new/choose).
- **Request a feature** — open a [feature request](https://github.com/IvanLarinDev/dropwheel/issues/new/choose).
- **Improve docs** — fixes to the README or other documentation are always welcome.
- **Submit code** — pick up an open issue or propose a change via a pull request.

## Before you start

For anything larger than a small fix, please open an issue first to discuss the
approach. This avoids duplicate work and makes review faster.

Development requires Windows 10/11 and the .NET 10 SDK. Dropwheel targets
`net10.0-windows` and uses WPF, WinForms, and Windows shell APIs, so other
platforms are not supported build hosts.

## Development workflow

1. **Fork** the repository and clone your fork.
2. **Create a branch** from `main` with a descriptive name, e.g.
   `feature/smart-routing` or `fix/shelf-overflow`.
3. **Make your changes** in focused, logically separated commits.
4. **Build and test** from the repository root:

   ```powershell
   dotnet restore Dropwheel.slnx
   dotnet build Dropwheel.slnx --configuration Release --no-restore
   dotnet test tests/Dropwheel.Tests/Dropwheel.Tests.csproj --configuration Release --no-build
   ```

   For changes to startup, packaging, or Windows integration, also exercise both
   release publish variants:

   ```powershell
   dotnet publish src/Dropwheel --configuration Release --output "$env:TEMP\dropwheel-fd"
   dotnet publish src/Dropwheel --configuration Release -r win-x64 --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true --output "$env:TEMP\dropwheel-sc"
   ```

   Exercise the affected Windows behavior manually when it cannot be covered by
   the xUnit suite, and describe those checks in the pull request.
5. **Push** the branch to your fork.
6. **Open a pull request** against `main` and fill in the PR template.

## Project conventions

- Keep code, comments, and log messages in English.
- Keep data in `Models`, non-UI logic in `Services`, and WPF code in `UI`.
- For UI changes, include before/after screenshots and describe manual
  verification. Keep `docs/demo/` aligned when wheel geometry, timing, colours,
  or behavior changes.

## Commit messages

Use Conventional Commits with a short imperative description, for example:

```
feat(sort): add fallback for unmatched files
fix(layout): correct wheel index calculation
docs(readme): clarify portable ZIP installation
```

Reference related issues where relevant (e.g. `Fixes #123`).

## Pull request guidelines

- Keep pull requests focused; one logical change per PR is easier to review.
- Fill out the pull request template completely.
- Link the issue your PR addresses.
- Describe what you changed and how you verified it.
- Be responsive to review feedback.

## Reporting security issues

Please do **not** report security vulnerabilities through public issues. See our
[Security Policy](SECURITY.md) for how to report them privately.

## Questions

If anything is unclear, open a
[GitHub Discussion](https://github.com/IvanLarinDev/dropwheel/discussions) and
we'll help you get started.

Thank you for helping make Dropwheel better!
