# Aether Control — Build Roadmap / Checklist

Source of truth for progress on this build. Updated as work lands.

**Jump to:** [Phases 1-9 (build history)](#phase-1--solution-skeleton) · [Phases 10-14 (forward plan)](#phase-10--flicker-root-cause-for-real) · [Phase 20 (storage/network flicker recurrence)](#phase-20--storagenetwork-flicker-recurrence) · [Phase 21 (CPU/GPU % jitter vs. Portrait Stats)](#phase-21--cpugpu--jitter-vs-portrait-stats) · [Phase 22 (PDH sampling correctness + median filtering)](#phase-22--pdh-sampling-correctness--median-filtering) · [Phase 23 (Optimisation Centre crash + the real flicker cause)](#phase-23--optimisation-centre-crash--the-real-flicker-cause) · [Phases 24-29 (comparable-app review — visual identity, GUI/UX)](#phase-24--visual-identity-icon-and-logo-now-match-the-in-app-accent) · [Phase 30 (formal storage audit — stale-not-zero + regression tests)](#phase-30--formal-storage-audit--stale-not-zero--regression-tests) · [Phase 31 (680/340 flicker — confirmed root cause)](#phase-31--680340-flicker--confirmed-root-cause) · [Phase 32 (per-device view models — the real architecture)](#phase-32--per-device-view-models--the-real-architecture) · [Phase 33 (shared metric-quality model — storage)](#phase-33--shared-metric-quality-model--storage) · [Phase 34 (single hardware owner + self-healing Super I/O reads)](#phase-34--single-hardware-owner--self-healing-super-io-reads)

## Phase 34 — Single hardware owner + self-healing Super I/O reads
Opus 5.5 audited the whole project live on Matt's machine (crash dumps, event
log, a live sensor dump, an A/B contention test) and produced a 5-stage plan;
Matt approved two immediate decisions (disable fan writes until fixed; do the
background-service split right after this stage) and asked to work through
the plan. This is Stage 1's first and largest item.
- [x] **Removed `AetherControl.FanHelper.exe` entirely** (project, solution
      entry, `ProjectReference`/copy-target, DI registration). A live A/B test
      (2026-09-23) proved this app's own out-of-process fan-RPM probe was the
      thing breaking motherboard reads: two concurrent Super I/O readers on
      the same Nuvoton NCT6701D chip make BOTH sides read back a poisoned
      pattern (every voltage rail collapsed to one of two identical values —
      Vcore = AVCC = CMOS Battery = 2.04V or 4.08V) persistently. Fan RPM is
      now read from `HardwareMonitorService`'s own single session, same as
      every other motherboard sensor.
- [x] **`IFanControlService.IsSoftwareControlSafe`** — per Matt's approval,
      `SetPercent` no-ops unless this is true. Returns `true` now that the
      contention source is removed (`ResetToAutomatic`/`ResetAllToAutomatic`
      were never gated — returning to BIOS default is always the safe
      direction).
- [x] **A much deeper problem, found while verifying the above**: removing
      FanHelper alone did NOT fully fix it. A fresh `Computer.Open()` on this
      board sometimes gets a poisoned first Super I/O read that never
      self-corrects for that session's lifetime — reproduced with a live
      capture showing 30 consecutive one-second polls all reading the
      identical invalid pattern, with nothing else touching the chip.
      Separately, already-installed vendor software (Armoury Crate, iCUE —
      both running throughout testing) genuinely does poll the same chip on
      its own schedule: an idle system with nothing of Aether's own running
      concurrently re-poisoned on an unmistakably periodic ~11-second
      cadence.
- [x] **`HardwareMonitorService.LooksPoisoned`** (`internal static`, pure —
      takes plain voltage values, not `ISensor`, specifically so it's
      unit-testable) — flags a read where 4+ voltage sensors collapse into
      fewer than 4 distinct values. Real boards report many genuinely
      different rails; this board's 15 sensors cover at least 8 when reading
      correctly (measured), 2 when poisoned.
- [x] **`EnsureSuperIoReadsAreValid`** — called once at startup after
      `Computer.Open()`: detects, and if poisoned, Close()/Open()s and
      re-checks, up to 5 attempts.
- [x] **Mid-session self-healing in `Poll()`** — the same detection runs on
      every poll. A poisoned tick falls back to the last known-good
      motherboard reading (same "stale beats garbage" convention already used
      for storage free-space) rather than publish impossible values, and
      schedules a reopen for the *start* of the next poll (not mid-tick —
      this poll's already-fetched `IHardware` references stay valid for the
      rest of this tick). Rate-limited to once per 10 seconds
      (`ReopenCooldown`) so sustained real contention can't turn into a
      Close/Open on every single poll.
- [x] **Found and fixed a self-inflicted infinite loop while testing this**:
      the first version of the mid-session recovery did one blind
      Close()+Open() with no verification. Because a reopen's own very next
      read can *also* be poisoned (same root quirk), this could re-trigger
      the mid-session check on the very next poll forever, with zero external
      cause — confirmed live (continuous poison/reopen pairs ~2s apart,
      unbounded, after all external test processes had already exited). Fixed
      by routing the mid-session recovery through the same verify-and-retry
      `EnsureSuperIoReadsAreValid` startup uses, plus the cooldown above.
