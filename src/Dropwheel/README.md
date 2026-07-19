# src/Dropwheel — application project

This is the C# project for Dropwheel, an overlay launcher for Windows. A
floating orb expands into a radial wheel of targets such as folders,
applications, and links. Drop files to copy or move them, drop text to save it
as a file, or route files into subfolders with sorter rules. The wheel opens
automatically when a dragged file approaches the orb.

## Structure

- `Models` — configuration, targets, and sorting rules: what the application
  stores.
- `Services` — all non-UI logic, including files, launching, input hooks, folder
  watching, sorting, and icons. See the README in that directory.
- `UI` — the WPF windows and wheel. The main `OverlayWindow` is split across
  several partial files.
- `App.xaml.cs` — the entry point, which creates the overlay and tray icon and
  installs input hooks.
- `app.manifest` — application metadata such as DPI awareness.

## Building and running

From the repository root on Windows with the .NET 10 SDK installed:

```powershell
dotnet restore Dropwheel.slnx --locked-mode
dotnet build Dropwheel.slnx --configuration Release --no-restore
dotnet run --project src/Dropwheel/Dropwheel.csproj --configuration Release --no-restore
dotnet test tests/Dropwheel.Tests/Dropwheel.Tests.csproj --configuration Release --no-restore
```

## Adding functionality

- Put data in `Models`, behavior in `Services`, and presentation in `UI`. Keep
  the layers separate.
- Use mockups for visual and animation changes; the project enforces a design
  gate.
- Keep testable logic pure and `internal`; tests access it through
  `InternalsVisibleTo`.

## Avoid

- Do not swallow exceptions silently. Log through `ErrorLog` or propagate them.
- Do not rename model properties without accounting for existing user
  `config.json` files.
- Do not put file, network, or process-launching logic in the `UI` layer.
