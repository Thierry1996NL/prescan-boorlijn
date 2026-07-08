# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Borevexa is a native Windows (WPF/.NET 8) desktop application that produces HDD (horizontal directional drilling)
prescan feasibility reports. It replaced an earlier Next.js/React web prototype of the same product; this native
app is now the sole implementation. The app is a linear, numbered-step wizard (stap 0–13) covering project intake,
GIS import (BGT/BAG/KLIC), boreline drawing, surface analysis, subsurface (BRO) analysis, cross-section/profile,
machine placement, and final PDF/HTML report export — all backed by a local SQLite database, with no server or
online backend.

## Commands

Build and run (from repo root, i.e. this `Borevexa/` folder):

```powershell
dotnet build Borevexa.sln -c Debug
dotnet run --project .\Borevexa.App\Borevexa.App.csproj
```

Directly launching the built `.exe` from the shared artifacts output can be blocked by Windows ("Toegang
geweigerd") because it is an unsigned, low-prevalence binary. Prefer running via the dotnet host, which is not
subject to that block:

```powershell
dotnet "C:\Users\ThierryPapenhuijzen\Borevexa\artifacts\bin\Borevexa.App\debug\Borevexa.App.dll"
```

There are two verification tools in `tools/`, run via `dotnet <built dll>` after `dotnet build`:

- **`Borevexa.GisRegression`** — parses real BGT/BAG/KLIC/DXF import files and checks feature counts and geometry
  types against known-good baselines. Takes an optional import-folder argument; defaults to
  `%USERPROFILE%\OneDrive - Inpark\Documenten\Borevexa Rootmap\Importbestanden`. Pass `--strict` to fail on any
  deviation. Run this after touching any GIS parser (`GisBgtKadasterParserService`, `GisImklParserService`,
  `GisDxfParserService`, `GisFeatureParserService`).
- **`Borevexa.ReportMapContract`** — a static contract check between the C# side and the MapLibre map (see
  Architecture below). Takes an optional repo-root argument (auto-detected via `Borevexa.sln` otherwise). Verifies
  every message type sent C#→map has a handler in `step3-map.html` and vice versa, plus a handful of source-level
  invariants about the report map capture flow. Run this after changing `step3-map.html` or any
  `SendMapMessage`/`case "...":` handling in the app.

There is no separate unit test project; these two tools are the project's test suite.

