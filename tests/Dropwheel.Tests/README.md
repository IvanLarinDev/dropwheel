# tests/Dropwheel.Tests — application tests

This directory contains all automated Dropwheel tests (xUnit). They exercise
pure service and model logic without opening windows, including sorting, hotkey
parsing, configuration behavior, and UI text formatting.

## Running the tests

From the repository root:

```powershell
dotnet restore Dropwheel.slnx --locked-mode
dotnet test tests/Dropwheel.Tests/Dropwheel.Tests.csproj --configuration Release --no-restore
```

The full suite completes in seconds. Run all tests before declaring any task
complete; a partial run does not count.

## Structure

- Each file covers one concern, such as `SortServiceTests.cs`,
  `HotkeyServiceTests.cs`, or `TargetStoreReloadTests.cs`. Classes are flat and
  `sealed`, without base classes or elaborate fixtures.
- The application exposes internals through `InternalsVisibleTo`, so testable
  application logic should be pure and `internal static`.
- Tests that touch the static `TargetStore` use
  `[Collection("TargetStoreState")]`, which disables parallel execution between
  them to protect shared state. `TargetStore.DirOverride` redirects the
  configuration directory to a temporary location that `Dispose` removes.

## Guidelines

- Add test files alongside the existing files in the same style: a flat
  `sealed` class with sentence-like test names such as
  `Broken_file_keeps_current_settings_and_reports_the_error`.
- Do not leave temporary files or directories behind; remove them in `Dispose`.
- Do not test WPF windows directly. Extract behavior into a pure `internal`
  function and test that function.
