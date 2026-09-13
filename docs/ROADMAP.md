# Aether Control Roadmap

## Product vision

Aether Control is a vendor-neutral control centre for custom Windows PCs, combining trustworthy hardware monitoring, safe system control, gaming automation, RGB management and a polished secondary-display experience.

The product should feel calmer, clearer and more transparent than traditional OEM utilities. Every displayed value should have a stable identity, a known source, a freshness state and predictable presentation. Every setting changed by Aether Control should be explainable, verifiable and reversible.

## Guiding principles

1. **Trust before breadth**: correct, stable telemetry takes priority over additional controls.
2. **Stable identity**: a hardware card exists for the lifetime of the device, not for the lifetime of one poll result.
3. **One authoritative pipeline**: dashboard, portrait mode, tray, history and alerts consume the same published snapshot.
4. **Incremental UI updates**: routine telemetry changes update properties in place and do not rebuild controls.
5. **Honest states**: unavailable, stale, unsupported and conflicting readings are never represented as zero.
6. **Purposeful animation**: animate meaningful changes only. Never restart an animation because a polling cycle created a new object.
7. **Safe control**: changes to fans, power, startup, RGB and Windows settings must be capability-gated, logged and reversible.
8. **Vendor neutrality**: optional integrations enhance the experience but are not required for the core dashboard.
9. **Progressive complexity**: the default experience is suitable for an average custom-PC owner, with advanced detail available when requested.
10. **Evidence-based completion**: a defect is not closed until the reported behaviour is captured, corrected and protected by regression coverage.

---

# Current status

## Confirmed storage-card defect

The original storage flicker was traced to repeated UI-container replacement rather than incorrect storage data.

### Confirmed mechanism

1. `StorageHealthProbe` creates fresh storage snapshot objects on every poll.
2. `ObservableCollectionMergeExtensions.MergeFrom` compared incoming models with existing models.
3. Reference-based or volatile-value equality caused items to appear changed on every polling cycle.
4. The collection emitted `Replace` operations.
5. `ItemsControl` rebuilt the corresponding `MetricCard` container.
6. The replacement card's `NumberTween` began at zero and animated towards the true value.
7. The visible value passed through approximately half of the target, producing the apparent 680 GB to 340 GB to 680 GB jump.

### Completed corrections

- Added last-known-good storage values for incomplete storage enumeration.
- Added stale-state reporting rather than displaying zero.
- Clamped storage usage percentages to the valid 0 to 100 range.
- Reduced storage WMI work to bulk queries.
- Added value-path diagnostics and UI animation tracing.
- Added pure tween decision logic.
- Added collection merge and tween regression tests.
- Prevented replacement when a snapshot is unchanged.
- Improved model equality to reduce unnecessary storage-card replacement.

### Resolved

The record-equality approach (excluding one volatile field at a time from
`StorageDriveInfo.Equals`) was correctly identified as a losing game rather
than a fix — the system drive became stable, but a second SSD under
continuous write activity kept regenerating its card. The architectural
correction below replaced it entirely, and a live trace confirms it:

- Every drive (four out of four, including the one that previously showed
  454 distinct card instances over 14 minutes) now holds exactly one real
  UI-container identity for the full session.
- Real, sub-perceptible telemetry changes are applied in place with zero
  card recreation.
- `StorageDriveInfo.Equals`/`GetHashCode` overrides were removed entirely —
  the fix no longer depends on model equality at all.
- Temporary diagnostics (`StorageDiagnostics`, `UiValueTraceLog`,
  `MetricCard.DiagnosticTag`) are gated off by default (`TraceEnabled =
  false`) rather than deleted — they're what proved both the cause and the
  fix, and stay available for the next "card flickers" report.

See "Definition of done for the storage issue" below — every listed
criterion is now met.

---

# Phase 31: Finish the storage stability correction

**Status: Complete.** All acceptance criteria below are met and confirmed
via a live trace (`ui-value-trace.log`, ~90 seconds, all 4 drives holding a
single card instance throughout). See "Resolved" under Current status above.

## Objective