Build output is redirected outside the repo/OneDrive via `Directory.Build.props` (`ArtifactsPath`) to
`C:\Users\ThierryPapenhuijzen\Borevexa\artifacts\bin\<Project>\<config>\` — this was done deliberately because
OneDrive syncing `bin`/`obj` caused file locks and slow builds. Don't "fix" this by moving build output back
under the repo.

## Architecture

### Solution layout

- **`Borevexa.App`** — the WPF UI and all application logic. One `MainWindow` class split across ~20
  `MainWindow.*.cs` partial-class files by domain (e.g. `MainWindow.BoreTrace.cs`, `MainWindow.SurfaceAnalysis.cs`,
  `MainWindow.BroUnderground.cs`, `MainWindow.MapReportPipeline.cs`, `MainWindow.ReportBuilders.cs`). `MainWindow.
  xaml.cs` itself still holds shared state (~200 fields), startup/window-lifecycle code, workspace/step navigation
  orchestration, and generic helpers used across domains — it is intentionally not split further than this file-
  per-domain granularity. When adding a feature, look for the existing `MainWindow.<Domain>.cs` file for that step
  before adding to `MainWindow.xaml.cs`.
- **`Borevexa.Core`** — SQLite persistence (`ProjectRepository`, `LocalDatabase`), the report/step catalogs
  (`WorkflowCatalog`, `StepReportCatalog`, `ReportContractCatalog`), domain models, and `AppLog` (see Logging
  below). Has no WPF dependency.
- **`Borevexa.Geo`** / **`Borevexa.Cad`** — thin, currently minimal projects reserved for future GDAL/Mapsui and
  DXF/AutoCAD work (see README "Volgende technische stappen").
- **`tools/Borevexa.GisRegression`** and **`tools/Borevexa.ReportMapContract`** — console apps, see Commands above.

### Local storage — no server

Everything lives under `%LOCALAPPDATA%\Borevexa\`: the SQLite database (`borevexa-prescan.sqlite`), imported
project files (`ProjectFiles`), generated report map captures (`ReportLiveMaps`, `ReportLocks`), exports
(`Exports`), and logs (`Logs`, `map-debug.log`). `ProjectRepository` caches projects/step-data in memory
(`_stepDataCache`, `_projectsCache`) to avoid redundant SQLite round-trips — invalidate/update these caches
rather than bypassing them when adding new read/write paths.

### Step/workflow model

Each numbered step (`PrescanStep`) has substeps; `WorkflowCatalog` and `StepReportCatalog` define what exists
per step, and `ReportContractCatalog`/`ReportContract` define what data each step must produce for the final
report to be considered complete (`ReportQualityService` checks this). Steps that show a GIS map are registered
in `GisMapWorkspaceRegistry` with a `GisMapWorkspaceDefinition` describing per-step map behavior (whether it
needs scoped/substep-specific state, whether it supports being "locked" for the report, whether it captures
live, whether it sends the boreline trace before capture, etc.) — **new map-bearing steps should be added here
as configuration, not with new `if (stepNumber == N)` branches**, which is exactly the pattern that used to
cause per-step inconsistencies (a fix for one step silently not applying to another).

### The GIS map and its C#↔JavaScript contract

The map is MapLibre GL JS running inside a `WebView2` control, loaded from `Assets/MapLibre/step3-map.html`
(despite the "step3" name, this one file is reused for every step that shows a map — steps 3 through the 3D
step). All map interaction crosses a WebView2 message boundary:

- C# → map: `SendMapMessage(json)` sends `{"type": "...", ...}`; the HTML side dispatches on
  `message.type === "..."` inside its `window.chrome.webview` message listener.
- Map → C#: the HTML calls `send({type: "...", ...})`; the C# side receives it in the WebView2
  `WebMessageReceived` handler and dispatches via a `switch`/`case "...":` on the message type.

This contract is **not type-checked by the compiler** (it's JSON strings on both ends), which is why
`Borevexa.ReportMapContract` exists as an automated check — it scans both files for every message type used and
fails if either direction is missing a handler. Run it after touching the message protocol in either direction.

### Report map capture pipeline

Producing the map image that appears in the exported report is deliberately not a plain screenshot: `MainWindow.
MapReportPipeline.cs` and `Services/ReportPreviewService.cs`/`ReportMapCaptureCoordinator.cs` implement a
capture flow that (a) asks the map to settle (tiles loaded, camera not mid-animation) before calling
`CorewebView2.CapturePreviewAsync`, (b) validates the resulting PNG isn't blank/near-solid-color before
accepting it (`ValidateMapCapture`), and (c) on "Opslaan voor rapportage" freezes that image to a per-step file
so later live captures don't silently replace an intentionally locked report image. Captures only fire on
actual reporting moments (opening a preview, exporting, locking) — not on every pan/zoom, which was a real
performance problem earlier. Follow this pattern (settle → validate → don't fire on cheap interactions) rather
than reintroducing ad hoc screenshotting when adding new map-driven report content.

### Surface (BGT) analysis

`MainWindow.SurfaceAnalysis.cs` computes which BGT surface (verharding/groen/water/etc.) the boreline crosses
by an exact point-in-polygon test against parsed BGT geometry (`GisBgtKadasterParserService`) — not by sampling
rendered map pixels, which was tried first and was inaccurate. Two important, non-obvious invariants here: the
analysis walks the *actual boreline length*, not the (possibly shorter) cross-section/profile axis length; and
`GisBgtKadasterParserService` filters out BGT features that have an `eindRegistratie` (an ended/superseded
version in the BGT mutation history) — including those double-counts obsolete geometry on top of current
geometry and produces wrong classifications.

### Logging

`AppLog` (`Borevexa.Core.Services.AppLog`) is the only logging path — `AppLog.Swallowed(exception)` in a catch
block, or `AppLog.Warn(message)`. It writes to `%LOCALAPPDATA%\Borevexa\Logs\app.log`, throttles repeated
identical errors to once per 30 seconds, and rotates at 5 MB. `App.xaml.cs` wires global handlers
(`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) so an
unhandled exception is logged and the app keeps running instead of silently dying. Don't add ad hoc
`Trace`/`Debug.WriteLine`/empty-catch logging — use `AppLog`.
