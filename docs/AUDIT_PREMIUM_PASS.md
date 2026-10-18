# Aether Control — Premium Pass Gate 0 Audit & Baseline (2026-10-18)

No code changes in this gate. Read-only audit against the live repo at `main` (`1d84ec5`).

## 1. Dependency table (every PackageReference, project, purpose)

### src/AetherControl.App (net8.0-windows10.0.19041.0, unpackaged WinUI 3)
| Package | Version | Purpose |
|---|---|---|
| Microsoft.WindowsAppSDK | 1.5.240627000 | WinUI 3 runtime + Mica/NavigationView/AppNotification |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | XAML resource indexing (PRI) at build |
| CommunityToolkit.Mvvm | 8.3.2 | ObservableObject / RelayCommand in ViewModels |
| Microsoft.Extensions.Hosting | 8.0.1 | DI container (`App.Services`) |
| H.NotifyIcon.WinUI | 2.1.3 | Tray icon + tooltip |

### src/AetherControl.Services (net8.0-windows)
| Package | Version | Purpose |
|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | Sensor engine (single shared `Computer` session) |
| System.Management | 10.0.2 | WMI (chipset INF, disk, motherboard queries) — must stay in lockstep with LHM's own nuspec (see §3) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | DI abstractions |
| Microsoft.Extensions.Logging.Abstractions | 8.0.2 | Logging abstractions |
| Microsoft.Extensions.Hosting.Abstractions | 8.0.1 | Hosting abstractions |
| Microsoft.Extensions.Http | 8.0.1 | HttpClient factory (update checks, external IP) |
| HidSharp | 2.6.4 | Direct-HID Corsair RGB control |
| System.Diagnostics.PerformanceCounter | 8.0.1 | GPU-engine performance counters (Task-Manager-matching GPU%) |

### src/AetherControl.Data (net8.0)
| Package | Version | Purpose |
|---|---|---|
| Microsoft.Data.Sqlite | 8.0.8 | Settings/history SQLite store (WAL mode) |

### tests/AetherControl.Tests (net8.0-windows)
| Package | Version | Purpose |
|---|---|---|
| Microsoft.NET.Test.Sdk | 17.8.0 | Test host |
| xunit / xunit.runner.visualstudio | 2.5.3 | Test framework |
| coverlet.collector | 6.0.0 | Coverage |

Pure libraries with no packages: `AetherControl.Core` (net8.0), `AetherControl.Plugins.Abstractions` (net8.0), `AetherControl.Plugins.Sample` (net8.0).

## 2. Platform table

| Item | Value | Source |
|---|---|---|
| SDK pin (`global.json`) | 8.0.400, rollForward latestFeature | `global.json` |
| Local SDKs installed | 8.0.422, 9.0.318, 10.0.401 | `dotnet --list-sdks` |
| `Directory.Build.props` | LangVersion latest, Nullable + ImplicitUsings enable; NO central package management, NO Version/Authors centralisation | `Directory.Build.props` |
| App TFM | net8.0-windows10.0.19041.0, MinVersion 10.0.17763.0 | `AetherControl.App.csproj` |
| App packaging | `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, unpackaged standalone .exe | same |
| Services TFM | net8.0-windows | `AetherControl.Services.csproj` |
| Core/Data/Plugins TFM | net8.0 | their csproj files |
| App version | 0.9.5 (single source in App csproj) | `AetherControl.App.csproj` |
| CI | `dotnet test tests/... -c Release` + experimental `app-build` job (VS MSBuild Release/x64, `continue-on-error: true`) | `.github/workflows/ci.yml` |
| LICENSE | **None** — repo root has no LICENSE file (README: "All rights reserved … no license granted") | `ls LICENSE*` → absent |
| `.github/` | Only `workflows/ci.yml` — no issue templates, no Dependabot config | dir listing |

Support-window context (verified via web, Oct 2026): .NET 8 LTS ends **10 Nov 2026**; .NET 10 LTS supported to Nov 2028. Windows App SDK latest stable **2.5.1** (16 Sep 2026); 1.5 servicing ended Feb 2025, 1.8 ends Sep 2026 — so the pinned **1.5.240627000 is out of support**. LHM stable is **0.9.6** (already pinned — correct; 0.9.7 is prerelease only).

## 3. LibreHardwareMonitorLib: PawnIO or WinRing0?

**Answer: PawnIO.** Evidence:
- Upstream merged "Replace WinRing0 with PawnIO" (PR #1857) on 16 Sep 2025; the v0.9.6 release notes mention PawnIO module updates and a PawnIO installer fix. (https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/pull/1857, https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6)
- Local nuspec for 0.9.6 confirms MPL-2.0 licence and the `System.Management 10.0.2` dependency the csproj comment warns about (checked `~/.nuget/packages/librehardwaremonitorlib/0.9.6/librehardwaremonitorlib.nuspec`).
- No WinRing0/WinRing0x64 driver ships in the 0.9.6 package runtimes (win-x64/arm64/x86 only).

**No Gate 1 driver fix needed** — the Defender-flagged WinRing0 path is already gone.

## 4. Theme resource inventory

Token files: `Themes/Colors.xaml` (role tokens: Surface*/Text*/Border*/Status* + `AetherAccentColor`/`AetherAccentBrush` + SystemAccentColor family + legacy `Aether*` aliases), `Themes/Styles.xaml` (spacing scale, radii, CardBorder/Hero/Inset styles, full type scale incl. `FontMono` = Cascadia Mono→Consolas, `StatValueTextStyle` tabular), `Themes/PortraitColors.xaml` (aliases Portrait keys to shared tokens — no separate palette). Runtime accent: `Theming/AccentPalette.cs` mutates the shared `AetherAccentBrush` in place (custom controls update live) + rewrites SystemAccentColor family (stock controls pick up on restart). Forced `RequestedTheme="Dark"` in `App.xaml` + `MicaBackdrop{BaseAlt}` in `MainWindow`.

Hard-coded colour literals found in code (not resources):
- `Controls/RadialGauge.xaml.cs:160-161` — Warm/Hot arc gradient pairs (`Color.FromArgb(255,0xF5,0xC8,0x6B)` etc.)
- `Controls/PortraitRing.xaml.cs:127` — lighten helper (structural, operates on bound brush)
- `Controls/PortraitSparkline.xaml.cs:49-50` — area-fill alpha fade (`FromArgb(90,…)` / `(0,…)`)
- `Controls/HoverBorderEffect.cs` — reads `AetherBorderColor`/`AetherAccentColor` resources (correct) but `Attach` is the only hover path
- `MainWindow.xaml.cs:151-153`, `Views/PortraitWindow.xaml.cs:211-214` — title-bar caption button colours (`FromArgb(30/50,255,255,255)`, muted foreground) — title-bar API takes Colors, not brushes, so these are structural
- `Views/RgbPage.xaml:82` — swatch `Background="#00E5FF"` (a literal colour *sample*, correct as-is)
- `Themes/PortraitColors.xaml` — `PortraitAmdBrandBrush #ED1C24`, `Nvidia #76B900`, `Intel #0071C5` (vendor logo colours, intentionally not theme tokens)
- Non-code: `Data/Migrations/001_Initial.sql` default `'#00E5FF'` (seed value, not UI), `docs/index.html` landing-page CSS (web, not app)