Eliminate replacement-driven animation restarts for every drive while preserving genuine telemetry updates.

## Required investigation

Capture the remaining SSD's complete update path:

- Stable device ID
- Old and new snapshot values
- Field-by-field equality differences
- Collection action
- View-model identity
- Card/container identity
- Tween identity
- Tween start value
- Tween target value
- Rendered frame values
- Poll sequence ID
- Snapshot ID

Determine whether replacement is triggered by volatile fields such as:

- Temperature
- Activity
- Read rate
- Write rate
- Health
- Stale status
- Timestamp
- Capacity or free-space precision
- Device metadata ordering

## Architectural correction

Do not continue adding exclusions to record equality until snapshots happen to compare equal.

Use stable presentation objects:

- Keep immutable service-layer snapshots where useful.
- Maintain one `StorageDriveViewModel` per stable device ID.
- Create the view model only when a drive first appears.
- Apply incoming snapshot values to the existing view model.
- Raise `PropertyChanged` only for materially changed properties.
- Use collection `Add` only when a device appears.
- Use collection `Remove` only when a device is genuinely removed.
- Do not emit collection `Replace` for ordinary telemetry changes.
- Keep the same `MetricCard` container for the device's lifetime.
- Keep raw storage values in bytes.
- Apply formatting and animation thresholds in the presentation layer.

## Suggested update contract

```csharp
public interface IIncrementallyUpdatable<in TSnapshot>
{
    string StableId { get; }
    void Apply(TSnapshot snapshot);
}
```

This is a design direction, not a mandatory interface. Use the structure that best fits the existing architecture.

## Acceptance criteria

- Every currently attached drive retains one view-model identity across repeated polls.
- Every currently attached drive retains one card/container identity across repeated polls.
- Temperature, activity and throughput changes do not replace the card.
- A real free-space change updates the existing card.
- An unchanged display target does not restart `NumberTween`.
- No drive repeatedly animates from zero.
- Enumeration order changes do not affect card identity.
- The remaining SSD remains visually stable during live validation.
- Temporary diagnostics capture no unexplained replacements.
- All existing tests continue to pass.

## Regression tests

- Changed temperature does not replace a storage card.
- Changed activity does not replace a storage card.
- Changed read/write throughput does not replace a storage card.
- Changed free space updates the existing view model.
- Same display target does not restart the tween.
- New device creates one card.
- Removed device removes one card.
- Changed enumeration order preserves card identity.
- Stale sample preserves value and changes quality state only.
- Multiple volumes on one physical disk remain correctly associated.

---

# Phase 32: Incremental live-collection architecture

## Objective

Remove replacement churn from all live telemetry collections, not only storage.

## Audit scope

Review collection-update semantics for:

- Storage drives
- Logical volumes
- Physical disks
- Fans
- Named sensors
- CPU devices
- GPU devices
- Network interfaces
- Top processes
- RGB devices
- Plugin widgets
- Portrait-mode metrics

## Required behaviour

- Identity matching is separate from telemetry equality.
- Incoming snapshots update existing presentation objects.
- Collection changes represent actual membership changes only.
- Display controls are not recreated every polling cycle.
- A property animation is scoped to the property that changed.
- Old animation callbacks cannot update a newly selected or repurposed item.
- Removed hardware uses a short absence policy where transient disappearance is possible.

## Acceptance criteria

- No routine polling cycle triggers broad `Replace` actions.
- CPU, GPU, fan and network cards retain identity across polling.
- Process rows are matched by a suitable stable process identity rather than collection position.
- UI allocation and garbage-collection pressure are reduced during monitoring.
- Portrait mode and the main dashboard consume the same stable presentation data.

---

# Phase 33: Telemetry integrity and metric quality

**Status: started — storage done, initial rollout list otherwise pending.**
Added `MetricQuality` (Good/Stale/Unavailable/Unsupported/PermissionRequired/
Conflicting/Disconnected/Error) in `AetherControl.Core.Enums` and applied it
to both storage metrics already named in the initial rollout list below:

- **Storage free space**: `StorageDriveInfo.IsFreeSpaceStale` (a bool)
  generalized to `FreeSpaceQuality: MetricQuality`. `StorageHealthProbe` now
  distinguishes `Stale` (a real prior reading exists, just not confirmed
  this poll) from `Unavailable` (never obtained a reading for this disk at
  all) — previously both cases were folded into a single `true`.
- **Storage temperature**: new `TemperatureQuality` field. Found and fixed a
  real, previously-silent violation of the "never represent unavailable as
  zero" principle — `HardwareSnapshotMapper.MapStorage` used `FindValue`'s
  fallback-to-zero pattern, so a drive with no real SMART temperature
  sensor exposed (common on some external/bridge-chip enclosures) displayed
  a fabricated "0°C" indistinguishable from a genuine reading. Added
  `TryFindValue` (reports whether a sensor was actually found, not inferred
  from the value) and wired `TemperatureQuality = Unsupported` when none is.
  `StorageDriveViewModel.DetailText` now shows "temp n/a" instead of "0°C"
  for these drives.
- Deferred, explicitly out of scope for this pass: CPU temperature, GPU
  temperature, fan RPM, network throughput, RTSS FPS. Each would need
  touching `HardwareSnapshotMapper` (more sensors), `DashboardViewModel`,
  `PortraitViewModel`, `TrayReadoutFormatter`, and `HistoryService`/alerts —
  a materially larger blast radius across code with no currently-known,
  evidenced failure mode (unlike storage, which had two real, live-captured
  bugs this quarter). Rolling the model out further without a specific
  reported problem to anchor each change risks exactly the "destabilise
  working hardware control" outcome this whole roadmap warns against.
- `HistoryService`/alerts consuming `MetricQuality` (rather than just the
  raw metric value) is not yet wired — the model exists, but nothing
  downstream of storage's own display path reads it yet.

## Objective

Create a consistent quality and provenance model for every displayed metric.

## Metric quality states

- `Good`
- `Stale`
- `Unavailable`
- `Unsupported`
- `PermissionRequired`
- `Conflicting`
- `Disconnected`
- `Error`

## Suggested sample model

```csharp
public sealed record MetricSample<T>(
    string MetricId,
    T? Value,
    DateTimeOffset Timestamp,
    MetricQuality Quality,
    string Source,
    TimeSpan? SamplingInterval,
    string? Detail = null);
```

## Required behaviour

- Missing values display as unavailable, not zero.
- Failed polls retain the last valid sample and mark it stale where appropriate.
- Each expanded metric can identify its source.
- Conflicting providers are visible rather than silently averaged.
- Permission problems are distinct from unsupported hardware.
- History does not treat unavailable samples as genuine zero values.
- Alerts ignore invalid or stale samples unless explicitly configured otherwise.

## Initial rollout

Apply the shared quality model to:

1. Storage free space
2. Storage temperature
3. CPU temperature
4. GPU temperature
5. Fan RPM
6. Network throughput
7. RTSS FPS

## Acceptance criteria

- Zero represents a measured zero only.
- Stale values show freshness information.
- Unsupported sensor types use a stable designed state.
- Quality state is available to dashboard, portrait, tray, history and alerts.
- Metric cards use text and icons as well as colour to communicate state.

---

# Phase 34: Storage model and presentation clarity

## Objective

Separate physical storage devices from logical filesystem volumes.

## Physical disk presentation

Show:

- Device model
- Physical capacity
- Temperature
- Activity
- Read rate
- Write rate
- Health, only where reliably supported
- Connection or bus type where reliable
- Associated logical volumes

## Logical volume presentation

Show:

- Friendly label
- Mount point
- File system
- Total capacity
- Used space
- Free space
- Percentage free or used
- Freshness state

## Dashboard recommendation

Display logical volumes as capacity bars:

```text
System (C:)
682 GB free of 1.81 TB
[█████████████░░░░░░░░░]
```

Display physical-device detail on the Hardware page:

```text
Samsung SSD 990 PRO 2TB
44 °C · 3% active
Read 28 MB/s · Write 12 MB/s
Volumes: C:, D:
```

## Required decisions

Document whether free space is:

- Per logical volume
- Aggregated per physical disk
- Displayed only for user-visible volumes
- Excluding hidden recovery/system partitions

## Acceptance criteria

- A physical disk is never ambiguously labelled with a filesystem metric.
- Multi-partition drives are represented accurately.
- Hidden/system partitions do not inflate user-facing free-space totals unless deliberately included and labelled.
- Mapped and removable drives have deliberate inclusion and availability rules.

---

# Phase 35: Animation and motion system

## Objective

Make telemetry feel responsive without constant movement or misleading transitions.

## Animation rules

- Do not animate a value when the semantic target has not changed.
- Do not animate large startup values from zero by default.
- Fade in the first valid reading or display it immediately.
- Cancel the previous animation when a new target is accepted.
- Prevent stale callbacks from writing after a card is unloaded or rebound.
- Use raw numeric values internally and format separately.
- Use a display-specific material-change threshold.
- Respect Windows reduced-motion settings.
- Pause non-essential animation when the application is hidden.
- Prefer restrained capacity-bar animation for storage and memory.
- Use short transitions for utilisation and network rates.
- Avoid animation for tiny storage changes below visible precision.

## `NumberTween` hardening

- Same target does not restart.
- Non-finite values are rejected or represented as unavailable.
- Large `long`-backed measurements maintain precision.
- Old animation frames cannot update a newer target.
- Control unload stops rendering callbacks.
- Rebinding starts from the correct current semantic value.
- No animation writes back into the source model.

## Acceptance criteria

- Storage values no longer count from zero on each poll.
- Reduced-motion mode directly applies accepted values.
- Animations do not create misleading temporary values.
- CPU/GPU cards remain lively without appearing unstable.

---

# Phase 36: Navigation and information architecture

## Objective

Organise the feature set into a clear mental model for an average custom-PC owner.

## Proposed product areas

### Observe

- Dashboard
- Hardware
- Processes
- Network
- History
- Alerts

### Control

- Performance profiles
- Fans
- RGB
- Game automation
- Startup
- Optimisation

### Display

- Portrait dashboard
- Layouts
- Monitor selection
- Visual presets

## Navigation principles

- Use a compact WinUI navigation rail with icons and labels.
- Keep high-frequency destinations visible.
- Avoid unnecessary nested pages.
- Provide a clear current-location state.
- Keep settings and diagnostic tools accessible but secondary.
- Preserve keyboard navigation and correct tab order.
- Prevent clipping at 125%, 150%, 175% and 200% display scaling.

## Acceptance criteria

- Users can understand the product's three purposes at a glance.
- Monitoring and control features are not intermixed without hierarchy.
- Portrait-display configuration has a clear home.
- Navigation remains usable at narrow widths and high scaling.

---

# Phase 37: Dashboard redesign

## Objective

Make the dashboard answer four questions quickly:

1. Is the system healthy?
2. What is under load?
3. Is anything unusual?
4. Which profile is active?

## Proposed hierarchy

### System summary

- Overall system state
- CPU temperature and load
- GPU temperature and load
- Memory usage
- Storage status
- Active profile

### Primary live cards

- CPU
- GPU

Use a radial gauge plus sparkline only where it improves comprehension.

### Secondary cards

- Memory
- Network
- Active game/profile

### Operational sections

- Logical storage volumes
- Fans
- Top processes
- Recent alerts/activity

## Recommended visual mapping

| Metric | Preferred visual |
|---|---|
| CPU/GPU utilisation | Radial gauge plus sparkline |
| CPU/GPU temperature | Prominent value and status |
| Memory | Horizontal capacity bar |
| Storage volumes | Capacity bars |
| Fans | Compact rows with RPM and optional mini-trend |
| Network | Current rates plus sparkline |
| Processes | Ranked list |
| Active profile | Status chip or segmented selector |
| Alerts/activity | Timeline |

## Acceptance criteria

- The dashboard has a clear top-to-bottom hierarchy.
- Radial gauges are reserved for headline metrics.
- Storage, memory and fan information use more appropriate visuals.
- Layout changes do not cause cards to jump during live updates.
- Empty/loading/error states are intentionally designed.

