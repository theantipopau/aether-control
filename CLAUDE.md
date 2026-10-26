# Aether Control — working notes for Claude

WinUI 3 / .NET 10 hardware monitor + control app (LibreHardwareMonitorLib). Runs **elevated**.
Projects: `src/AetherControl.{App,Core,Data,Services,Plugins.*}`, tests in `tests/AetherControl.Tests`.
Phase history and open items live in `ROADMAP.md` — add a phase entry for significant work.

## Build & test
- The App head needs Visual Studio's MSBuild **18+** (VS 2026) with the Windows application
  development workload (PRI tasks, XAML compiler) to resolve the .NET 10 SDK from `global.json`.
  This machine has VS2022 / MSBuild 17.14, so it can't build the head locally (checked 2026-10-26).
  Plain `dotnet build`/`dotnet publish` does not work for the head either: a publish skips the PRI
  step and ships without `AetherControl.pri`, and the app then dies at startup with
  "Cannot find a resource with the given key: AetherAccentBrush" (the 0.9.6 portable zip).
  Command: `msbuild src/AetherControl.App/AetherControl.App.csproj -restore -p:Configuration=Release -p:Platform=x64`
  (always `Platform=x64`; AnyCPU fails). CI's `app-build` job runs it and uploads `AetherControl-x64-Release`.
- Releases: bump `<Version>` in the App csproj, then push tag `vX.Y.Z` (must match).
  `.github/workflows/release.yml` builds, checks the `.pri` is present, builds the Inno Setup installer
  (`builder/AetherControl.iss`) and portable zip, and opens a **draft** GitHub release.
- `AetherControl.exe --exit` closes a running instance via the same ordered shutdown as tray → Exit
  (fans handed back to BIOS); the installer and uninstaller use it.
- Tests: `dotnet test tests/AetherControl.Tests` works normally. Keep the suite green before committing.
  CI runs the same command on push/PR (`.github/workflows/ci.yml`, windows-latest, Release).
- Hardware mapping tests use hand-written fakes in `tests/AetherControl.Tests/Fakes/FakeHardware.cs` —
  add real sensor names/values from live captures as fixtures rather than inventing them.

## Running the app
- The app is elevated, so a normal `Stop-Process` fails and a running instance locks the build output.
  Kill before rebuilding: `Start-Process taskkill.exe -ArgumentList '/IM','AetherControl.exe','/F' -Verb RunAs -Wait`
- Launch with `Start-Process <exe> -Verb RunAs`.
- Computer-use cannot click/type into it (UIPI blocks input to higher-integrity windows). Ask the user to
  exercise UI, then verify via logs.
- Smart App Control occasionally blocks a fresh build. Never try to bypass it; retry later.

## Evidence first
Diagnose from live evidence, not guesses; retract claims that turn out wrong.
- `%APPDATA%\Aether Control\unhandled-exceptions.log` — crashes swallowed by the global handler
  (a feature that "silently does nothing" usually has an entry here).
- `%APPDATA%\Aether Control\logs\aether-YYYY-MM-DD.log` — daily logs.
- `%APPDATA%\Aether Control\hardware-open-failed.log`, `superio-reopen.log` — hardware-open diagnostics.
- Crash dumps: `%LOCALAPPDATA%\CrashDumps\AetherControl.exe.*.dmp`; ILogger warnings also reach the
  Windows Application Event Log (source ".NET Runtime").
- Diagnostics page → "Export support bundle" zips capability report, latest snapshot and recent logs.

## Known gotchas
- **WinRT collections:** don't use LINQ / `foreach` on `DisplayArea.FindAll()` — it throws
  `InvalidCastException`. Use `Count` + indexer.
- **Super I/O has a single owner.** Never open LHM `Computer` from a second process/helper; concurrent
  access makes both read 0xFF. Any "reopen on bad state" recovery must verify reads before resuming
  (see `HardwareMonitorService.LooksPoisoned`, ROADMAP Phase 34).
- Global exception handler keeps the process alive, so UI failures can be invisible — check the log.
- Shell: the Bash tool mangles PowerShell `$` syntax; run PowerShell scripts with the PowerShell tool.

## Test rig
DISPLAY1 2560x1440 primary; DISPLAY2 768x1366 portrait at (2560,192) — Portrait Mode's target.
ASUS PRIME B650EM-A WIFI (NCT6701D), Ryzen 7 9800X3D, RX 9070 XT + iGPU. Several fan headers are
unpopulated and read 0 RPM.

## Conventions
- Match surrounding code: explanatory doc comments that record *why* (including the evidence behind a fix).
- Best-effort I/O paths must never crash the app — catch and surface a status message.
- Commit messages describe the user-visible symptom and root cause.
