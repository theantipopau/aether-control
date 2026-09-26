# Aether Control — working notes for Claude

WinUI 3 / .NET 8 hardware monitor + control app (LibreHardwareMonitorLib). Runs **elevated**.
Projects: `src/AetherControl.{App,Core,Data,Services,Plugins.*}`, tests in `tests/AetherControl.Tests`.
Phase history and open items live in `ROADMAP.md` — add a phase entry for significant work.

## Build & test
- `dotnet build` **cannot build the App project** on this machine (`Microsoft.Build.Packaging.Pri.Tasks.dll`
  missing from all SDKs, MSB4062). Use Visual Studio's MSBuild, and always pass `Platform=x64` (AnyCPU fails):
  ```bash
  "/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" src/AetherControl.App/AetherControl.App.csproj -restore -p:Configuration=Debug -p:Platform=x64 -v:minimal
  ```
- Tests: `dotnet test tests/AetherControl.Tests` works normally. Keep the suite green before committing.
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