---

# Phase 38: Profile and control UX

## Objective

Make system control safe, understandable and reversible.

## Persistent profile control

Provide a clear profile selector:

```text
[ Quiet ] [ Balanced ] [ Performance ] [ Custom ]
```

Show:

- Active state
- What the profile changes
- Whether it was selected manually or automatically
- Last transition
- Conflict state
- Restore/default action

## Optimisation cards

Every optimisation should show:

- What changes
- Why it may help
- Current state
- Expected trade-off
- Elevation requirement
- Restart requirement
- Last applied time
- Verification result
- Undo or restore action

## Required safety pattern

```text
Inspect → Explain → Apply → Verify → Log → Roll back if verification fails
```

## Acceptance criteria

- No optimisation is presented as a universal performance guarantee.
- All reversible changes expose a restore action.
- Failed application or verification is visible.
- Automatic gaming-profile changes clearly show when and why they occurred.
- Previous system state is restored after the relevant game closes.

---

# Phase 39: Fan-control experience

## Objective

Provide safe, transparent fan control without imitating opaque vendor tools.

## Fan-card information

- Friendly fan name
- Current RPM
- Current duty percentage
- Current control source
- Temperature source
- Active curve/profile
- Safety floor
- Vendor-software conflict state
- BIOS restoration state

## Curve editor

Support:

- Dragging points
- Keyboard editing
- Numeric editing
- Monotonic validation
- Preview
- Apply
- Verify
- Revert
- Reset to BIOS control
- Warning for unsafe values

## Safety requirements

- Preserve the existing minimum safety floor unless a tested safer design replaces it.
- Revert to BIOS control on exit or service failure.
- Detect competing vendor software where possible.
- Do not write unsupported fan headers.
- Log apply, verify, conflict and restoration events.

## Acceptance criteria

- A user can always determine who currently controls each fan.
- An unsuccessful write does not appear successful.
- Exiting Aether Control does not leave fans in an unsafe state.
- The fan page remains useful in read-only mode.

---

# Phase 40: Activity and change centre

## Objective

Build trust by recording what Aether Control changes and why.

## Timeline events

- Manual profile change
- Automatic game-profile activation
- Power-plan change
- Fan-control change
- RGB-profile change
- Optimisation applied
- Verification result
- Reversion completed
- Failed restoration
- Device connected/disconnected
- Sensor became stale/unavailable
- Alert triggered/cleared
- Plugin failure

## Example

```text
21:32  Game detected: Cyberpunk 2077
21:32  Gaming profile activated
21:32  Power plan changed to High Performance
22:48  Game closed
22:48  Previous power plan restored
22:48  Fan control returned to BIOS
```

## Acceptance criteria

- Automatic actions are distinguishable from manual actions.
- Reversible operations show both apply and restore outcomes.
- Sensitive information is not stored unnecessarily.
- The timeline can be filtered by category.
- Failed actions provide a useful next step.

---

# Phase 41: Portrait Display 2.0

## Objective

Turn Portrait Mode into a first-class configurable secondary-display product.

## Core capabilities

- Automatic portrait-monitor detection
- Per-monitor saved layouts
- One-click full-panel placement
- Restore previous window bounds
- Layout lock
- Auto-hide pointer
- Adjustable polling/display interval
- Full-screen startup option
- Reduced-motion mode
- Burn-in mitigation
- Graceful monitor disconnect/reconnect handling

## Widgets

- CPU utilisation and temperature
- GPU utilisation and temperature
- CPU/GPU sparklines
- Memory capacity
- Storage status
- Fan RPM list
- RTSS FPS
- Top CPU processes
- Top GPU processes
- Active profile
- Clock/session duration
- Network rates

## Presets

- Gaming
- Work
- Streaming
- Minimal

## Architecture requirements

- Use the shared hardware snapshot and stable presentation models.
- Do not create an independent hardware polling loop.
- Do not recreate widgets on every sensor update.
- Persist layout by stable monitor identity.
- Handle temporarily unavailable RTSS as an unavailable state, not zero FPS.