- [x] **`hardware-open-failed.log` / `superio-reopen.log`** — permanent,
      minimal, best-effort file logs (`%APPDATA%\Aether Control\`) for
      `Computer.Open()` failure and every poison/recover/reopen event. No
      `ILogger` provider is wired up anywhere in the App project (it goes to
      the Windows Event Log, not nowhere — corrected a wrong claim from an
      earlier session), so this is the only place a future regression like
      this is visible without a live diagnostic session.
- [x] **`SuperIoPoisonDetectionTests`** (6 tests) — pins the detection logic
      against the *exact* voltage values captured live from both the
      poisoned and healthy reads on this machine, plus the sensor-count and
      boundary edge cases. 53/53 tests pass overall.
- [x] Verified end-to-end multiple ways: (1) external probe vs. Aether
      running/closed — matched the pre-fix baseline; (2) 30 consecutive polls
      from Aether's own live session; (3) 6 then 5 confirmed launch/kill
      cycles with no poisoning; (4) a deliberate 20-request hammer against a
      running session — detected and recovered every time, no garbage ever
      reached `LatestSnapshot`; (5) a single gentle probe against normal
      operation — caught a real collision, self-healed in ~2.4s.
- [x] README/docs site updated (FanHelper removed from the project table,
      build instructions, and provenance section; "One safe hardware
      session" bullet now describes the self-healing behaviour).
- **Not yet done** (rest of Stage 1): crash-dump analysis (no native debugger
      installed — `cdb`/WinDbg still needed), ordered shutdown, sensor-mapping
      accuracy fixes (effective clocks, real Vcore, named board temps, unused
      data), wiring up or removing the 6 dead settings, a diagnostics/support-
      bundle page, and hardware-fixture tests for the CPU/GPU mapper.

## Phase 33 — Shared metric-quality model — storage
Continuing `docs/ROADMAP.md`'s "Next" sequence after Phase 32 closed. Added
`MetricQuality` (`AetherControl.Core.Enums`: Good/Stale/Unavailable/
Unsupported/PermissionRequired/Conflicting/Disconnected/Error) and applied
it to storage — the two metrics in `docs/ROADMAP.md`'s "Initial rollout"
list that already had a codebase to attach it to.
- [x] `StorageDriveInfo.IsFreeSpaceStale` (bool) → `FreeSpaceQuality:
      MetricQuality`. `StorageHealthProbe.QueryPhysicalDisks` now correctly
      distinguishes `Stale` (real prior reading, just not fresh this poll)
      from `Unavailable` (never had a reading for this disk at all) —
      previously both were the same `true`.
- [x] New `TemperatureQuality` field — caught and fixed a real, previously-
      silent bug while wiring this up: `HardwareSnapshotMapper.MapStorage`'s
      `FindValue` fallback-to-zero pattern meant a drive with no real SMART
      temperature sensor exposed displayed a fabricated "0°C", indistinguishable
      from a genuine 0°C reading. Added `TryFindValue` (reports whether a
      sensor was actually found) and set `TemperatureQuality = Unsupported`
      when none is. `StorageDriveViewModel.DetailText` shows "temp n/a" for
      these drives instead.
- [x] Build (Core/Services/App) clean, all 47 tests pass, app launches and
      runs without a crash on the rebuilt binary.
- **Deliberately deferred**: CPU/GPU temperature, fan RPM, network
      throughput, RTSS FPS — each would touch `HardwareSnapshotMapper` (more
      sensors), `DashboardViewModel`, `PortraitViewModel`,
      `TrayReadoutFormatter`, and `HistoryService`/alerts, a materially
      larger blast radius with no currently-evidenced failure mode (unlike
      storage, which had two real, live-captured bugs). Rolling this out
      further without a specific reported problem to anchor each change
      risks the exact "destabilise working hardware control" outcome the
      product roadmap itself warns against.

## Phase 32 — Per-device view models: the real architecture
Matt: C: was stable after Phase 31, but a second SSD was still repeatedly
jumping, and correctly pushed back on the display-precision-equality
approach — patching `StorageDriveInfo.Equals` to exclude another volatile
field every time one was found is a losing game, not a fix. Live trace
(`/nvme/0`, 454 distinct card instances over 14 minutes) confirmed the
push-back: exact record equality fixed 3 of 4 drives but the one under
continuous real write activity kept regenerating.

**Root fix, not another equality patch**: separated stable identity from
volatile telemetry entirely, per the requested design.
- [x] `LiveCollectionSync<TViewModel, TSnapshot, TKey>`
      (`AetherControl.Core.Collections`) — owns an `ObservableCollection<TViewModel>`
      and keeps it in sync with a fresh snapshot list every poll: `Add` only
      for a genuinely new key, `Remove` only after a key has been absent for
      `maxMissedSyncs` consecutive polls (default 3 — absorbs one transient
      miss without visibly dropping a card), and for every key still
      present, an in-place `Apply` call onto the *same* view-model instance.
      **No `Replace` is ever issued for an existing key.**
- [x] `StorageDriveViewModel`, `NamedSensorValueViewModel`, `ProcessUsageViewModel`
      (`AetherControl.App.ViewModels`) — long-lived, one per stable identity
      (DeviceId / sensor name / Pid), created once, `Apply()`'d in place
      forever after. Each field is a normal `[ObservableProperty]` —
      CommunityToolkit's generated setter already compares old/new per field
      and only raises `PropertyChanged` for what actually changed, which is
      "raise PropertyChanged only for properties that materially changed"
      with no whole-object equality needed at all. `StorageDriveViewModel`
      also traces exactly which field(s) changed on each `Apply` when
      `StorageDiagnostics.TraceEnabled` (answers "which field causes each
      replacement" with evidence, even though there's no more replacement
      to cause).
- [x] `StorageDriveInfo.Equals`/`GetHashCode` **override removed** — reverted
      to plain, byte-exact record equality. Per the explicit design
      constraint this phase was built to: domain-model equality must not
      depend on display precision, and with per-field view-model updates
      there's no code path left that needs whole-object equality for
      storage at all. `StorageDriveInfo` stays exactly what it always was:
      an immutable, byte-precise service-layer snapshot.
- [x] `DashboardViewModel.Drives`/`MotherboardVoltages`/`MotherboardFanSpeeds`/
      `MotherboardVrmTemperatures`/`TopProcessesByCpu` and
      `PortraitViewModel.TopCpuProcesses`/`TopGpuProcesses` all migrated to
      `LiveCollectionSync` + their view-model type. `PortraitViewModel.Fans`
      deliberately left on plain `MergeFrom` — `PortraitFanRow`'s `RpmText`
      is already a pre-formatted, rounded string, so record equality on it
      is already precision-correct with no risk of the same failure mode;
      migrating it would have been unnecessary churn.
      `StorageDetailConverter` removed — a converter over a *whole* bound
      object only re-runs when the DataContext reference itself is swapped,
      not when `ObservableObject` raises `PropertyChanged` for one of
      several fields it reads, so it couldn't stay reactive under this
      design. Replaced with `StorageDriveViewModel.DetailText`, a computed
      property refreshed via `OnHealthChanged`/`OnTemperatureCelsiusChanged`/
      `OnIsFreeSpaceStaleChanged` partial hooks — the same pattern already
      used for `FreeGb`/`CapacityGb`/`UsedPercent`.
- [x] 16 new tests: 9 in `LiveCollectionSyncTests` (new device creates one
      card; changed volatile field updates in place, same instance; changed
      value updates in place; same key retains the same instance across 50
      polls with continuous tiny drift; enumeration order never recreates
      either card; a removed device survives one transient miss but is
      removed after reaching the absence threshold; reappearing before the
      threshold cancels the miss counter and reuses the same instance;
      multiple independent devices each get their own stable card) plus
      2 replacing the removed display-precision tests in
      `ObservableCollectionMergeExtensionsTests` (now framed around what
      `MergeFrom` is actually still used for — Portrait's Fans list).
      44 → 47 net after removing the 2 tests that specifically pinned the
      now-reverted precision-equality behaviour.
- [x] **Live verification: confirmed.** The Smart App Control block
      (`Microsoft-Windows-CodeIntegrity/Operational` event 3077/3118 —
      almost certainly triggered by how many times this unsigned binary was
      rebuilt/relaunched with a different hash in one session) cleared on
      its own on the next launch attempt. Ran the build live with tracing
      on for ~90 seconds and checked `ui-value-trace.log` directly: **every
      one of the 4 drives — including `/nvme/0`, the one that had shown 454
      distinct card instances over 14 minutes before this phase — held
      exactly one real `MetricCard` GUID for the entire session.** Real,
      sub-perceptible telemetry changes (`Apply: FreeBytes
      717264080896->717264076800 (ΔGB=-0.000004)`, genuine filesystem
      activity) were applied in place with zero card recreation. The
      original 680/340-style oscillation cannot occur under this
      architecture — there is no code path left that tears down and
      recreates a card for an unchanged or trivially-changed reading.
      Per `docs/ROADMAP.md`'s own "Definition of done for the storage
      issue," every listed criterion is now satisfied. Gated
      `StorageDiagnostics.TraceEnabled` back to `false` (default,
      no-overhead) now that it's done its job — the infrastructure stays
      in the codebase rather than being deleted, since it's what actually
      cracked this, and the exact same trace could catch a future
      "card flickers" report the same way.
- **Not done, explicitly out of scope for this phase**: an
      `IIncrementallyUpdatable<TSnapshot>` interface was suggested as one
      possible shape; `LiveCollectionSync`'s constructor-injected delegates
      (`create`/`apply`) were used instead, since the view-model types
      already differ enough in constructor requirements
      (`StorageDriveViewModel` needs `DeviceId` from the snapshot at
      construction, others don't) that a single interface didn't fit more
      cleanly than delegates. Network adapters, GPUs, and plugin widgets
      were named in the review request but don't exist as multi-item
      collections in the current codebase (Aether shows one GPU and one
      network adapter as scalar properties) — nothing to migrate there.

## Phase 31 — 680/340 flicker: confirmed root cause
Matt reported the 680GB/340GB alternation was still reproducible after Phase
30, and correctly pushed back that Phase 30 only ever proved the *backend*
(StorageHealthProbe) was stable — it never traced what the UI layer actually
did with a value once bound. This phase instruments and traces the full
pixel path instead, per that push-back.

**Found, via a real cross-repository comparison, not guessing:** `NumberTween`
is a direct port of Radium PCs Companion's `useAnimatedNumber.ts`
(`E:\radiumpcs`, already a documented reference project — see Phase 10). The
original hook coerces a non-finite (`NaN`/`Infinity`) value to 0 and
**returns immediately** (no animation). The WinUI3 port coerced to 0 but
**fell through** into the animation path, so a single non-finite input would
glide the last real value down toward a fabricated 0 — for a value around
680, visibly passing through ~340 on the way. Real, evidenced port-fidelity
bug; fixed. However, no concrete reachable path was found for storage's own
`FreeGb` to actually go non-finite under real WMI conditions, so this alone
doesn't explain the report — see below for what does.

**Confirmed root cause, captured directly in a live trace (not inferred):**
Added `UiValueTraceLog` + `MetricCard.DiagnosticTag` (opt-in, temporary —
see `StorageDiagnostics`), wired to the Storage section's cards, and ran the
app with tracing on. The trace showed a **brand-new `MetricCard` instance
GUID appearing roughly once every second, forever**, for every storage card
— each one animating from 0 up to its real value over ~15 frames. For a
value like 682GB, that climb passes through ~340 (its half) as a completely
ordinary intermediate frame. This is not a wrong value — it's the correct
value, climbing from zero to itself, every single second.
- **Why**: `ObservableCollectionMergeExtensions.MergeFrom` (Phase 23)
  replaces the collection item via the indexer whenever a fresh reading
  differs from what's there — but `StorageHealthProbe` builds a brand-new
  `StorageDriveInfo` every poll even when nothing changed, and plain classes
  compare by reference, so "differs" was true on literally every poll.
  Phase 23's own doc comment asserted that `ItemsControl` recycles the
  container on a `Replace` and just rebinds it — **that assumption was never
  verified and was wrong** for an `ItemsControl` backed by a
  `VariableSizedWrapGrid` `ItemsPanel` (no virtualization support): it tears
  the container down and rebuilds it on `Replace`, exactly like a full
  `Reset`.
- [x] Converted `StorageDriveInfo`, `NamedSensorValue`, `ProcessUsageInfo` to
      `record`s (value equality) so two separately-constructed, field-
      identical readings are actually equal.
  - `StorageDriveInfo` further overrides `Equals`/`GetHashCode` to compare
    at **display precision** (whole GB/°C, matching its own `"F0"` format)
    rather than raw bytes — the system drive is under real, continuous
    write activity (temp files, browser cache, logs), so its exact
    `FreeBytes` differs by a few KB on nearly every poll even though the
    *displayed* number never moves. Plain record equality (byte-exact)
    fixed 3 of 4 drives immediately in the live trace but left the actively-
    written system drive still rebuilding every second — caught by watching
    the *second* live trace, not assumed.
- [x] `ObservableCollectionMergeExtensions.MergeFrom` now skips the indexer
      assignment entirely (no `Replace` event, no container touched) when
      the incoming item is value-equal to what's already there. Relocated
      from `AetherControl.App.ViewModels` to `AetherControl.Core.Collections`
      — it has zero WinUI dependency and belongs where it can be unit tested
      directly (the App project's TFM can't be referenced from the test
      project's).
- [x] `NumberTween` split into a thin WinUI3 adapter plus `TweenState`
      (`AetherControl.Core.Animation`) — pure, no `CompositionTarget`
      dependency, fully unit tested. This is what made the non-finite-input
      port-fidelity fix testable at all.
- [x] Temporary diagnostics added and left in place for now (not deleted
      immediately per Part 8 — the fix should stay observable until Matt
      confirms it live): `UiValueTraceLog` (writes to `ui-value-trace.log`
      when `StorageDiagnostics.TraceEnabled`, opt-in per `MetricCard` via
      `DiagnosticTag`), and an animation bypass
      (`StorageDiagnostics.AnimationEnabled = false` skips `NumberTween`
      entirely for tagged cards, writing the formatted value straight to the
      `TextBlock`). Both are static, in-memory, non-persisted flags — not
      wired into `AppSettings`/SQLite, since a schema migration is more risk
      than a short-lived diagnostic warrants. **Remove `StorageDiagnostics`,
      `UiValueTraceLog`, `MetricCard.DiagnosticTag`, and the `DiagnosticTag`
      binding in `DashboardPage.xaml` once the fix is confirmed closed.**
- **Live verification status**: rebuilt and relaunched with tracing on.
  First trace run showed the bug directly (a new card GUID every second,
  all 4 drives). After the record-equality fix, a second trace run showed
  3 of 4 drives fully stable (exactly one card instance for the whole
  session) — the 4th (the actively-written system drive) was still
  rebuilding every second, which led directly to the display-precision
  equality fix above. **That second fix has not yet been re-verified live**
  — the running instance is elevated and couldn't be closed from this
  session to rebuild; needs one more close/rebuild/relaunch/observe cycle.
- [x] 6 new regression tests
      (`ObservableCollectionMergeExtensionsTests`) pin: an unchanged reading
      produces no `CollectionChanged` event at all and the original object
      instance is preserved; a real change still replaces; 10 repeated
      identical polls produce zero replaces after the first insert; a
      value-equal `StorageDriveInfo` really does equal another
      independently-constructed one; sub-whole-GB byte drift on an actively-
      written drive counts as unchanged; a change large enough to cross a
      whole-GB boundary still counts as changed.
- [x] 9 new `TweenState` tests
      (`TweenStateTests`) pin: a fresh state starts at 0; a non-finite
      target is ignored outright (value stays exactly where it was, not 0,
      not halfway); a real `0.0` target still animates normally (the fix
      doesn't swallow legitimate zero readings); a repeated identical target
      snaps rather than restarting; a target within the snap threshold also
      snaps; a new target arriving mid-animation redirects from the current
      *interpolated* value, not the original start point; a completed
      animation clears `IsAnimating`; 20 repeated identical polls never
      animate; large realistic GB values round-trip precisely.
- **Cross-repository review (partial, evidence-based)**: found
  `theantipopau/pccompanion` (public GitHub repo) — this is "PC Companion,"
  already known locally as Radium PCs Companion (`E:\radiumpcs`, read and
  ported from extensively in earlier phases — see Phase 10's `RadialGauge`/
  `MetricCard`/`SeverityToneConverter`/`NumberTween` ports). Diffing
  `useAnimatedNumber.ts` against `NumberTween.cs` directly is what surfaced
  the non-finite-handling port-fidelity bug above — a real code-level
  comparison, not a README feature-list comparison. The full 30-category
  comparison matrix (OmenCore + Portrait Stats + PC Companion vs. Aether,
  across polling/timers/portrait-detection/fan-safety/tray/automation/etc.)
  requested alongside this was **not** completed in this pass — it's a
  large, separate undertaking, and the one comparison actually relevant to
  the open defect (the animation hook) was prioritized and completed instead
  of spreading effort across all 30 categories shallowly. Logged as a
  follow-up phase rather than rushed.
- **UI/UX redesign (Parts 10-13 of the audit brief)**: not attempted in this
  pass. A full navigation restructure, new visual language, accessibility
  overhaul, and asset inventory is real, valuable work, but doing it in the
  same pass as an active, actively-being-diagnosed data-correctness defect
  — without the ability to visually verify any of it live (no computer-use
  access to this unlisted app) — is exactly the "no broad UI rewrite without
  regression validation" risk the audit brief itself warned against.
  Deliberately deferred, not skipped.
- [x] Build (Core, Services, App via VS MSBuild) and full `dotnet test` run
      clean before and after every change in this phase: 38 passing after
      Phase 30 → 40 passing at the end of this phase, 0 failures throughout.

## Phase 30 — Formal storage audit — stale-not-zero + regression tests
A detailed audit brief described free space "flickering between ~680GB and
~340GB" and required diagnosis from real evidence only, no UI smoothing as a
disguise, and a proper regression-tested fix. Full write-up (evidence, data
flow, files, remaining risk) delivered as a chat report; this entry is the
durable record.
- **Could not reproduce 680/340 live.** `storage-trace.log` had ~6300+ polls
  already logged since the Phase 20-23 fixes landed — completely stable
  throughout, no zero readings, no halving, no duplicate/reordering
  artifacts. 680 is close to real drive C:'s free space at various points
  this session (664-682GB); 340 is close to exactly half of that. That
  match is the basis for what follows — it is a code-verified mechanism,
  not a directly-captured trace of the reported event, and is reported as
  such rather than overclaimed as "confirmed root cause."
- [x] **Found and fixed a real gap this audit exposed**: `BuildFreeSpaceByPhysicalDisk`
      returned an *empty* dictionary on a caught `ManagementException` (or on
      one disk's association simply not resolving that poll), and the caller
      fell back to `GetValueOrDefault(deviceId, 0)` — a transient failure
      displayed as a hard 0. `MetricCard`'s `NumberTween` would then glide
      the previous good value down to 0 and back up on the next successful
      poll, visually passing through the midpoint — mechanistically exactly
      "X, then half of X, then X again." Fixed: `StorageHealthProbe` now
      keeps `LastGoodFreeBytesByDiskId` and falls back to the last known
      value (marked `IsFreeSpaceStale = true`) instead of zero when a disk's
      reading is missing that poll.
  - [x] `StorageDriveInfo.IsFreeSpaceStale` (new) — missing data is now a
        distinct, carried fact, never silently coerced to "0 bytes free."
  - [x] `StorageDetailConverter` appends "· stale" to the Detail line when
        set — the number itself is never hidden, only labelled untrustworthy.
- [x] **Extracted `ComputeFreeSpaceByPhysicalDisk` as a pure, testable core**
      (no WMI, no `DriveInfo`) out of `BuildFreeSpaceByPhysicalDisk`, which is
      now a thin I/O wrapper. Added `InternalsVisibleTo("AetherControl.Tests")`
      on the Services assembly so the test project can exercise `internal`
      logic directly rather than needing it made public API.
- [x] **14 new regression tests** (`StorageHealthProbeTests.cs`): one
      disk/one volume, one disk/multiple volumes, multiple disks, duplicate
      association rows (pins the Phase 20 dedup fix), reversed enumeration
      order producing identical results, a temporarily-unavailable volume
      being excluded rather than zeroed, a mapped-drive-style unresolved
      association being dropped rather than corrupting another disk's total,
      a fully failed enumeration returning empty rather than fabricating
      zeros, byte↔GB conversion at exact boundaries, and the stale flag
      being independent of the value it's attached to.
  - **One of these caught a second, real, previously-unknown bug during
    this same audit**: `StorageDriveInfo.UsedPercent` had no clamp, so
    `FreeBytes > CapacityBytes` (which the new stale-fallback path can
    legitimately produce — `CapacityBytes` and `FreeBytes` come from
    independent WMI reads a moment apart) computed a literal **-50%**.
    Fixed by clamping `UsedPercent` to `[0, 100]` at the model layer — the
    one place this number is computed, not patched at every display site.
- **Ten hypotheses, checked against the actual current architecture** (not
  reasoned about in the abstract): (1) two-records-aggregated /
  (2) physical+logical mixed / (9) duplicates not deduped — structurally
  ruled out by the `HashSet`-keyed `countedContributions` dedup from Phase
  20, now regression-tested. (3) LHM and DriveInfo both writing the same
  property — false; grep confirms `StorageHealthProbe` is the *only* writer
  of `FreeBytes`/`CapacityBytes` in the whole codebase. (4) racing refresh
  loops / (11) concurrent refresh — structurally impossible: `Enrich` is
  called from exactly one call site (`HardwareMonitorService.Poll()`),
  itself already serialized by `Monitor.TryEnter(_pollLock)`. (5) recycled
  card binds to the wrong volume — the Phase 23
  `ObservableCollectionMergeExtensions.MergeFrom` keyed by `DeviceId`
  already addresses this for the UI layer. (6) non-atomic clear/repopulate —
  `HardwareMonitorService.Poll()` builds one complete `HardwareSnapshot`
  and publishes it via a single `SnapshotUpdated` event; there is no
  intermediate partially-built state observable by a consumer. (7) failed
  enumeration overwriting the last good snapshot — **confirmed true, and
  fixed** (see above). (8) a removable/mapped volume changing the
  aggregate — covered by the new "temporarily unavailable" and "mapped
  drive disconnect" tests; a not-ready drive contributes nothing rather
  than a stale/wrong number. (10) double or inconsistent unit conversion —
  false; `StorageDriveInfo.CapacityGb`/`FreeGb` are the only division sites,
  now pinned by a boundary-value test.
- **Broader 13-point maintainability/UX audit** (telemetry provenance,
  capability-driven detection, Observe/Control/Display separation, storage
  bars vs. gauges, designed stale/loading states, reversible optimisation
  workflows, activity timeline, portrait presets, accessibility, polling
  efficiency, test coverage, README screenshots): reviewed against the
  current codebase rather than implemented wholesale in this pass — several
  items are already substantially satisfied by earlier phases (storage
  already uses a capacity bar via `MetricCard.Progress`, not a radial gauge;
  snapshots are already atomic; UI already updates by stable ID). Doing a
  full ground-up rewrite of every item in one unreviewed pass would risk
  the exact "destabilise working hardware control" outcome the brief itself
  warned against — logged as future phases rather than rushed.
- [x] Build (Services, App via VS MSBuild) and full `dotnet test` run clean
      before and after: baseline 4/4 passing, 18/18 passing after (14 new).
- [ ] Not yet re-confirmed live against a real recurrence of the reported
      symptom, since none occurred in this session's telemetry either before
      or after the fix.

## Phases 24-29 — Comparable-app review: adaptable ideas, visual assets, GUI/UX
Reviewed six comparable open-source projects at Matt's request (Lenovo Legion
Toolkit, hw-smi, HardwareVisualizer, Core-Monitor, the HardwareMonitor/openhardwaremonitor
fork, LibreHardwareMonitor itself) for anything worth adapting, plus a pass
over Aether's own current assets/theme/layout. Working through these
incrementally rather than in one pass — ticked items below are done, open
ones are queued.

### Phase 24 — Visual identity: icon and logo now match the in-app accent
Found via the review, not reported by Matt: `Assets/Icon.png`, `Logo.png`,
and `AppIcon.ico` (taskbar/tray/titlebar icon) were all green, while every
other pixel of the actual UI (`Colors.xaml`'s `AetherAccentColor`, every
card highlight, the titlebar accent-fade strip) is cyan (`#00E5FF`) — the
first thing anyone sees in the taskbar didn't match the app that opened.
- [x] Hue-shifted `Icon.png`/`Logo.png`/`AppIcon.ico` from green (~144°) to
      the exact accent hue (~186°) via HSV rotation (Python/Pillow) rather
      than redrawing from scratch — preserves the original artwork's shading/
      glow exactly, just recolours it. Regenerated the 10-size `.ico`
      (16 through 256px) from the new `Icon.png`. Synced the README's
      separate `img/Logo.png`/`Icon.png` copies to match.
- [ ] Not yet visually confirmed live (rebuild pending — Matt's running
      instance has the exe locked; batching remaining changes before asking
      for a relaunch).
- [ ] GitHub social-preview image (1280×640, shown when the repo link is
      shared) — not created yet, should be generated from the new logo once
      the icon is confirmed.

### Phase 25 — NavigationView grouping
- [x] Added a `NavigationViewItemSeparator` and reordered the side nav into
      monitoring/viewing (Dashboard, Portrait Mode, History) then
      action/control tools (Optimisation Centre, RGB Control, Firmware &
      Drivers, Device Utilities) — previously one flat list of 7 items with
      no visual grouping. Tag-based navigation switch in
      `OnNavigationSelectionChanged` is unaffected by reordering.
- [ ] Not yet visually confirmed live (same pending rebuild as Phase 24).

### Phase 26 — Dashboard sparklines
- [x] Added a "Trends" section at the top of the Dashboard's left column
      (CPU/GPU load, last 60 samples), reusing `PortraitSparkline` directly
      rather than retrofitting it into `MetricCard`. Deliberately didn't
      embed the sparkline inside `MetricCard` itself — that control is shared
      across Dashboard *and* Optimisation Centre inside fixed-height
      `VariableSizedWrapGrid` cells (`ItemHeight="106"`), and the Dashboard's
      own responsive layout already has a `VisualStateManager` that
      repositions `RightColumnPanel` into `Grid.Row="2"` below ~1180px width
      — retrofitting card height or grid rows risked breaking either without
      being able to visually verify every window-width case. A dedicated
      section sidesteps both: same proven control Portrait Mode already
      uses, no shared-control or grid-row risk.
      `DashboardViewModel` gained `CpuUsageHistory`/`GpuUsageHistory`
      (`PortraitHistory`, 60-sample ring buffer — same class, same capacity,
      as Portrait Mode's own).
- [ ] Not yet visually confirmed live.

### Phase 27 — History page: richer trends
Correction on the original review: `HistoryViewModel.LoadAsync` already
computes min/max/avg — it just wasn't visible until you clicked Load, and
never updated after that.
- [x] Auto-refresh: `HistoryViewModel` now loads immediately on construction
      (previously needed a click even to see the page's own default
      selection) and re-loads automatically every 30s (`Timer`, marshaled
      back via `DispatcherQueue`) plus whenever the metric or resolution
      selection changes (`OnSelectedMetricChanged`/`OnSelectedResolutionChanged`
      partial hooks) — no longer needs the manual "Load" click for the
      common case, though the button still works for an on-demand refresh.
      Made `HistoryViewModel` `IDisposable` to stop the timer on navigation
      away; wired via `HistoryPage`'s `Unloaded`.
- [ ] Not done: overlaying 2+ series at once (e.g. CPU temp + GPU temp) and
      axis labels/gridlines on the chart itself — bigger changes to the
      `Canvas`/`Polyline` rendering in `HistoryPage.xaml.cs`, left for a
      later pass.
- [ ] Not yet visually confirmed live.

### Phase 28 — Fan curve editor
- [ ] Not started, biggest item of the batch. Every serious fan-control tool
      (FanControl, SpeedFan, Core-Monitor) converges on a draggable
      temp-vs-speed curve instead of a flat per-channel percentage slider,
      which is all `OptimisationPage`'s Fan Control section has today. Needs
      a custom `Canvas`-based curve control (points draggable, interpolated
      line, persisted per channel) — a real feature addition, not a quick
      visual fix, so scoping this as its own phase once 24-27 land.

### Phase 29 — GPU% via vendor-native APIs (NVIDIA NVML) — someday
- [ ] Not started, lowest priority of the batch. `hw-smi` reads GPU load via
      vendor SDKs (NVML for NVIDIA, ADLX/AMDSMI for AMD, Level-Zero for
      Intel) rather than a generic OS counter — more authoritative than
      either LHM's ADL sensor or the "GPU Engine" PDH counter Aether
      currently uses (Phase 21/22), and doesn't have the PDH sampling-
      interval fragility that took two rounds to fix. Real effort for
      NVIDIA-only benefit (P/Invoke bindings, driver-DLL versioning) against
      an already-solid current fix — parked behind the cheaper wins above.

Explicitly *not* adopting: Lenovo Legion Toolkit's on-screen-overlay concept
(needs a D3D hook into every game, too large for the value here); a light
theme (every comparable "control centre" style app in this genre — Armoury
Crate, MSI Center, Core-Monitor — stays dark-only by design, and Aether's
`Colors.xaml` already says as much deliberately).

## Phase 23 — Optimisation Centre crash + the real flicker cause
Matt: "clicked optimization centre and crashed" + "storage values are still
jumping all over the shop" (after Phase 22's fixes were already live).

**The crash** — pulled a managed stack out of the actual crash dump
(`dotnet-dump analyze`, `clrstack -all`) rather than guessing from the Event
Viewer's native-only "Microsoft.ui.xaml.dll / 0xc000027b" entry. A background
thread was mid-`WindowsCleanupService.DirectorySize` → `EnumerateFilesSafely`,
walking Edge/Chrome's cache folders (auto-triggered by opening the page) —
`EnumerateFilesSafely`'s catch only covered `IOException`/`UnauthorizedAccessException`,
and `PathTooLongException` (very plausible against a browser cache's deep
hashed-folder structure) does *not* derive from `IOException`. It escaped,
crossed the async→UI-thread boundary, and WinRT turned it into a fatal
stowed exception — killing the whole app over one folder it couldn't walk.
- [x] Broadened the catches in `WindowsCleanupService` (all three
      filesystem-walking spots) — `EnumerateFilesSafely`'s now catches
      any exception, since it's walking live state owned by another running
      process (the browser) where an enumeration failure is an expected
      outcome, not a bug to propagate.
- [x] **Added `App.UnhandledException` as a backstop**, logging to
      `%AppData%\Aether Control\unhandled-exceptions.log` and setting
      `Handled = true`. There was no global handler at all before this — a
      single missed catch anywhere in the app could take the entire process
      down, which is exactly what happened here.

**The "still jumping" storage report** — `storage-trace.log` (added in Phase
20) by this point covered the *entire* session since the last relaunch:
~6300 polls, and the backend was perfectly stable throughout (two clean,
gradual transitions consistent with real disk writes, zero anomalies). That
ruled out the sensor/WMI layer entirely and pointed at the UI. Found it:
`DashboardViewModel.Drives` (and `MotherboardVoltages`/`MotherboardFanSpeeds`/
`MotherboardVrmTemperatures`/`TopProcessesByCpu`) were reassigned to a
**brand-new list object every single poll**. `ItemsControl` has no way to
know a freshly-assigned `IReadOnlyList<T>` represents "mostly the same data as
before" — it tears down and recreates every realized `MetricCard` container
from scratch. A freshly-constructed `MetricCard`'s `NumberTween` starts at 0,
so its first `NumericValue` update glides 0 → (say) 682GB over ~220ms —
*every single second, forever*. The number was never actually wrong; the UI
was destroying and rebuilding the animated tile that displays it once a
second. This is almost certainly the dominant cause of most "jumping"
reports this session, not sensor noise.
- [x] Added `ObservableCollectionMergeExtensions.MergeFrom` — updates an
      `ObservableCollection<T>`'s contents in place (`Replace`/`Insert`/`Remove`/`Move`
      via the indexer) instead of ever reassigning the collection reference.
      `ItemsControl` reuses the existing container for a key that's still
      present, so the same live `NumberTween` glides from its *previous*
      value instead of sweeping from zero — the same smooth behaviour the
      CPU/GPU/RAM tiles (bound directly to scalar properties, never
      recreated) already had.
- [x] Converted `DashboardViewModel.Drives`/`MotherboardVoltages`/
      `MotherboardFanSpeeds`/`MotherboardVrmTemperatures`/`TopProcessesByCpu`
      and `PortraitViewModel.Fans`/`TopCpuProcesses`/`TopGpuProcesses` to
      persistent `ObservableCollection<T>` properties, merged in place every
      poll. Portrait Mode's Fans list has the same bug pattern plus its own
      symptom: a fan being renamed had its `TextBox` fully torn down and
      recreated roughly once a second, discarding an in-progress edit.
- [x] `CollectionEmptyToVisibilityConverter`'s empty-state bindings on those
      same properties would have gone stale (bound to a reference that never
      changes, they'd never re-evaluate again after the first poll) — fixed
      by binding `X.Count` instead of `X` itself (`ObservableCollection<T>`
      raises `PropertyChanged("Count")` on every mutation, which x:Bind's
      dependency-property-path tracking does pick up) and widening the
      converter to accept a plain `int` alongside the original collection form.
- [x] Not independently re-verified live — needs Matt's own check on both
      the crash and the storage/tile flicker.
      **Update:** Matt sent a real ~18s screen recording. Extracted 14 frames
      (`cv2`/OpenCV, no ffmpeg on this machine) and compared them directly —
      every sensor card (temps, loads, RAM, storage, network) was smooth and
      stable across the whole clip; storage in particular never moved off
      978/666/562/530GB once. The `MergeFrom` fix holds. What *was* still
      visibly rotating: **Top Processes** — 5 different near-zero-CPU
      background processes (AsusFanControlService, System, LightingService,
      svchost, firefox...) swapping in and out of the top-5 every 2 seconds
      at idle. Root cause was a threshold, not the container-recreation bug:
      `SampleCpu`/`SampleGpu` let anything above 0.05%/any-nonzero-value
      count as a "top process," so at idle the ranking of five near-zero
      values is close to random — a different process legitimately edges
      into 5th place almost every sample. Raised both to a shared
      `MeaningfulUsageThresholdPercent = 1.0` (GPU: applied after summing a
      process's engine instances, not per-instance, so a process spread
      across several engines each under the floor isn't wrongly dropped).
      Not yet re-verified against a second recording.

## Phase 22 — PDH sampling correctness + median filtering
Matt: "numbers are still bouncing around" (generic, no new screenshot this
round) plus a request to look at comparable open-source tools for how they
read sensors. `FanControl` (Rem0o) turned out to have no public source (its
GitHub repo is releases-only) and `Levminer/cores` is a Rust/Tauri codebase,
neither directly portable — but Microsoft's own `PerformanceCounter` docs
confirmed a real, previously-unaddressed mechanism: a rate-based counter
(which "GPU Engine\Utilization Percentage" is) computes its value from the
delta between *its own* last two `NextValue()` calls, and calling it again
too soon after the previous call — under ~1 second — produces an unstable,
not just stale, result. Two concrete bugs followed from that:
- [x] **GPU Engine counters now sample on their own minimum ~950ms cadence,
      decoupled from the caller.** `HardwareMonitorService`'s poll interval
      is user-configurable down to 250ms (Settings → Dashboard refresh
      rate) — well under what a PDH rate counter needs. `GetTotalEngineUtilization`
      now returns the last good sample instead of re-querying if called
      again too soon, regardless of what refresh rate is configured.
- [x] **Split into two independent `GpuEngineCounterSet`s.** The headline
      GPU% (`GetTotalEngineUtilization`, ~1s cadence) and the Top Processes
      GPU ranking (`SampleGpu`, Portrait Mode's ~2s cadence) were sharing
      the same `PerformanceCounter` objects — since a counter's delta is
      computed from whoever last called it regardless of which caller,
      having Dashboard and Portrait Mode open at the same time would desync
      both readings' effective sampling interval. Now each keeps its own set;
      the extra PDH registrations are cheap (a handful of GPU-active
      processes at most).
- [x] **`MedianFilter` (window of 3) ahead of the existing `EmaSmoother` for
      CPU/GPU load.** A single wrong instantaneous sample still leaks
      partway through EMA alone and takes a couple of seconds to fade — a
      median of the last 3 samples rejects a one-off spike outright (it can
      never be the middle value of three) before smoothing sees it, the same
      technique HWiNFO's sensor smoothing and RTSS use. A real sustained
      level change still reaches the median within 1-2 samples, so this
      doesn't meaningfully add lag on top of what EMA already has.
- [x] **Storage: replaced 8 per-poll WMI round trips with 2 bulk queries.**
      `BuildFreeSpaceByPhysicalDisk` previously ran a separate `ASSOCIATORS
      OF` query per drive per association hop (2 queries × 4 drives on
      Matt's machine, every poll). Now reads the whole of
      `Win32_LogicalDiskToPartition` and `Win32_DiskDriveToDiskPartition`
      once each and matches locally in memory — the same bulk-read pattern
      the Windows Storage Management stack itself uses
      (`Get-Partition`/`Get-Disk`), and it closes the window for any
      cross-query inconsistency mid-poll, which was the one remaining
      unproven suspect after the duplicate-row theory was fixed and the
      dedup guard (kept, now structurally redundant but cheap) never fired
      in a clean 20+ second trace.
- [ ] Not independently re-verified live — needs Matt's own check, and the
      intermittent storage recurrence from Phase 20 still needs a bad-poll
      trace if it happens again (`storage-trace.log` logging stays in place).

## Phase 21 — CPU/GPU % jitter vs. Portrait Stats
Matt's report: GPU% swinging 2→21→2 within a second or two, called out as
"not accurate at all, or stable" compared to Portrait Stats. Diff against
`E:\Portrait Stats\PortraitStats\Services\HardwareMonitorService.cs` showed
the sensor read is identical — same LHM call, same `"GPU Core"`/`"D3D 3D"`
Load sensor, same 1-second poll interval — and Portrait Stats doesn't smooth
it either (`GpuUsagePercent = r.GpuUsagePercent ?? GpuUsagePercent;`, raw).
So the two apps see the exact same real, bursty sensor value; Portrait Stats
isn't reading it "better," it's just not the thing Matt happened to be
watching second-by-second on a fixed dashboard card. `ProcessRankerService`
(top CPU/GPU processes) was already a verbatim port of Portrait Stats' own
Task-Manager-style PDH `GPU Engine` counter approach — identical in both, not
a discrepancy.
- [x] **CPU/GPU utilisation % — smoothed, same `EmaSmoother` already used for
      clock speed and network throughput.** `HardwareMonitorService.Poll()` now
      runs `cpuInfo.UtilisationPercent`/`gpuInfo.UtilisationPercent` through
      their own `EmaSmoother` (alpha 0.15, same as clock) before publishing the
      snapshot. Doesn't change what's real underneath — the raw sensor swings
      just as hard as before — it damps the number Matt actually looks at,
      the same tradeoff already made for clock speed and network.
- [x] **Recurrence with real evidence: a stale binary, not a live bug.** Matt's
      "still jumping" report turned out to be against an exe built ~15:44,
      almost two hours before the smoothing fix landed at 17:37 — `dotnet
      build` on `AetherControl.App` fails in this environment (missing
      `Microsoft.Build.Packaging.Pri.Tasks.dll`, a bare-SDK gap, not present
      when built via Visual Studio's own MSBuild), so the CLI silently
      couldn't produce a fresh exe earlier in the session and nobody had
      rebuilt since. Rebuilding via VS's `MSBuild.exe` directly
      (`-p:Platform=x64`) works around it.
- [x] **GPU% — switched from LHM's ADL "GPU Core" sensor to the same "GPU
      Engine" PDH counters Task Manager itself uses.** Real side-by-side
      against Task Manager (two screenshots, same instants): LHM's GPU Core
      load sensor read ~15% against Task Manager's ~8%, consistently, not
      noise — a genuine measurement difference (LHM/ADL reports overall
      driver-level load across all engines/clocks; Task Manager's headline
      number totals just the "3D" engine instance across processes). Added
      `ProcessRankerService.GetTotalEngineUtilization()`, which sums the
      `engtype_3D` instances of the "GPU Engine" category the same way Task
      Manager does — this reuses the counter set `ProcessRankerService`
      already keeps refreshed for the Top Processes list rather than opening
      a second one. `HardwareMonitorService.Poll()` now uses this as the
      primary source (falling back to the LHM sensor only if this driver
      doesn't expose the counter category at all), still smoothed by the same
      `EmaSmoother`. Also added a lock around `ProcessRankerService`'s
      internal dictionaries — this method is now called from three different
      timers (Dashboard, Portrait Mode, and `HardwareMonitorService`'s own
      1-second poll) against the one singleton instance, and none of its
      collections were thread-safe before.
- [ ] Not independently re-verified live — needs Matt's own check.

## Phase 20 — Storage/network flicker recurrence
Matt's report: HDD free space swinging between ~1100GB and ~784GB, upload
speed "pinging around" — needed to "remain consistent." A ~300GB swing isn't
real disk activity, so this is the same signature as Phase 10's storage bug
(wrong drive's data shown at a fixed card position), recurring despite the
earlier DeviceId-sort fix.
- [x] **Storage — matching is now cached, not re-run every poll.**
      `StorageHealthProbe`'s fuzzy model-name matching (LHM drive ↔ WMI
      `Win32_DiskDrive`) previously ran fresh every single poll — whatever
      made that occasionally unstable (never fully identified; a real trace
      confirmed the *underlying* free-space lookup itself was stable, so the
      instability lives specifically in the match step), it can't matter
      once matching only happens once. First successful match for a given
      LHM identifier is now cached by WMI DeviceID
      (`\\.\PHYSICALDRIVEn`, stable for the OS session); every later poll
      does an exact dictionary lookup instead of fuzzy string matching.
      Re-resolves only if a cached DeviceID stops appearing in current WMI
      results (drive unplugged). Deliberately a structural fix over another
      diagnostic-and-wait round — the exact per-poll trigger was never
      pinned down, but eliminating repeated matching eliminates the
      opportunity for it regardless of what it was.
- [x] **Network upload/download — smoothed, same technique as CPU clock**
      (Phase 10). Real throughput is genuinely bursty (background sync,
      telemetry, prefetch) — a correct "1 Mbps then 40 then 2" per-second
      reading looks identical to a flickering bug on a fixed dashboard card.
      `EmaSmoother` (already built for CPU clock) now damps
      `NetworkMonitorService`'s Upload/DownloadKbps before they're published,
      not chasing a bug that was never actually there.
- [ ] Not independently re-verified live (same computer-use limitation as
      Portrait Mode — not a Start-Menu app) — needs Matt's own check.
- [x] **Recurrence, with real evidence this time.** Two screenshots a few
      seconds apart showed all four physical drives moving *together* —
      637→988GB, 435→680GB, 356→562GB, 335→532GB, each a correlated
      ~1.55-1.59x jump. That rules out the Round 1 theory (wrong drive's data
      shown at a fixed position — a pure identity mix-up wouldn't move four
      distinct drives in the same direction at once) and points at
      `BuildFreeSpaceByPhysicalDisk` itself over-counting: WMI associator
      queries (`Win32_LogicalDiskToPartition`/`Win32_DiskDriveToDiskPartition`)
      are known to occasionally return duplicate rows for the same
      partition/disk pair — a real provider quirk, not something specific to
      this hardware — which would double-count that logical drive's free
      space into its physical disk's total. Fixed by deduplicating: each
      (logical drive, physical disk) pair now contributes its free space at
      most once per poll via a `HashSet` guard, regardless of how many
      duplicate rows WMI returns for it on a given poll. The exact per-poll
      trigger for the duplication was never directly observed (no full trace,
      just the two data points) — flagging that this is the best fix the
      evidence supports, not a confirmed-via-trace root cause. If it recurs,
      the next step is a real diagnostic dump of the raw associator query
      results, not a third theory.
- [x] **Recurred once more (847/584/478/452GB vs. the real 988/684/562/532)
      within a single running session — added `StorageDiagnosticLog`**
      (`%AppData%\Aether Control\storage-trace.log`) logging every drive's
      raw `TotalFreeSpace`, matched partition IDs and disk IDs, and the final
      per-disk total, every poll. A 20+ second live trace right after this
      landed showed the underlying query rock-stable — `\\.\PHYSICALDRIVE0..3`
      read identically (684.32/561.60/532.36/988.24GB, only C: drifting by
      0.01GB from real writes) every single poll, matching the dashboard
      exactly. So `BuildFreeSpaceByPhysicalDisk` itself is confirmed correct
      and deterministic; whatever produced the one bad reading didn't
      reproduce in this window, meaning it's genuinely intermittent rather
      than constant. The logging stays in place (cheap, append-only) so the
      next time Matt sees a wrong number, the exact poll is already on disk
      to read back rather than needing a fresh screenshot.

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
