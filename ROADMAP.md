# Aether Control — Build Roadmap / Checklist

Source of truth for progress on this build. Updated as work lands.

**Jump to:** [Phases 1-9 (build history)](#phase-1--solution-skeleton) · [Phases 10-14 (forward plan)](#phase-10--flicker-root-cause-for-real)

## Reference projects (Matt's, reused — not duplicated)
- **Portrait Stats** (`E:\Portrait Stats`) — WPF portrait monitor app. Ported
  directly into Aether Control: hardware sensor name-matching logic and the
  fan-RPM-needs-its-own-process lesson (`HardwareSnapshotMapper` /
  `FanRpmProbeService`); `FanLabelStore` (user-assigned fan channel names,
  verbatim port); `ProcessRankerService` (CPU/GPU top-process ranking,
  verbatim port, now backing Dashboard's "Top Processes" section).
- **OmenCore** (`E:\Omen`) — HP Omen control suite. `Services/Corsair/CorsairHidDirect.cs`
  ported into `CorsairHidDirectService` — direct-HID Corsair keyboard/mouse
  RGB control (no iCUE, no OpenRGB), including the full known-product table
  and per-PID HID report layouts learned empirically in OmenCore. This is
  Matt's own previously-shipped code, not a derivative of OpenRGB's GPL
  codebase, so none of Phase 12's licensing caution applies to it. Also
  referenced for the privilege-separation / elevated-worker-process pattern;
  OmenCore's `Services/SystemOptimizer/Optimizations/*` (Network/Power/
  Service/Storage/VisualEffects/InputOptimizer) is a large, not-yet-mined
  vein for future Optimisation Centre expansion.
- **PulseLan** (`E:\Network Traffic`) — Tauri/Rust network monitor. Referenced
  conceptually for network-metric UX; not code-portable (different language).
- **img/Logo.png, img/Icon.png** — supplied brand assets. `Icon.png` → tray +
  exe icon (converted to `.ico`), `Logo.png` → README/docs header.

## Phase 1 — Solution skeleton
- [x] `.sln`, `Directory.Build.props`, `global.json`
- [x] `AetherControl.Core` (models, enums, interfaces, events) — builds clean
- [x] `AetherControl.Plugins.Abstractions` — builds clean
- [x] `AetherControl.Data` (SQLite schema + migrations + repositories) — builds clean
- [x] `AetherControl.Services` project scaffolded (LibreHardwareMonitorLib, System.Management)

## Phase 2 — Hardware monitoring (in progress)
- [x] LibreHardwareMonitor `Computer`/`IVisitor` wrapper
- [x] CPU/GPU/RAM sensor mapping — **ported Portrait Stats' proven fallback
      name lists** (Tctl/Tdie, Core Max, Cores (Average), GPU Memory Total, etc.)
      and best-GPU-by-VRAM selection instead of naive FirstOrDefault
- [x] Fan RPM: **adopted Portrait Stats' separate-process pattern**
      (`AetherControl.FanHelper` console app, motherboard-only `Computer`,
      launched fresh every ~3s) instead of reading Fan sensors off the
      long-lived instance, since ASUS's `AsusFanControlService` reclaims the
      Super I/O ports after the first read and sticks RPM at 0 otherwise
- [x] Storage: **replaced index-guess pairing with WMI associator queries**
      (`Win32_DiskDrive` → `Win32_DiskDriveToDiskPartition` → `Win32_LogicalDiskToPartition`)
      for correct per-drive capacity/free space on multi-drive systems
- [x] Network monitor service (throughput, latency, external IP)
- [ ] Live validation against real hardware (can't run WinUI3/elevated probes
      in this sandbox — needs a pass on Matt's machine)

## Phase 3 — Data & settings
- [x] SQLite schema (settings, tray prefs, portrait layouts, history, RGB
      presets, startup overrides, optimisation log, plugins)
- [x] SettingsService, HistoryService with daily/weekly/monthly rollup queries
- [x] Real xUnit tests: `HistoryRepositoryTests` (insert/query/purge round-trip
      against a real temp SQLite file) and `TrayReadoutFormatterTests` — all
      4 tests pass (`dotnet test`)

## Phase 4 — Optimisation Centre
- [x] Clear standby memory (`NtSetSystemInformation`, real P/Invoke, requires elevation)
- [x] Startup analysis (`Win32_StartupCommand` + `StartupApproved` registry toggle — same mechanism Task Manager uses)
- [x] Temp file / cache cleanup with safe-location allowlist
- [x] Restore point creation (`SystemRestore` WMI class)
- [x] Gaming profile (Game Mode registry flag, power plan switch, foreground process priority boost) — fully reversible
- [ ] Memory usage analysis / suggestions surface in UI

## Phase 5 — RGB integration
- [x] OpenRGB SDK network protocol client (controller enumeration, solid colour, brightness)
- [x] Mode/effect switching — implemented but **unverified against a live OpenRGB
      server** (no OpenRGB instance in this sandbox); flagged in code, needs a
      real-hardware pass
- [x] Aura Sync detection (registry uninstall-key scan) + launch-only integration (no reimplementation, per spec)
- [x] Firmware/Driver Centre service (WMI current-version detection, links only, no auto-install)
- [x] Plugin manager (collectible AssemblyLoadContext per plugin)
- [x] `AetherControl.Services` — full solution build green (`dotnet build`, 0 errors)

## Phase 6 — App shell (WinUI 3)
- [x] DI/host wiring (`Microsoft.Extensions.Hosting`)
- [x] Dashboard page + ViewModel (CPU/GPU/RAM/Storage/Motherboard/Network cards)
- [x] System tray integration (H.NotifyIcon.WinUI) with app icon from `img/Icon.png` (→ `Assets/AppIcon.ico`, multi-res)
- [x] **Portrait Mode — rebuilt as a real port of Portrait Stats, not a stub.**
      Matt's exact feedback: "the portrait mode is not working... currently
      you cant move it and what it displays is terrible." Both were real:
      - **Couldn't move it**: the old code called
        `presenter.SetBorderAndTitleBar(hasBorder:false, hasTitleBar:false)`
        and nothing else — no system title bar *and* no drag mechanism of any
        kind. Fixed with a manual drag implementation (`GetCursorPos` +
        `AppWindow.Move` in `PortraitWindow.xaml.cs`, wired to the header's
        pointer events) — reliable regardless of presenter chrome flags,
        rather than depending on `SetTitleBar`/DWM interaction that's harder
        to verify without a live drag test.
      - **"terrible" display**: the old window was 6 generic `MetricCard`s in
        a plain vertical stack at 280x640. Replaced with a faithful port of
        Portrait Stats' actual `MainWindow.xaml` layout at its real 768x1366
        size: header with clock, CPU section (usage number + ring gauge +
        sparkline history + temp/clock/power tiles), GPU section (same, plus
        wattage/core-clock/mem-clock/VRAM), a renamable fan RPM list, RAM +
        drive temp, an FPS tile (RTSS shared-memory read, ported from
        Portrait Stats' `RtssFpsSource` — no RTSS SDK needed), and Top
        Processes (CPU/GPU columns via `ProcessRankerService`, already
        ported for the Dashboard). New controls: `PortraitRing`,
        `PortraitSparkline`, `PortraitMetricTile`, `PortraitVendorBadge` —
        ported from Portrait Stats' WPF controls of the same shape, redrawn
        for WinUI3's shape APIs. New `PortraitColors.xaml` carries Portrait
        Stats' own multi-accent palette (CPU cyan/GPU purple/RAM green/etc.)
        deliberately kept separate from the main app's single-accent theme —
        this is Matt's own proven design, not meant to match the rest of
        Aether Control.
      - **Deliberately not ported**: Portrait Stats' Discord voice-channel
        section — that's a distinct bot/account integration decision, not
        something to wire blindly without Matt's own credentials/account
        choice. Flagged here rather than silently dropped.
      - **Not independently visually verified**: `AetherControl.exe` isn't a
        Start-Menu-registered app, so this session's computer-use access
        couldn't target it to confirm the drag fix or layout live. Rebuilt
        and confirmed stable (launches, runs, no crash) but the actual drag
        feel and visual result need Matt's own check.
      - **Round 2 — the actual drag bug found**: the first drag implementation
        was structurally correct (pointer capture + `AppWindow.Move`) but the
        header `Grid` had no `Background` set. In WinUI3/UWP a `Panel` with a
        null background is only hit-test-visible where a child actually
        paints something — the empty space a user would naturally grab to
        drag never received a `PointerPressed` at all. Fixed with an explicit
        `Background="Transparent"`.
      - **"Move to 2nd monitor and maximise, and un-maximise" — Fill Screen
        is now a toggle**: a fixed 768x1366 window won't match every real
        monitor's resolution, so instead of guessing, drag the window onto
        the target monitor and toggle Fill —
        `DisplayArea.GetFromWindowId(...).OuterBounds` + `AppWindow.MoveAndResize`
        snaps it to fill exactly that monitor. Toggling off restores the
        pre-fill position/size (remembered at the moment of filling) — Matt's
        exact ask, "when you maximise it, it can be minimised too." Works
        even with `IsResizable=false` (that flag only blocks user
        edge-dragging, not programmatic resize).
      - **Round 3 — Matt confirmed Round 2's fix still didn't move it.** The
        `Background="Transparent"` fix was real (that bug existed) but wasn't
        the whole story, and the hand-rolled `GetCursorPos`/`AppWindow.Move`
        drag implementation itself apparently doesn't reliably move a
        borderless (`hasBorder:false, hasTitleBar:false`) window in practice
        — plausible root cause: stripping `WS_CAPTION` at the Win32 level
        may put the window in a state where even direct `SetWindowPos`-based
        moves (which is what `AppWindow.Move` calls) don't behave normally,
        though this wasn't proven, just no longer worth chasing. Replaced
        entirely with `Window.SetTitleBar()` on a dedicated `DragRegion`
        element (name + clock, no buttons) — the exact mechanism
        `MainWindow` already uses successfully in this same codebase, so
        rather than debug a custom implementation further, this switches to
        the one already proven to work. Interactive controls (Pin/OLED/Fill)
        moved to be *siblings* of `DragRegion` rather than descendants — WinUI3
        doesn't auto-passthrough clicks to controls placed inside a
        `SetTitleBar` region without extra `InputNonClientPointerSource`
        setup, so keeping them outside sidesteps that entirely. This also
        brought back the native window border/title-bar/Close button (traded
        deliberately for a drag mechanism that actually works); Minimize/
        Maximize are disabled via the presenter since this stays a
        fixed-purpose panel, and the native Close button is now intercepted
        (`PortraitWindow.ForceClose()` vs. a regular close) to hide rather
        than destroy the window, matching what the old custom ✕ button did.
        **Not yet independently confirmed working** — same computer-use
        limitation as before (not a Start-Menu app) — needs Matt's own test.

## Phase 16 — Startup Manager visual parity with Radium
Read Radium's actual `StartupManagerPage.tsx` (`E:\radiumpcs\src\pages\`) for
the real design rather than guessing: summary tiles, publisher + location +
raw command per row, a coloured impact badge, an empty state.
- [x] `StartupEntry` gained `Publisher`, read via `FileVersionInfo.CompanyName`
      off the same executable path already extracted for impact estimation.
- [x] Summary tiles (Enabled / High Impact / Total Entries) above the list,
      matching Radium's `ops-summary-bar` — High Impact tone-warns when non-zero.
- [x] Each row rebuilt as a `HoverCard`: `ToggleSwitch` (was a plain
      `CheckBox`), Name/Publisher·Location/raw command (monospace, dimmed),
      and a colour-coded impact pill reusing the same Cool/Warm/Hot brushes
      MetricCard already uses elsewhere — one palette, not a second one
      invented for this page.
- [x] Empty state ("No startup entries found") matching the pattern already
      used on Fan Control/RGB/Device Utilities rather than a blank list.

## Phase 17 — Temperature alerts
Nothing paged the user when a temperature crossed a dangerous line — every
competitor app has this, Aether Control didn't.
- [x] `AlertSettingsStore` (Services) — JSON-backed, same pattern as
      `FanLabelStore`: Enabled/CpuTemperatureThreshold/GpuTemperatureThreshold,
      avoids a SQLite schema migration for two numbers and a bool.
- [x] `TemperatureAlertService` (App project — `Microsoft.Windows.AppNotifications`
      needs the WindowsAppSDK-versioned TFM, which only the App project has)
      — edge-triggered with 5°C hysteresis so a temperature sitting right at
      the threshold doesn't spam a toast every poll. Registered/unregistered
      with `AppNotificationManager` at startup/exit, which is what makes
      toasts work at all from an *unpackaged* WinUI3 app.
- [x] Settings gained an "Alerts" section: enable toggle + CPU/GPU threshold
      NumberBoxes.
- [ ] Not yet covered: RAM/drive-temp/fan-stall alerts — only CPU/GPU
      temperature for now, the two that actually matter for hardware safety.

## Phase 18 — Game Profiles (per-game auto Gaming Profile)
Direct port attempt of OmenCore's `GameProfileService` hit a wall worth
recording: OmenCore's version is HP-laptop-specific (GPU mux switching,
per-game undervolt offsets, keyboard lighting profiles) — none of which
apply to a custom desktop. Its `BiosUpdateService` turned out not to be a
good port candidate either: it's an HP-only softpaq-catalog client, and its
own code comments admit HP's public API "doesn't provide direct BIOS lookup"
most of the time — even OmenCore falls back to just linking the HP support
page. Aether Control's existing Firmware & Drivers page already does the
honest equivalent (current version + vendor link, no fake auto-check) for
generic hardware, so this wasn't ported.
- [x] `IGameProfileService`/`GameProfileService`: track a game executable,
      auto-apply the *existing* `ApplyGamingProfileAsync` the moment it
      launches, revert once every tracked game has exited. Detection is a
      periodic `Process.GetProcesses()` diff (3s interval) rather than
      OmenCore's WMI process-start trace — matches the technique
      `ProcessRankerService` already uses elsewhere in this codebase, and a
      few seconds of latency doesn't matter for this use case.
      Multi-game-safe: reverts only when *nothing* tracked is still running.
- [x] Shipped in the Optimisation Centre as a "Game Profiles" section right
      below Gaming Profile — add by name + executable, per-profile enable
      checkbox, live RUNNING badge, remove.
- [ ] Not ported: RGB colour/lighting-profile-per-game tie-in — real
      possibility now that Corsair direct control exists (Phase 12), just
      not wired up yet.

## Phase 19 — Undervolting (not started — two independent reasons, not one)
Actually read `E:\Omen\src\OmenCoreApp\Hardware\RyzenSmu.cs` before deciding,
rather than assuming risk in the abstract. Two separate blockers turned up:
1. **It needs a kernel-mode driver Aether Control doesn't ship.** The SMU
   mailbox writes in that file don't happen directly — every PCI-config-space
   read/write is routed through **PawnIO**, a signed third-party kernel
   driver, via `pawnio_open`/`pawnio_execute`. That's a real "extra software
   requirement" — a kernel driver install, Secure Boot interaction, the
   works — which directly contradicts what Matt said explicitly earlier this
   project: *"I don't want any extra software requirements."* Bundling a
   kernel driver is a materially bigger ask than anything else shipped this
   session (HidSharp, RTSS shared-memory reads, etc. all need nothing
   installed).
2. **The actual undervolt command IDs aren't in this file.** `RyzenSmu.cs` is
   generic SMU transport plumbing — message IDs (`SendMp1`/`SendPsmu`'s
   `message` argument) live in a caller not yet located, and per its own doc
   comment ("Based on G-Helper/UXTU implementation") those specific values
   were reverse-engineered against HP's mobile Ryzen SKUs. Matt's own CPU
   (9800X3D, desktop Zen 5) is a different generation with different SMU
   firmware — wrong command IDs on the wrong silicon can hang or bugcheck a
   running system, not just fail gracefully.
Recommendation put to Matt directly rather than guessed past: skip this one,
or scope it much narrower (a citable source for desktop Zen 5's exact SMU
mailbox IDs, no kernel driver) if he still wants it.

## Phase 15 — Tray icon parity (a real gap, not a new idea)
Matt's exact question: "system tray should show temps etc?" — it should have
already. `TrayReadoutFormatter` (Phase 3, with its own passing xUnit test)
and the `TrayMetricPreference`/`ISettingsService.GetTrayMetricPreferencesAsync`
plumbing were built early on and then never actually connected to the
`TaskbarIcon` in `MainWindow.xaml` — a genuine dropped thread, not something
deferred on purpose.
- [x] `MainWindow` now subscribes to `IHardwareMonitorService.SnapshotUpdated`
      and sets `TrayIcon.ToolTipText` via the existing formatter (e.g.
      "CPU 54°C  ·  GPU 48°C  ·  RAM 37%"), hovering the tray icon.
- [x] Falls back to a sensible default trio (CPU temp/GPU temp/RAM%) when
      `tray_metric_preferences` is empty (true on every fresh install — the
      table is never seeded), so the tray isn't blank until Settings gets a
      UI for it.
- [ ] No Settings UI yet to customise which metrics show or their order —
      the backend (`SaveTrayMetricPreferencesAsync`) already supports it,
      just needs a page.
- [ ] Tray *icon* itself is still the static app icon — HWiNFO/CAM-style
      "live number badge drawn onto the tray icon" is a further step beyond
      tooltip text, not started.
- [x] Settings now has a "Tray Icon" section — checkboxes for the 7
      `MetricKind` values `TrayReadoutFormatter` actually knows how to render
      (CPU/GPU temp+utilisation, RAM%, network up/down), backed by the
      already-existing `SaveTrayMetricPreferencesAsync`. Defaults match
      `MainWindow`'s own fallback (CPU temp/GPU temp/RAM%) so first-run
      behaviour is consistent before anyone visits Settings.
- [x] **Accent colour picker now actually does something** (previously
      `Colors.xaml` hardcoded Cyan with a TODO admitting the picker wasn't
      wired up). `AccentPalette.Apply()` mutates the shared `AetherAccentBrush`
      instance in place — every one of Aether Control's own custom-styled
      elements (gauges, cards, hover borders, `SegmentedToggle`) repaints
      immediately, no restart. Stock WinUI controls (buttons, switches, the
      nav selection pill) derive their colour from `SystemAccentColor` through
      internal `ThemeResource` chains that don't reliably re-resolve from a
      plain dictionary swap — those pick up the new accent on next launch,
      once `Apply()` runs again during startup. Settings' status message says
      so explicitly rather than overpromising a fully-live reskin.
- [x] **UI consolidation pass**: `Themes/Styles.xaml` now holds one
      `CardBorderStyle`, `PageTitleTextStyle`, `SectionHeaderTextStyle`,
      `SecondaryTextStyle`, and `AccentHyperlinkButtonStyle` shared by every
      page — no more hand-duplicated Border/TextBlock brush declarations
      that could silently drift apart. Every page's single primary action
      (Run/Rescan/Refresh/Load/Save) is now `AccentButtonStyle`; secondary
      actions stay default so the accent colour actually signals something.
      Vendor/support hyperlinks use the app's accent brush instead of the OS
      accent colour, so they stay on-brand regardless of Windows theme.
      Empty-value states clean up automatically: `MetricCard`'s unit label
      collapses when there's no unit, and a driver's vendor link hides
      entirely rather than showing a dead link when no URL is known.
      Every horizontally-laid-out card row (CPU/GPU/RAM/Network metrics,
      storage drives, motherboard fans) is now wrapped in its own horizontal
      `ScrollViewer` so a narrower window scrolls that row instead of
      clipping cards off-screen. `MetricCard` also got a fixed 152px width
      so tiles line up into a clean grid regardless of label length, and its
      `ThemeShadow` now actually renders (needs a Z-`Translation`, which the
      original markup was missing — previously a no-op).
- [x] Visual polish pass: custom title bar (`ExtendsContentIntoTitleBar` +
      `SetTitleBar`, transparent caption buttons tinted to match the palette),
      Mica (`BaseAlt`) backdrop on the main window with page backgrounds
      switched to a translucent charcoal so it actually shows through, and
      `MetricCard` now animates numeric changes (250ms ease-out glide) instead
      of snapping — formatting moved into the control itself
      (`NumericValue`/`Format`) so x:Bind sites stay converter-free. Portrait
      Mode intentionally stays fully opaque (no Mica) since it's a dedicated
      always-on display where desktop bleed-through or OLED-mode conflicts
      would be a regression, not a feature.
- [x] Optimisation Centre page
- [x] RGB Control page
- [x] Firmware/Driver Centre page (WMI-sourced current versions + vendor links, no auto-install)
- [x] Device Utilities (Mouse Centre) page — vendor links/profiles only
- [x] Settings page (theme/accent, refresh rate, export/import)
- [x] History page (custom Polyline chart, daily/weekly/monthly)
- [x] Project configured as its own standalone `AetherControl.exe` (unpackaged, `WindowsPackageType=None`)

## Phase 7 — Plugin framework
- [x] `IAetherPlugin` + capability interfaces (`IDashboardWidgetProvider`,
      `ITrayMetricProvider`, `IOptimisationTaskProvider`)
- [x] Assembly-scanning `PluginManager` implementation (collectible `AssemblyLoadContext` per plugin)
- [x] Sample plugin (`AetherControl.Plugins.Sample` — uptime widget) demonstrating the contract, builds clean

## Phase 8 — Polish / verification
- [x] Every project except the WinUI3 head builds green with plain `dotnet build`
      (`Core`, `Data`, `Plugins.Abstractions`, `Services`, `FanHelper`, `Plugins.Sample`)
- [x] `AetherControl.App` (WinUI3) — all C# and XAML/x:Bind compiles clean;
      blocked only at the PRI packaging step by a missing VS2022 component
      (see Known limitations)
- [x] Manifest: `requireAdministrator` (matches Portrait Stats — CPU MSR
      sensors and standby-memory clear both need it)
- [x] App icon wired from `img/Icon.png` → `Assets/AppIcon.ico` (multi-resolution)
- [x] `dotnet test` — 4/4 passing
- [x] README with architecture summary + Logo.png header
- [x] This roadmap doubles as the architecture/status reference; see README for
      the folder-structure and database-schema summary

## Phase 9 — Real hardware validation (in progress, live on Matt's machine)
- [x] **VS2022 Community installed** (with UWP + .NET Desktop workloads) —
      resolved the PRI-tooling gap. Building via VS's own MSBuild (not the
      `dotnet` CLI, which still can't find the PRI task even after VS install
      since it only checks its own SDK folder) now produces a real
      `AetherControl.exe`.
- [x] **First real launch found two bugs, both fixed:**
  - Every numeric dashboard tile rendered blank (not even "0") despite real
    sensor data arriving underneath (CPU/GPU names and Storage/Motherboard
    data *did* show). Root cause: `MetricCard`'s `Storyboard`/`DoubleAnimation`
    approach to animating numbers silently failed to update its target
    dependency property from x:Bind's first assignment. Replaced with
    `NumberTween` — a plain per-frame callback on
    `CompositionTarget.Rendering`, the same mechanism (ported from Radium PCs
    Companion's `useAnimatedNumber` React hook, which drives its own
    `requestAnimationFrame` loop) — which has no such failure mode.
  - Tray menu "Exit" didn't actually terminate the process — had to be
    force-killed via Task Manager. Root cause: `H.NotifyIcon`'s `TaskbarIcon`
    owns a hidden native window that `Application.Exit()` doesn't know about;
    it was never disposed. Fixed in `MainWindow.OnWindowClosed`.
- [x] **Full visual redesign** after first-look feedback ("doesn't look
      premium, no circular data, needs ASUS/Alienware/Dell-tier polish").
      Found and read Matt's third reference project, **Radium PCs Companion**
      (`E:\radiumpcs`, a Tauri/React app with an extensive, tested design
      system) and ported its actual proven patterns rather than guessing:
  - New `RadialGauge` control (270° arc, matches Radium's `Gauge.tsx`
    coordinate system exactly — 100×100 viewport, 225°→495° sweep, r=37 —
    rebuilt with WinUI's `PathGeometry`/`ArcSegment` instead of an SVG path
    string) with tone-coloured gradient fill and tick marks.
  - Dashboard now leads with a **hero row of 4 gauges** (CPU Temp/Load, GPU
    Temp/Load, RAM) rather than gauges everywhere — Radium reserves circular
    gauges for a handful of headline metrics too, not the whole surface,
    which reads calmer, not busier.
  - `MetricCard` redesigned: tone-coloured left accent stripe, bottom meter
    bar, ported severity thresholds (`SeverityToneConverter`: temperature
    warm≥70°C/hot≥85°C, usage warm≥65%/hot≥85%, straight from Radium's
    `severity.ts`) so temperature/utilisation tiles go cyan→amber→red
    automatically instead of staying flat white regardless of state.
- [x] **First re-test result**: real data confirmed everywhere, gauges and
      tone colours read well. Follow-up gaps found and fixed:
  - RAM Speed showed 0 MHz — genuinely never implemented (LibreHardwareMonitor
    doesn't expose module speed; added `MemorySpeedProbe` via WMI
    `Win32_PhysicalMemory.Speed`, queried once and cached since it can't
    change at runtime).
  - Motherboard Voltages/VRM Temperatures were dropped from the dashboard
    entirely during the redesign (real regression, not a hardware gap — the
    data was already flowing, just never displayed). Added back, plus a
    "no data yet" placeholder for any of Voltages/VRM/Fan Speeds when empty
    instead of silently rendering nothing (which reads as a missing feature).
  - Motherboard Fan Speeds still empty even after the FanHelper fix from the
    previous round — likely ASUS Armoury Crate (confirmed installed on this
    machine) holding the Super I/O ports at the OS level, which the
    fresh-process-per-poll trick doesn't fully solve, matching the caveat
    already documented in Portrait Stats' own comments. Surfaced as an
    explicit on-screen explanation rather than a blank section.
  - Storage cards only showed Model/Health/Temp — capacity and free space
    (`CapacityBytes`/`FreeBytes` were already being collected, just not
    displayed) are now shown via a usage meter bar + "Health · Temp" detail line.
  - Layout used a fraction of a wide window (everything left-aligned in thin
    scrolling strips). Replaced per-section horizontal `ScrollViewer`s with
    `VariableSizedWrapGrid` so cards wrap and use available width like a
    real dashboard grid instead of a scrollable list.
  - Gave `MetricCard` an optional `Detail` line (ported from Radium's own
    `MetricCard`, which has the same `<small>{detail}</small>` slot) and a
    `CardWidth` override for wider cards like storage.
- [x] **Second re-test result**: gauges/tone colours landed well. New feedback,
      compared directly against Armoury Crate — needs more "flare," and to
      borrow the RAM cleaner from Radium PCs Companion / OmenCore since Matt
      considers those solid. Also caught a real bug: every storage card
      showed "0 GB free."
  - **Storage 0 GB bug, root-caused**: the WMI associator walk went physical
    disk → partition → logical disk with `Win32_DiskDrive.DeviceID` (e.g.
    `\\.\PHYSICALDRIVE0`) backslash-escaped for the query. Rewrote it to walk
    the *other* direction — logical disk → partition → physical disk, the
    same direction and the same unescaped-DeviceID handling Portrait Stats'
    own proven system-drive lookup uses — since neither a drive letter nor a
    partition DeviceID ("Disk #0, Partition #0") contains a backslash to get
    wrong in the first place.
  - **RAM optimiser ported from Radium's `cleanup.rs`**: it doesn't just purge
    the standby list — it also calls `EmptyWorkingSet` on every accessible
    process first (documented API, `PROCESS_QUERY_LIMITED_INFORMATION |
    PROCESS_SET_QUOTA`, no elevation required, never terminates anything).
    Ported as `RamTrimmer`, now the first step of the memory-optimise task,
    with `GlobalMemoryStatusEx` measuring before/after like Radium's
    `sysinfo`-based measurement. `OptimisationResult` gained
    Before/After/Trimmed/Scanned fields, and the Optimisation Centre now
    shows a result panel with those as cards after running — same shape as
    Radium's own post-run cards. The memory task also no longer claims
    `RequiresElevation: true`, since the working-set trim (the part that
    always works) doesn't need it — only the standby-list half does, and it
    degrades gracefully with a clear message when not elevated.
  - **Visual "flare"**: added a `SectionHeader` control (small diagonal
    accent mark before the label, echoing Armoury Crate's own angled section
    header motif) and swapped it in on all 6 dashboard sections.
  - Storage/Startup cleaner parity with Radium's `RegistryCleanerPage`/
    `StorageCleanerPage`/`StartupManagerPage` and OmenCore's equivalents is
    NOT yet done — noted as the next round, not skipped silently.
- [x] **Third re-test result**: storage free space and the memory result
      panel both confirmed working. New requests: responsive layout (window
      wasn't full-screen and the dashboard just left half the width empty
      instead of using it), plus a general GUI/colour/font/animation/
      side-menu pass.
  - **Responsive two-column dashboard**: `DashboardPage` root is now a `Grid`
    with `VisualStateManager`/`AdaptiveTrigger` (breakpoint 1180px) — above
    it, CPU/GPU/Memory sit in a left column and Storage/Motherboard/Network
    in a right column side by side; below it, the right column drops under
    the left instead of squeezing into a column too narrow for its cards.
  - **App-wide accent colour cascade**: WinUI3's stock controls
    (NavigationView's selection pill, ToggleSwitch, CheckBox/RadioButton,
    Slider, HyperlinkButton, `AccentButtonStyle`) all derive their colour from
    `SystemAccentColor` + its Light1-3/Dark1-3 tints, not from any
    `AetherAccent*` resource — so the side nav's selected-item highlight was
    quietly using the *Windows* accent colour, not the app's own. Overrode
    the whole `SystemAccentColor` family in `Colors.xaml` with matching
    tint/shade ratios, so every stock control (nav selection included) now
    follows Aether Control's own accent automatically. Noted as a TODO in the
    same file: Settings' Accent picker doesn't apply to the UI at runtime
    yet, so swapping the enum today only affects what's saved to the DB —
    when that gets wired up, it needs to update this whole colour block, not
    just `AetherAccentColor`.
  - **Card hover**: `MetricCard` now animates its border to the accent
    colour on pointer-over (150ms ease-out) — its own per-instance brush, not
    the shared `AetherBorderBrush` resource, so hovering one card doesn't
    flash every card on the page.
  - Made the `Frame`'s page-to-page transition explicit
    (`NavigationThemeTransition`) rather than relying on default behaviour.
  - Did **not** get to: the segmented-pill mode selector or product-style
    imagery from the Armoury Crate comparison, deeper NavigationView
    restyling, or Storage Cleaner/Startup Manager parity with Radium/OmenCore
    — carried forward, not dropped.
- [x] **Fourth re-test result**: layout and accent colour confirmed good. Caught
      a real bug: storage free space and CPU clock speed both visibly
      flicked between two different values every second.
  - **Clock speed root cause**: `FindValue`'s fallback (grab "any Clock
    sensor of this type") almost never returns 0, so the intended
    `AverageMatching` per-core fallback never actually ran — the field was
    silently reading whatever sensor happened to be first in LHM's internal
    list, which isn't guaranteed stable poll-to-poll. Added `FindNamedOrZero`
    (returns 0 on a miss instead of grabbing an arbitrary sensor) so the real
    fallback — averaging all `Core #N` clock sensors — actually runs. Also
    fixed a related bug in the per-core array: matching a core's sensors by
    `Contains("#1")` also matches "#10"-"#19", silently pairing the wrong
    core's clock/temperature together; now indexed by exact parsed core number.
  - **Storage root cause**: confirmed the "best-effort" ordinal pairing
    between LHM drives and WMI drives (flagged as a known risk when written)
    was the actual bug — WMI's enumeration order isn't guaranteed stable
    between calls, so a card's free-space figure could silently swap to a
    different physical disk's number each poll. Now matched by model name
    (stable across polls) instead of list position.
- [x] **Flicker still reported after the clock/storage-matching fix** —
      re-diagnosed with a different theory: values were individually correct
      but the *list order* of storage drives / motherboard sensors / CPU
      cores isn't guaranteed stable poll-to-poll (neither WMI's nor LHM's
      enumeration order is), so the card at a fixed screen position could
      silently show a different physical drive's real numbers each second —
      indistinguishable from a single value flickering. Fixed by sorting
      every such list deterministically (storage by identifier, sensors by
      name, CPU cores by index) before it reaches the UI, so a given screen
      position always shows the same drive/sensor across polls.
  - Also fixed a real leak found in passing: `DashboardPage` never disposed
    its `DashboardViewModel`, so navigating away and back kept adding
    permanent subscribers to the singleton `IHardwareMonitorService` each
    time. Now unsubscribes on `Unloaded`.
  - Still **unverified** — awaiting Matt's re-test to confirm the ordering
    theory was the actual (or the whole) cause.
- [x] **GUI pass on the non-Dashboard pages**, bringing them up to the same
      design language established there:
  - Settings: was a flat list of 8 controls with no structure — now grouped
    into Appearance / Performance / Startup & Tray / Logging / Data sections,
    each its own `SectionHeader` + card.
  - RGB: added an empty state ("no devices found — make sure OpenRGB is
    running") instead of silently showing nothing, plus a scanning spinner.
  - Firmware: added a `SectionHeader`, tightened the layout around it.
  - Device Utilities: added a `SectionHeader` for the vendor list.
  - History: added an empty state on the chart before anything's loaded.
  - Not done this round: Optimisation Centre already got attention two
    rounds ago (result cards) and wasn't touched further; no new icon
    glyphs added (still flagged as unverified from earlier); Storage
    Cleaner/Startup Manager parity with Radium/OmenCore still outstanding.
- [x] **Fifth report: still flicking.** Ruled out a runaway-polling theory by
      reading the actual stored value directly from the SQLite settings db
      (`dashboard_refresh_ms = 1000` — normal). Re-examined `StorageHealthProbe`
      line by line and found a real, concrete bug the model-name-matching fix
      introduced: `QueryPhysicalDisks()` never actually set `DeviceId` on the
      WMI-side results, and never sorted them — so whenever `FindBestModelMatch`
      failed to match a drive by name (plausible for the USB "Seagate
      Expansion," which WMI often reports under its USB-bridge chipset name
      rather than the product name) it fell back to `candidates[0]` from a
      list whose order WMI doesn't guarantee stable between separate queries
      — the exact same bug as before, just one layer deeper. Fixed by setting
      `DeviceId` and sorting the WMI results by it before matching, so even
      the fallback path is now deterministic every poll.
      **Still unverified — this is the third attempt at this specific bug.**
      If it's still happening after this, the next step is to stop guessing
      blind and add temporary diagnostic logging so an actual poll-by-poll
      trace can be inspected instead.
- [x] **HoverCard**: factored the hover-brightens-to-accent effect out of
      `MetricCard` into a shared `HoverBorderEffect` helper, and built a
      generic `HoverCard` wrapper (any content, same hover feedback) for the
      list-style rows that don't fit `MetricCard`'s label/value/unit shape —
      Optimisation's task/startup rows, RGB's device rows, Firmware's
      component rows, Device Utilities' vendor rows. Settings' group cards
      deliberately left as plain (non-hover) borders — hovering while
      interacting with the ComboBoxes/ToggleSwitches inside would flash the
      border distractingly rather than read as a clickable card. Also swapped
      Optimisation's remaining plain section-header TextBlocks for the
      `SectionHeader` control for consistency with every other page.
- [ ] **Awaiting Matt's next look** — both on whether the flicker is finally
      resolved, and on the non-Dashboard pages' GUI pass (Settings grouping,
      empty states, HoverCard) that hasn't been reviewed yet.

## Known limitations (documented, not silently glossed over)
- **WinUI3 build needs a VS2022 component this sandbox doesn't have.**
  `dotnet build` on `AetherControl.App` gets all the way through C# compilation
  *and* the XAML compiler (every page, every x:Bind, every converter — all of
  it compiled clean) and only fails at the PRI resource-indexing step:
  `Microsoft.Build.Packaging.Pri.Tasks.dll` could not be loaded. That DLL
  ships with Visual Studio's "Windows application development" workload, not
  the standalone .NET SDK this sandbox has. **Action needed on Matt's machine:**
  open the solution in VS2022 with that workload installed (or run
  `dotnet build src/AetherControl.App -p:Platform=x64` from a dev machine that
  has it) — no code changes are expected to be required, this is a tooling
  gap, not a compile error.
- All other five projects (`Core`, `Data`, `Plugins.Abstractions`, `Services`,
  `FanHelper`) build with `dotnet build` today, 0 errors, 0 warnings.
- **Confirmed there's no lighter-weight fix**: searched the .NET 8 SDK install
  and the full NuGet package cache — `Microsoft.Build.Packaging.Pri.Tasks.dll`
  isn't shipped by `Microsoft.WindowsAppSDK` (only its `.targets` files that
  reference it) or by the standalone SDK. It's exclusively a Visual Studio
  workload file. **No exe can be produced from this sandbox.** To build one:
  install the "Windows application development" workload (Visual Studio
  Installer → Visual Studio 2022, or the free standalone Build Tools for
  Visual Studio 2022 if you don't want the full IDE), then run
  `dotnet build src/AetherControl.App -p:Platform=x64`. No code changes
  expected — every C# file and every XAML page/binding in the project
  compiles clean already, including after the visual polish pass below.
- Nav pane `FontIcon` glyphs (Dashboard/Portrait/Optimisation/RGB/Firmware/
  Devices/History) are best-effort Segoe Fluent codepoints chosen from memory,
  **not visually verified**. Worth a 30-second glance once buildable — Visual
  Studio's XAML designer or an icon picker makes swapping any wrong one trivial.
- OpenRGB mode-switching packet is structurally implemented but not
  byte-verified against a live server.
- SMART failure prediction flags the whole drive set rather than the specific
  failing drive (per-drive correlation needs PNPDeviceID matching — noted as
  a follow-up).

---

# Forward Plan — Phases 10-14

Written after the third flicker fix attempt was reported as unconfirmed and
Matt asked for (a) a real path to "look like the competitors" and (b) direct
answers on OpenRGB-without-OpenRGB, Corsair/Logitech detection, and fan
control. Those three questions drive the shape of Phases 11-13 below.

## Phase 10 — Flicker: root cause, confirmed ✅
Three blind attempts (sensor-fallback instability, list-ordering instability,
a missing `DeviceId` assignment) each fixed a real bug without confirmation.
`PollDiagnosticsLog` gave a real poll-by-poll trace, which settled both halves:
- [x] **Storage free space — confirmed fixed.** 20+ consecutive polls showed
      the exact same four values (`1031.4GB / 698.0GB / 499.0GB / 536.1GB`)
      at the exact same `DeviceId`s, zero variation. The DeviceId-sort +
      model-match fix held.
- [x] **CPU clock — root cause was never a bug.** The trace showed a
      genuine repeating ~4-second cycle (`418 → 393 → 693 → 572 → 809 → 648
      → 858 → 680 → 879...` then the same sequence again) — Windows/Intel
      core parking rotating which physical cores are active at idle. Every
      hardware monitor sees this; it reads as "flicker" only because the
      card redraws it raw, once a second, at a fixed screen position.
- [x] Fixed by adding `EmaSmoother` (`HardwareMonitorService.SmoothClockSpeeds`)
      — exponential smoothing (`alpha=0.15`) applied to the package clock and
      each per-core clock only. Deliberately **not** applied to storage free
      space or anything else that should be exactly stable — smoothing a
      value that's supposed to be constant would hide a real future
      regression instead of revealing one.
- [x] `PollDiagnosticsLog` removed — it did its job; keeping it around as a
      permanent per-poll disk write would be pure overhead in a shipping build.
- [ ] Apply the same trace-first approach (temporary targeted logging over
      guessing) to anything else reported as "flickering" or "flaky" going
      forward, rather than pattern-matching to this bug again by reflex.

## Phase 11 — Hardware detection layer (no vendor software required)
Direct answer to "what about detecting Corsair or Logitech devices?": **this
part is genuinely easy and needs nothing installed.** USB HID devices can be
enumerated directly by Vendor ID — Corsair `0x1B1C`, Logitech `0x046D`,
Razer `0x1532`, SteelSeries `0x1038`, ASUS `0x0B05`, Glorious `0x258A`,
HyperX/Kingston `0x0951` — via `HidSharp` (already a transitive dependency
through LibreHardwareMonitorLib) or Windows' own SetupAPI/WMI
(`Win32_PNPEntity`). No iCUE, no G HUB, no Synapse needed to *see* the device.
- [x] `PeripheralDetectionService` (`AetherControl.Services/Devices/`):
      enumerates connected HID devices via `HidSharp` (now a direct package
      reference, not just transitive), groups by vendor+product ID to avoid
      one physical device showing up N times for its N HID interfaces,
      resolves product name from the descriptor with a vendor-label fallback
      when a device (esp. a proprietary RGB interface) refuses that request.
- [x] Wired into **Device Utilities**: a "Detected Devices" section reads
      directly from real hardware — no static "here are 6 vendors, good
      luck" list as the only content.
- [x] Wired into **RGB Control** page too: a "Detected Hardware" section
      shows what's plugged in even for devices not yet controllable through
      OpenRGB (Phase 12) — detection and control stay separate concerns.
- [ ] Highlight/reorder the vendor-link cards below to surface detected
      vendors first, once more than a couple of vendors are commonly detected
      in testing.

## Phase 12 — RGB control without requiring OpenRGB installed
Direct answer to "how do we integrate OpenRGB without needing OpenRGB?":
OpenRGB itself has no trick — it talks to devices via raw USB HID reports
(or SMBus/I2C for motherboard ARGB headers), the same way any other
software would. It doesn't need iCUE/Aura/G HUB either, which is exactly why
it's popular. Replicating that means Aether Control becoming its own
"OpenRGB" for the devices it wants to support — there's no shortcut that
also avoids the engineering.

Two real constraints shape this:
1. **Licensing**: OpenRGB is GPLv2. Its per-device protocol code
   (`Controllers/*.cpp` on GitHub) is the reference for how each device
   works, but copying it into Aether Control's own commercial codebase would
   pull the whole module under GPL's copyleft. The safe path is a clean-room
   reimplementation — read the protocol, write our own code — not a port.
2. **Scope**: OpenRGB supports hundreds of individual products. Nobody
   reimplements that whole matrix as a first move; you pick the handful of
   most common devices and grow the list.

Recommended sequencing:
- [x] **Corsair, solved a different way than planned**: rather than a
      clean-room reimplementation of OpenRGB's Corsair driver, ported
      Matt's own `CorsairHidDirect` from OmenCore — already-shipped,
      already-tested code with no GPL heritage at all, since it's his.
      `CorsairHidDirectService` + `CompositeRgbService` (merges it with
      `OpenRgbService` behind the existing `IRgbService` contract, so
      `RgbViewModel`/`RgbPage` needed zero changes) now give real,
      no-software-required control — static colour plus Breathing/Spectrum/
      Wave effects — over Corsair keyboards and mice, covering the exact
      devices detected on Matt's own machine (K55 RGB PRO, Dark Core RGB PRO).
      Wireless USB receivers are still detected (Phase 11) but excluded from
      the controllable list — a receiver has no LEDs of its own to light.
- [ ] **Bundled OpenRGB for everything else**: bundle OpenRGB's own SDK
      server binary inside Aether Control's install and manage it silently
      as a child process, for devices/vendors Corsair-direct doesn't cover
      (motherboard ARGB headers, RAM, other brands). Reuses the
      `OpenRgbService`/`OpenRgbClient` already built; still not started.
- [ ] **Further out**: apply the same "port Matt's own code" approach used
      for Corsair to Logitech and Razer — OmenCore has `Logitech/`,
      `Razer/`, `Services/Logitech/LogitechDeviceService.cs` etc. not yet
      mined. Same licensing advantage: it's Matt's own prior work.
- [ ] DPI control for Corsair mice: `CorsairHidDirectService` doesn't yet
      port OmenCore's `BuildSetDpiReport`/`ApplyDpiStagesAsync` — the report
      tables exist in OmenCore and are straightforward to add; no UI for it
      yet either (would fit Device Utilities' mouse-centre page).

## Phase 13 — Fan control (not just reading)
Direct answer to "controlling fan speeds?": Aether Control currently only
*reads* fan RPM (via the out-of-process `FanRpmProbeService`) — there's no
write path yet. Two genuinely different fan populations:
- **Motherboard-headered fans** (case/CPU fans wired to Super I/O PWM
  headers): LibreHardwareMonitorLib actually exposes a *writable* control on
  supported Super I/O chips (`ISensor.Control.SetSoftware(percent)`) — the
  same mechanism FanControl/SpeedFan use. Not wired up yet.
  **Real caveat, not hidden**: this exact board already has ASUS Armoury
  Crate fighting for the same Super I/O ports (that's why fan RPM reads come
  back empty today — see Phase 10's neighbour, the FanRpmProbeService docs).
  Writing fan-speed commands would hit identical contention, and could
  actively fight whatever Armoury Crate is simultaneously trying to set —
  hunting fans or silently-overridden commands. Software fan control on this
  board likely needs Armoury Crate closed, the same tradeoff FanControl users
  already accept on ASUS boards running AI Suite/Armoury Crate.
- **AIO/GPU fans** (liquid cooler pumps, GPU fan curves): vendor-specific
  protocols again — same category of work as Phase 12's RGB, would piggyback
  on the same HID infrastructure once that exists.

Plan:
- [x] `IFanControlService` implemented directly on `HardwareMonitorService`
      (not a separate class) — it's the one object allowed to own an open
      LibreHardwareMonitor `Computer` session; a second instance just for fan
      control would reintroduce the exact multi-instance risk that forced
      `FanRpmProbeService` out-of-process for reads. Registered in DI as both
      `IHardwareMonitorService` and `IFanControlService` resolving to the
      *same* singleton, not two separate registrations.
- [x] Wraps LHM's `IControl.SetSoftware`/`SetDefault()` per Super I/O control
      channel. Safety choices made deliberately, not left implicit:
      software control is clamped to **20-100%** (never a full stop — no
      thermal watchdog exists if the app crashes mid-curve), and
      `Dispose()` now calls `ResetAllToAutomatic()` before closing the
      session, so a normal app exit always hands fans back to BIOS control.
- [x] `IsConflictingVendorSoftwareRunning()` checks for known ASUS process
      names (best-effort — exact names aren't documented, a miss just means
      the warning doesn't show). Surfaced as a warning banner in the UI
      *before* the channel list, not a silent failure.
- [x] Shipped in the **Optimisation Centre** (`OptimisationPage`) as a "Fan
      Control" section — per-channel name/percent/mode readout, a slider,
      and an "Auto" button per channel, plus "Reset All to Automatic".
      Revisit placement/styling once Phase 14 lands; functionally complete.
- [x] Per-channel renaming via `FanLabelStore`, ported verbatim from Portrait
      Stats — LHM's own channel names ("Fan #2") are just enumeration order,
      not what's actually plugged in, so a pencil-icon button next to each
      channel opens a rename dialog and remembers the label persistently.
- [ ] Fan curve *presets* (Silent / Balanced / Performance / Custom) as a
      layer on top of the raw per-channel sliders shipped above.
- [ ] AIO/GPU fan vendor protocols — still blocked on Phase 12's HID work.

## Phase 14 — "Look like the competitors," properly
Carried forward from the Armoury Crate comparison a few rounds back — the
section-header accent mark and hover cards were a first pass, not the whole
answer. Real screenshots from Matt's machine (dashboard, Optimisation Centre,
RGB Control) confirmed the base is already reading well — cyan gauges, tone
coloring, hover cards all landed as intended — so this phase is about closing
specific remaining gaps, not a rework.
- [x] Nav pane icon glyphs (flagged unverified since Phase 8) — confirmed
      rendering correctly in Matt's own screenshot. No fix needed; closing
      the open item.
- [x] `SegmentedToggle` control shipped — a two-state pill matching Armoury
      Crate's mode-row look, replacing the plain `ToggleSwitch` on Gaming
      Profile. Built specifically for today's two-state case rather than a
      generic N-option control for Phase 13's not-yet-built fan presets —
      that can extend this or get its own control when it actually exists.
- [x] Dashboard's three Motherboard empty states (Voltages/VRM/Fan Speeds)
      now use the same bordered-card treatment as every other page's empty
      state (RGB, Fan Control, Device Utilities) — they'd been left as bare
      text, which read as unfinished next to everywhere else.
- [x] A thin accent-to-transparent gradient line under the title bar — one
      deliberate flourish so the flat-charcoal chrome doesn't read as a bare
      Win32 window, echoing the colour-strip detail in Armoury Crate/similar.
- [x] **Dashboard "Top Processes"** — `ProcessRankerService`, ported verbatim
      from Portrait Stats (CPU via per-process processor-time deltas, GPU via
      the "GPU Engine" performance counter category), now surfaces top-5-by-
      CPU as its own dashboard section, matching Task Manager's Processes
      tab. Runs on its own 2-second background timer rather than piggy-
      backing the per-second hardware poll — enumerating every running
      process is real work, and competing with the poll that took several
      rounds to make flicker-free wasn't worth it. GPU ranking
      (`GetTopByGpu`) is ported and available but not yet surfaced in UI.
- [ ] Product-style imagery: even a simple vendor/category icon per detected
      device (Phase 11) or drive would move the RGB/Storage cards away from
      "text in a box" toward the reference apps' look.
- [x] **Storage Cleaner shipped** — replaces the old one-click "Clean
      temporary files" task (which deleted an unknown amount from 4 fixed
      locations with zero preview) with a proper scan → inspect → select →
      clean flow, matching Radium's `StorageCleanerPage` concept: each
      location's *actual* on-disk size shown before anything is touched,
      per-row checkboxes (all selected by default so one click still clears
      everything, same convenience as before), "Clean Selected", and an
      automatic rescan afterward so the numbers never go stale on screen.
      Backend (`WindowsCleanupService.GetLocations`/`CalculateReclaimableBytes`)
      already existed from Phase 4 — this exposes it through the UI properly
      instead of only running it blind. Startup Manager parity (Radium's
      `StartupManagerPage`) is separate and still not started — the existing
      "Startup Impact" list already covers enable/disable; parity would mean
      matching Radium's specific layout/detail, not new backend work.
- [ ] Revisit NavigationView pane background layering/item spacing beyond
      the accent-colour cascade from Phase 9 — not yet started.
- [x] **CPU core voltage — root-caused and fixed.** A one-shot sensor dump
      (same trace-first method as Phase 10, removed once done) showed this
      exact CPU (AMD Ryzen 7 9800X3D) exposes no real voltage sensor on
      LibreHardwareMonitorLib 0.9.4 — only `Core #N VID` (the value requested
      of the VRM, not a measurement; reads ~0.2 on this chip). The old
      `"Core #1"` match candidate matched `"Core #1 VID"` by substring and
      displayed it as real core voltage. Now only matches genuine
      voltage-rail sensor names; when none exist, the VOLTAGE card hides
      itself (`DashboardViewModel.HasCpuVoltage`) instead of showing a
      confidently-wrong `0.17V`. A newer LibreHardwareMonitorLib release may
      add real Zen 4/5 voltage support later — worth a version bump when
      that's confirmed, not attempted blindly given how many other sensor
      mappings depend on this library's exact naming.