## Acceptance criteria

- Portrait mode restores correctly after monitor disconnect or resolution change.
- It remains readable at typical portrait resolutions.
- Widgets retain identity during telemetry updates.
- Layouts can be saved, restored and reset.
- Unavailable metrics do not distort the layout.

---

# Phase 42: Capability-driven experience

## Objective

Show features according to verified capability rather than vendor assumptions.

## Capability areas

- Basic hardware monitoring
- Elevated sensor monitoring
- Fan read
- Fan write
- Direct Corsair RGB
- OpenRGB connection
- RTSS FPS
- Battery monitoring
- Game detection
- Power-plan control
- Windows optimisation
- Toast notifications
- Plugin support

## Required behaviour

- Monitoring does not require elevation unless a specific sensor requires it.
- Elevation is requested only for the action that needs it where practical.
- Unsupported controls are explained rather than shown as broken.
- Optional integrations are discoverable but not mandatory.
- Conflicting control software is identified where supported.

## Acceptance criteria

- Aether Control starts usefully on a system with no optional companion tools.
- Feature availability is clear.
- Unsupported hardware does not generate misleading cards or zeros.
- Capability checks are testable independently from UI presentation.

---

# Phase 43: Visual design system

## Objective

Create an original, consistent visual identity that communicates airflow, energy and precision.

## Suggested palette

- Background: near-black graphite
- Surfaces: blue-black charcoal
- Primary accent: aether blue
- Secondary accent: restrained violet
- Normal telemetry: white and cool grey
- Attention: amber
- Critical: warm red
- Unavailable: desaturated grey

## Style principles

- Thin luminous lines rather than heavy glow
- Subtle airflow or energy contours
- Restrained gradients
- Clear typography
- Consistent 4 px / 8 px spacing system
- Consistent corner radii
- Consistent card elevation
- Strong keyboard focus states
- Minimal decorative animation
- No rainbow-per-sensor palette
- No direct imitation of Armoury Crate, Alienware Command Centre or OmenCore branding

## Required asset inventory

- Main application icon
- Monochrome tray icon
- Tray status variants
- Wordmark
- Dark and light logo variants
- Observe/Control/Display navigation icons
- CPU/GPU/storage/fan/network/device icons
- Metric-quality icons
- Profile icons
- Empty-state illustrations
- Loading glyph
- Warning/error glyphs
- GitHub social preview
- README hero graphic
- Dashboard screenshot
- Portrait screenshot
- Fan-control screenshot
- Optimisation screenshot
- RGB screenshot
- Architecture diagram
- Plugin diagram

## Acceptance criteria

- Tray icons remain legible at small Windows notification-area sizes.
- Assets are actually used and not merely committed as concepts.
- Icon stroke, size and optical weight are consistent.
- High-contrast and scaling behaviour are validated.

---

# Phase 44: Accessibility and scaling

## Objective

Provide a strong Windows accessibility baseline.

## Requirements

- Keyboard navigation
- Visible focus indicators
- Correct tab order
- Accessible names and values
- Screen-reader status announcements
- Colour-independent warning states
- Windows high-contrast support
- Reduced-motion support
- 125%, 150%, 175% and 200% display scaling
- No clipped navigation or title-bar buttons
- No off-screen restored windows
- Appropriate target sizes for touch-capable systems
- Charts and gauges with equivalent textual values

## Acceptance criteria

- Core workflows can be completed without a mouse.
- Current metric and quality state are accessible to screen readers.
- Reduced-motion mode eliminates nonessential tweening.
- No essential meaning is communicated using colour alone.

---

# Phase 45: Cross-repository consolidation

## Objective

Selectively adapt mature patterns from related projects without copying architecture blindly.

## Source projects

- OmenCore
- Portrait Stats
- Radium PCs Companion / PC Companion

## Review areas

### OmenCore

- Timer consolidation
- Hardware identity
- Capability gating
- Fan safety
- Apply/verify/rollback workflows
- Automation rules
- Tray behaviour
- Vertical navigation
- UI performance
- Settings migration
- Field-evidence conventions
- Release validation