Font sizes/margins in Views: largely tokenised (PagePadding, CardPadding, TelemetryLarge/Secondary, StatValue, LabelTextStyle). No new audit of every View file line-by-line — Gate 2's "zero literals" pass will confirm by search.

## 5. Animation inventory

- `Controls/NumberTween.cs` — thin WinUI adapter over pure `Core/Animation/TweenState`; driven by `CompositionTarget.Rendering`; duration = `Motion.ValueChange` (220 ms); snaps when `Motion.AnimationsEnabled` false; exceptions unsubscribe-then-rethrow so one broken card can't blank the app. Used by: `MetricCard` (value + progress), `RadialGauge`.
- `Controls/HoverBorderEffect.cs` — border-brighten ColorAnimation (140 ms, QuadraticEaseOut); snaps when animations off; per-instance brush so one card's hover can't flash the page. Used by: `MetricCard`, `HoverCard`.
- `Theming/Motion.cs` — central durations (Hover 140, ValueChange 220, PageTransition 200, ModeChange 340 ms) + cached `UISettings.AnimationsEnabled` with live-update handler. `MainWindow` drops the page slide entirely when off.
- `MainWindow` title-bar accent-fade, `PortraitSparkline` area fill, gauge needle dot — no independent Storyboards found in app code; the only `Storyboard`/`ColorAnimation` is inside `HoverBorderEffect`.
- Live-value path is safe: all gauge/card animation goes through `EmaSmoother` (alpha 0.15) + `MedianFilter` upstream and `NumberTween` downstream — never raw sensor values (Phases 20–21).

## 6. Baseline perf (method + numbers)

**Could not measure live in this gate.** The app runs elevated; this session cannot launch/foreground it (UIPI) and no 60 s Task Manager / dotnet-counters capture was taken. No numbers are invented here. Gate 3's exit criteria require re-measuring idle CPU/RAM against whatever baseline Matt captures on his rig — suggested method: Task Manager Details (CPU %, working set) over 60 s, Dashboard open, then Portrait Mode open, machine otherwise idle.

## 7. Baseline screenshots

`docs/screenshots/` already holds live captures committed in `c58ed59`: `overview.png`, `performance.png`, `settings.png` (+ README). These ARE the Gate 0 baseline — no new captures taken (elevated window cannot be screenshotted from this session). Per-page coverage beyond those three does not exist yet; Gate 7's `--demo` mode is the durable fix.

## 8. Build + tests

`dotnet test tests/AetherControl.Tests -c Release` (18 Oct 2026): **242/242 passed, 0 failed, exit 0.** App head not built here (known PRI-task/MSBuild gap — see CLAUDE.md; covered by CI's `app-build` experiment job).

## 9. Notable deviations from the prompt's assumptions (recorded, not fixed — Gate 0 changes nothing)

- Prompt says "no `.github/` visible" — it exists (only `workflows/ci.yml`). Prompt says "no screenshots" — three live screenshots exist. Prompt says continue numbering "after Phase 21" — repo is at **Phase 52**; new phases continue after 52.
- Token/motion systems the prompt describes as missing largely **exist already** (Colors/Styles tokens, AccentPalette, Motion, NumberTween, HoverBorderEffect, Mica BaseAlt, EmaSmoother+MedianFilter). Later gates should extend, not re-create.
- `AetherControl.FanHelper` (prompt §0.4 read-only list) **no longer exists** — removed in an earlier phase after live A/B testing; fan RPM reads from the single shared LHM session.
- `docs/ROADMAP.md` is the vision companion, not a stale fork (Phase 49) — leave alone.
- LHM is already 0.9.6/PawnIO — Gate 1's "first fix" is not needed.
- Known open items carried forward: `Motion.cs` CA1416 + NETSDK1206 notice; stock WinUI controls need restart for accent change; OpenRGB mode-switch packet unverified live; fan preset-picker UI unwired (engine tested); CI `app-build` job still experimental (`continue-on-error`).

## 10. Gate 0 exit criteria

- [x] This audit doc committed (pending — next commit)
- [x] Zero source changes (this gate touched no `src/`/`tests/` files)
- [x] Test count recorded: 242/242