### Portrait Stats

- System-drive physical-disk mapping
- Portrait-monitor detection
- Full-panel placement
- Restore previous bounds
- RTSS FPS integration
- Vendor badges
- Sparklines
- Top CPU/GPU processes
- Fan naming
- Unavailable-state handling

### PC Companion / Radium PCs Companion

- Radial gauge design
- Metric cards
- Severity tone conversion
- Animated-number semantics
- Dashboard layout patterns
- Visual assets

## Required comparison output

For each candidate pattern, record:

- Source project
- Source file/class
- Purpose
- Maturity
- Test coverage
- Dependencies
- Already present in Aether Control
- Suitable for direct adaptation
- Required redesign
- Risk
- Decision

## Acceptance criteria

- Every adopted pattern has a clear reason.
- Licensing and attribution requirements are respected.
- Aether Control keeps its own product identity.
- Existing implementations are replaced only where the source pattern is demonstrably stronger.

---

# Phase 46: Performance and polling efficiency

## Objective

Keep the application responsive and lightweight during continuous monitoring.

## Work items

- Measure per-poll duration.
- Measure WMI and LibreHardwareMonitor work separately.
- Verify one authoritative polling timer where practical.
- Batch UI dispatcher updates.
- Reduce unnecessary allocations.
- Avoid collection replacement.
- Bound sparkline history.
- Pause or reduce nonessential work when hidden.
- Use different refresh rates for fast and slow metrics where justified.
- Avoid writing unchanged history samples excessively.
- Measure memory and CPU use during dashboard and portrait operation.

## Acceptance criteria

- No overlapping hardware polls.
- No unbounded event handlers or history buffers.
- Normal telemetry updates do not create avoidable control churn.
- The application remains responsive during long monitoring sessions.
- Performance changes are supported by repeatable measurement.

---

# Phase 47: Testing and release gates

## Objective

Protect telemetry, control and UI behaviour with practical automated and live validation.

## Test categories

### Storage

- Single disk/single volume
- Single disk/multiple volumes
- Multiple disks
- Duplicate WMI associations
- Enumeration-order changes
- Temporarily unavailable drive
- Mapped drive disconnect
- Hidden partition exclusion
- Stale fallback
- Free space never exceeds capacity
- Used percentage remains within bounds

### Stable UI identity

- Same device ID retains the same view model
- Same device ID retains the same card
- Volatile sensor changes do not replace cards
- Device addition/removal uses correct collection actions
- Rebound controls do not inherit stale animations

### Tween and motion

- Same target does not restart
- New target cancels old target
- Unloaded control stops rendering subscription
- Non-finite values do not animate
- Large values maintain precision
- Reduced-motion applies directly

### Metric quality

- Missing is not zero
- Stale retains last-known value
- Unsupported is distinct from permission denied
- Conflicting sources remain visible
- History excludes invalid zero substitutions

### Control safety

- Apply/verify/revert sequence
- Failed verification rolls back
- Fan safety floor
- BIOS restoration
- Game-profile restoration
- Competing software conflict state

### UI

- Navigation state
- High scaling
- Keyboard access
- High contrast
- Empty/loading/error states
- Portrait monitor restoration

## Release gate

A release candidate must have:

- Clean build
- Zero test failures
- No new warnings without documented justification
- Live hardware validation for changed hardware-touching code
- No unexplained card replacement in trace logs
- No absent metric displayed as zero
- Fan and performance restoration verified where changed
- Temporary diagnostic logging removed or explicitly development-gated
- Updated screenshots for visible UI changes
- Updated roadmap and changelog

---

# Phase 48: Documentation and presentation

## Objective

Make the repository understandable to users, contributors and reviewers.

## Documentation work

- Update README feature hierarchy.
- Explain Observe, Control and Display areas.
- Explain sensor quality and stale readings.
- Explain physical disks versus logical volumes.
- Describe fan safety and restoration.
- Describe capability-based feature availability.
- Add current screenshots.
- Add architecture overview.
- Add plugin-development guidance.
- Add troubleshooting for missing sensors and vendor conflicts.
- Add privacy statement for local telemetry and history.
- Document portable and installer builds where applicable.

## Acceptance criteria

- README screenshots reflect the current application.
- Claims match implemented and verified behaviour.
- Hardware-dependent features distinguish implemented, test-verified and field-confirmed status.
- No personal machine paths or diagnostic logs are committed.

---

# Prioritised implementation sequence

## Immediate — complete

1. ✅ Capture and fix the remaining SSD card replacement.
2. ✅ Replace storage record-equality suppression with stable in-place view-model updates.
3. ✅ Validate all drives through repeated live polling.
4. ✅ Retain diagnostics until the final SSD is stable.
5. ✅ Remove or development-gate temporary storage tracing after confirmation (`StorageDiagnostics.TraceEnabled` gated back to `false`, infrastructure retained).

## Next

6. Audit all live collections for replacement churn.
7. Introduce the shared metric-quality model.
8. Separate logical volumes from physical storage devices.
9. Harden `NumberTween` and reduced-motion behaviour.
10. Consolidate dashboard and portrait presentation models.

## Product experience

11. Restructure navigation around Observe, Control and Display.
12. Redesign dashboard hierarchy.
13. Add persistent profile controls.
14. Build the activity/change timeline.
15. Improve fan-control workflows.
16. Upgrade Portrait Display layouts and presets.

## Polish

17. Complete capability-driven states.
18. Apply the Aether visual design system.
19. Complete accessibility and scaling validation.
20. Produce final application, tray and repository assets.
21. Complete cross-repository consolidation.
22. Establish repeatable release gates and updated documentation.

---

# Definition of done for the storage issue

**Status: closed.** All of the following are confirmed true:

- ✅ Every attached drive retains a stable view-model identity (`StorageDriveViewModel`, one per `DeviceId`, created once).
- ✅ Every attached drive retains a stable UI-container identity — confirmed via a live `ui-value-trace.log` capture: all 4 drives held exactly one real `MetricCard` GUID across the full session.
- ✅ No polling cycle restarts a storage tween without a meaningful target change (`TweenState` skips both non-finite and sub-threshold targets; `[ObservableProperty]` setters only raise `PropertyChanged` per field that actually changed).
- ✅ The system drive remains stable.
- ✅ The additional SSD that previously jumped (454 distinct card instances over 14 minutes pre-fix) now remains stable — same trace, zero recreation.
- ✅ Raw bytes, formatted text and progress values agree (`StorageDriveInfo` stays byte-precise; `CapacityGb`/`FreeGb`/`UsedPercent` are the only, single, presentation-layer conversion points).
- ✅ Enumeration order does not affect identity (`LiveCollectionSync` keys by `DeviceId`, not position; regression-tested).
- ✅ Stale or failed reads preserve the last valid value and clearly show quality (`IsFreeSpaceStale`, `DetailText`'s "· stale" suffix — unchanged from Phase 30).
- ✅ Regression tests cover the confirmed cause (47 tests total: `LiveCollectionSyncTests`, `TweenStateTests`, `ObservableCollectionMergeExtensionsTests`, `StorageHealthProbeTests`).
- ✅ Live trace evidence confirms the fix (see above).
- ✅ Temporary diagnostics are development-gated, not removed (`StorageDiagnostics.TraceEnabled`/`AnimationEnabled` default `false`; infrastructure retained for future use).
- ✅ The original 680/340-style oscillation no longer appears — architecturally impossible now, not just unobserved: there is no code path left that tears down and recreates a card for an unchanged or trivially-changed reading.

---

# Definition of product success

Aether Control succeeds when an average custom-PC owner can:

- Understand system health at a glance.
- Trust that displayed values are current and correctly identified.
- See when information is stale, unsupported or unavailable.
- Change a profile confidently.
- Understand what the application changed.
- Restore the previous state easily.
- Monitor a gaming session on a portrait display.
- Use the application without mandatory vendor ecosystems.
- Access advanced controls without making the default experience intimidating.

The target is not to contain more switches than OEM software. The target is to be the clearer, safer and more trustworthy control centre.
