using AetherControl.Core.Events;
using AetherControl.Core.Fans;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Hardware;
using AetherControl.Services.Optimisation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace AetherControl.App.ViewModels;

public sealed partial class OptimisationViewModel : ObservableObject, IDisposable
{
    private readonly IOptimisationService _optimisationService;
    private readonly IFanControlService _fanControlService;
    private readonly FanCurveController _fanCurveController;
    private readonly FanLabelStore _fanLabelStore;
    private readonly IGameProfileService _gameProfileService;
    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty] private IReadOnlyList<OptimisationTaskDescriptor> tasks = [];
    [ObservableProperty] private IReadOnlyList<StartupEntry> startupEntries = [];
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool createRestorePoint = true;
    [ObservableProperty] private bool gamingProfileEnabled;
    [ObservableProperty] private OptimisationResult? lastMemoryResult;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private IReadOnlyList<FanControlChannel> fanChannels = [];
    [ObservableProperty] private bool isFanConflictWarningVisible;
    [ObservableProperty] private string fanConflictText = string.Empty;
    // Fan-curve state. The controller itself is an app-lifetime singleton (owns the snapshot
    // subscription and hardware writes) — this VM only mirrors its active curve name for
    // bindings and must never dispose or reset it on page navigation away.
    [ObservableProperty] private string? activeCurveName;
    // Index into AvailableFanCurves (Balanced default) — the ComboBox binds SelectedIndex (int↔int)
    // because SelectedItem's object type won't TwoWay-bind to a FanCurve property under x:Bind.
    [ObservableProperty] private int selectedCurvePresetIndex = 1;
    [ObservableProperty] private double curveCpuTempC;
    [ObservableProperty] private int curveTargetPercent;
    [ObservableProperty] private string curveStatusMessage = "Pick a preset and press Apply — the curve then drives every channel from CPU temperature until stopped.";
    [ObservableProperty] private IReadOnlyList<CleanupLocationEntry> cleanupLocations = [];
    [ObservableProperty] private bool isScanningCleanup;
    [ObservableProperty] private bool isCleaning;
    [ObservableProperty] private string cleanupStatusMessage = string.Empty;
    [ObservableProperty] private IReadOnlyList<GameProfile> gameProfiles = [];
    [ObservableProperty] private string newGameProfileName = string.Empty;
    [ObservableProperty] private string newGameProfileExecutable = string.Empty;
    [ObservableProperty] private double memoryUsedGb;
    [ObservableProperty] private double memoryAvailableGb;
    [ObservableProperty] private double memoryTotalGb;
    [ObservableProperty] private double memoryUsedPercent;
    [ObservableProperty] private string memoryUsageText = "Waiting for sensor data…";
    // Vendor badges for section headers: Gaming Profile shows the GPU's brand, Fan Control the
    // motherboard's (PWM headers live on the board). Empty → BrandLogo collapses (unknown brand).
    [ObservableProperty] private string gpuBrandLogo = string.Empty;
    [ObservableProperty] private string boardBrandLogo = string.Empty;

    public OptimisationViewModel(
        IOptimisationService optimisationService,
        IFanControlService fanControlService,
        FanCurveController fanCurveController,
        FanLabelStore fanLabelStore,
        IGameProfileService gameProfileService,
        IHardwareMonitorService hardwareMonitor)
    {
        _optimisationService = optimisationService;
        _fanControlService = fanControlService;
        _fanCurveController = fanCurveController;
        _fanLabelStore = fanLabelStore;
        _gameProfileService = gameProfileService;
        _hardwareMonitor = hardwareMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        Tasks = _optimisationService.GetAvailableTasks();
        _ = LoadStartupEntriesAsync();
        RefreshFanChannels();
        _ = ScanCleanupAsync();

        // A curve started earlier keeps driving across page navigations (the controller is a
        // singleton) — pick up its name so the pill, sliders and readout stay honest.
        ActiveCurveName = _fanCurveController.ActiveCurve?.Name;
        if (ActiveCurveName is not null)
        {
            var match = AvailableFanCurves
                .Select((curve, index) => (curve, index))
                .FirstOrDefault(pair => pair.curve.Name == ActiveCurveName);
            SelectedCurvePresetIndex = match.curve is not null ? match.index : 1;
        }

        GameProfiles = _gameProfileService.GetProfiles();
        _gameProfileService.ProfilesChanged += OnGameProfilesChanged;

        // Live memory context for the Quick Tasks card — the Optimisation page previously showed
        // nothing about the memory it was about to optimise until *after* a run. Same published
        // snapshot every other surface reads (one authoritative pipeline), no extra polling.
        _hardwareMonitor.SnapshotUpdated += OnSnapshotUpdated;
        if (_hardwareMonitor.LatestSnapshot is { } seed)
        {
            ApplyMemory(seed);
        }
    }

    private void OnSnapshotUpdated(object? sender, SensorsUpdatedEventArgs e) =>
        _dispatcherQueue.TryEnqueue(() => ApplyMemory(e.Snapshot));

    private void ApplyMemory(HardwareSnapshot snapshot)
    {
        const double Gb = 1024d * 1024d * 1024d;
        var memory = snapshot.Memory;
        MemoryUsedGb = memory.UsedBytes / Gb;
        MemoryAvailableGb = memory.AvailableBytes / Gb;
        MemoryTotalGb = memory.TotalBytes / Gb;
        MemoryUsedPercent = memory.UtilisationPercent;
        MemoryUsageText = memory.TotalBytes > 0
            ? $"{MemoryUsedGb:F1} GB in use · {MemoryAvailableGb:F1} GB available of {MemoryTotalGb:F1} GB"
            : "Memory readings not available yet.";

        // Live fan-curve readout from the same snapshot — no extra polling. Syncs the active
        // name from the controller (cheap honesty if anything else ever stops it).
        CurveCpuTempC = snapshot.Cpu.TemperatureCelsius;
        var active = _fanCurveController.ActiveCurve;
        ActiveCurveName = active?.Name;
        if (active is not null && CurveCpuTempC > 0)
        {
            CurveTargetPercent = active.Evaluate(CurveCpuTempC);
            CurveStatusMessage = $"Curve \"{active.Name}\" wants {CurveTargetPercent}% at {CurveCpuTempC:F0} °C.";
        }

        // Derived from the same snapshot — no extra polling, same HardwareBrands rules as the
        // Overview (unknown → empty).
        GpuBrandLogo = HardwareBrands.DetectGpu(snapshot.Gpu.Name)?.LogoUri ?? string.Empty;
        BoardBrandLogo = HardwareBrands.DetectMotherboard(snapshot.Motherboard.Model)?.LogoUri ?? string.Empty;
    }

    private void OnGameProfilesChanged(object? sender, EventArgs e) =>
        _dispatcherQueue.TryEnqueue(() => GameProfiles = _gameProfileService.GetProfiles());

    [RelayCommand]
    private async Task AddGameProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGameProfileName) || string.IsNullOrWhiteSpace(NewGameProfileExecutable))
        {
            return;
        }

        await _gameProfileService.AddProfileAsync(NewGameProfileName.Trim(), NewGameProfileExecutable.Trim());
        NewGameProfileName = string.Empty;
        NewGameProfileExecutable = string.Empty;
        GameProfiles = _gameProfileService.GetProfiles();
    }

    [RelayCommand]
    private async Task RemoveGameProfileAsync(GameProfile profile)
    {
        await _gameProfileService.RemoveProfileAsync(profile.Id);
        GameProfiles = _gameProfileService.GetProfiles();
    }

    [RelayCommand]
    private async Task ToggleGameProfileEnabledAsync(GameProfile profile)
    {
        await _gameProfileService.SetEnabledAsync(profile.Id, !profile.IsEnabled);
        GameProfiles = _gameProfileService.GetProfiles();
    }

    public void Dispose()
    {
        _gameProfileService.ProfilesChanged -= OnGameProfilesChanged;
        // Also unsubscribe from the hardware monitor — the page disposes this VM on every
        // navigation away, and nothing else would ever remove the handler.
        _hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;
    }

    [RelayCommand]
    private async Task RunTaskAsync(OptimisationTaskDescriptor task)
    {
        IsRunning = true;
        try
        {
            var result = await _optimisationService.RunAsync(task.Id, CreateRestorePoint);
            StatusMessage = result.Message;
            if (result.ProcessesScanned is not null)
            {
                LastMemoryResult = result;
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task ToggleStartupEntryAsync(StartupEntry entry)
    {
        await _optimisationService.SetStartupEntryEnabledAsync(entry, !entry.IsEnabled);
        await LoadStartupEntriesAsync();
    }

    [RelayCommand]
    private async Task ToggleGamingProfileAsync()
    {
        var result = await _optimisationService.ApplyGamingProfileAsync(!GamingProfileEnabled);
        if (result.Success)
        {
            GamingProfileEnabled = !GamingProfileEnabled;
        }

        StatusMessage = result.Message;
    }

    [RelayCommand]
    private void RefreshFanChannels()
    {
        var contenders = _fanControlService.RunningConflictingSoftware();
        IsFanConflictWarningVisible = contenders.Count > 0;
        FanConflictText = $"{string.Join(", ", contenders)} {(contenders.Count == 1 ? "is" : "are")} also using the motherboard sensor chip. " +
            "A curve set here may be silently overridden, or override it. Close it first for reliable control.";
        var channels = _fanControlService.GetChannels();
        // The Super I/O chip has no concept of header naming ("Fan #2" is LHM's own enumeration
        // order, not what's actually plugged into it) — overlay any name the user has assigned,
        // same technique as Portrait Stats' FanLabelStore.
        foreach (var channel in channels)
        {
            channel.Name = _fanLabelStore.GetLabel(channel.Id, channel.Name);
        }

        FanChannels = channels;
    }

    public void RenameFanChannel(string channelId, string newLabel)
    {
        _fanLabelStore.SetLabel(channelId, newLabel);
        RefreshFanChannels();
    }

    /// <summary>Called directly from the page's Slider.ValueChanged — a per-drag RelayCommand with
    /// two parameters isn't idiomatic MVVM Toolkit, and this is simple enough not to need one.
    /// Ignored while a curve is driving (sliders are disabled then; this is the backstop).</summary>
    public void SetFanPercent(string channelId, int percent)
    {
        if (IsCurveActive)
        {
            return;
        }

        _fanControlService.SetPercent(channelId, percent);
    }

    [RelayCommand]
    private void ResetFanToAutomatic(FanControlChannel channel)
    {
        // A per-channel Auto while the loop drives every channel would be re-overridden on the
        // next tick — stop the curve instead of silently lying about who's in control.
        if (IsCurveActive)
        {
            StopFanCurve();
            CurveStatusMessage = $"Curve stopped — {channel.Name} and every channel back to BIOS control.";
            return;
        }

        _fanControlService.ResetToAutomatic(channel.Id);
        RefreshFanChannels();
    }

    [RelayCommand]
    private void ResetAllFansToAutomatic()
    {
        if (IsCurveActive)
        {
            StopFanCurve();
            CurveStatusMessage = "Curve stopped — all channels back to BIOS control.";
            return;
        }

        _fanControlService.ResetAllToAutomatic();
        RefreshFanChannels();
    }

    /// <summary>Starts the selected preset on every channel. Refuses while software control is
    /// unsafe (second Super I/O owner) rather than writing into scrambled registers.</summary>
    [RelayCommand]
    private void StartFanCurve()
    {
        var preset = SelectedCurvePreset;
        if (FanChannels.Count == 0)
        {
            return;
        }

        if (!_fanControlService.IsSoftwareControlSafe)
        {
            CurveStatusMessage = "Software fan control isn't safe right now — readings suggest another process is touching the Super I/O chip. Curve not started.";
            return;
        }

        try
        {
            _fanCurveController.Start(preset);
        }
        catch (Exception ex)
        {
            CurveStatusMessage = $"Could not start the \"{preset.Name}\" curve: {ex.Message}";
            return;
        }

        ActiveCurveName = preset.Name;
        RefreshFanChannels();
    }

    // Named StopActiveFanCurve (not StopFanCurveCommand): [RelayCommand] appends "Command" to
    // the method name, so the latter would generate StopFanCurveCommandCommand while XAML binds
    // StopFanCurveCommand — resolving to this method, which x:Bind rejects (WMC1121).
    [RelayCommand]
    private void StopActiveFanCurve()
    {
        StopFanCurve();
        CurveStatusMessage = "Curve stopped — all channels back to BIOS control.";
    }

    private void StopFanCurve()
    {
        _fanCurveController.Stop(); // itself hands every channel back to automatic/BIOS control
        ActiveCurveName = null;
        RefreshFanChannels();
    }

    public bool IsCurveActive => ActiveCurveName is not null;

    /// <summary>Manual sliders and per-channel Auto buttons only make sense when no curve is
    /// driving — bound to their IsEnabled so there is no inverse-bool converter to maintain.</summary>
    public bool IsManualFanControlEnabled => !IsCurveActive;

    public IReadOnlyList<FanCurve> AvailableFanCurves { get; } =
    [
        FanCurvePresets.Silent,
        FanCurvePresets.Balanced,
        FanCurvePresets.Performance
    ];

    /// <summary>The preset the ComboBox points at (index clamped — never null).</summary>
    public FanCurve SelectedCurvePreset => AvailableFanCurves[Math.Clamp(SelectedCurvePresetIndex, 0, AvailableFanCurves.Count - 1)];

    /// <summary>Read-only truth about the selected preset ("30°→20% · 50°→30% …") — straight from
    /// the same FanCurve objects the controller drives, so the label can never drift from behaviour.</summary>
    public string SelectedCurveSummary =>
        string.Join(" · ", SelectedCurvePreset.Points.Select(p => $"{p.TemperatureC:F0}°→{p.Percent}%"));

    partial void OnActiveCurveNameChanged(string? value)
    {
        OnPropertyChanged(nameof(IsCurveActive));
        OnPropertyChanged(nameof(IsManualFanControlEnabled));
    }

    partial void OnSelectedCurvePresetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedCurvePreset));
        OnPropertyChanged(nameof(SelectedCurveSummary));
    }

    [RelayCommand]
    private async Task ScanCleanupAsync()
    {
        IsScanningCleanup = true;
        try
        {
            var previews = await _optimisationService.ScanCleanupLocationsAsync();
            CleanupLocations = previews.Select(p => new CleanupLocationEntry(p)).ToList();
        }
        finally
        {
            IsScanningCleanup = false;
        }
    }

    [RelayCommand]
    private async Task CleanSelectedLocationsAsync()
    {
        var selectedPaths = CleanupLocations.Where(c => c.IsSelected).Select(c => c.Path).ToList();
        if (selectedPaths.Count == 0)
        {
            CleanupStatusMessage = "Nothing selected.";
            return;
        }

        IsCleaning = true;
        try
        {
            var result = await _optimisationService.CleanLocationsAsync(selectedPaths);
            CleanupStatusMessage = result.Message;
            await ScanCleanupAsync(); // sizes just changed — refresh rather than leave stale numbers on screen
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private async Task LoadStartupEntriesAsync()
    {
        StartupEntries = await _optimisationService.GetStartupEntriesAsync();
    }

    public bool HasFanChannels => FanChannels.Count > 0;

    partial void OnFanChannelsChanged(IReadOnlyList<FanControlChannel> value)
    {
        OnPropertyChanged(nameof(HasFanChannels));
    }

    public int StartupEnabledCount => StartupEntries.Count(e => e.IsEnabled);
    public int StartupHighImpactCount => StartupEntries.Count(e => e.IsEnabled && e.ImpactEstimate == "High");
    public int StartupTotalCount => StartupEntries.Count;

    partial void OnStartupEntriesChanged(IReadOnlyList<StartupEntry> value)
    {
        OnPropertyChanged(nameof(StartupEnabledCount));
        OnPropertyChanged(nameof(StartupHighImpactCount));
        OnPropertyChanged(nameof(StartupTotalCount));
    }

    // Plain, non-nullable convenience properties for MetricCard's NumericValue (which can't bind
    // directly to LastMemoryResult's nullable fields) — refreshed whenever LastMemoryResult changes.
    public double MemoryBeforeGb => LastMemoryResult?.MemoryBeforeGb ?? 0;
    public double MemoryAfterGb => LastMemoryResult?.MemoryAfterGb ?? 0;
    public double MemoryFreedGb => Math.Max(0, MemoryBeforeGb - MemoryAfterGb);
    public double ProcessesTrimmed => LastMemoryResult?.ProcessesTrimmed ?? 0;
    public double ProcessesScanned => LastMemoryResult?.ProcessesScanned ?? 0;

    partial void OnLastMemoryResultChanged(OptimisationResult? value)
    {
        OnPropertyChanged(nameof(MemoryBeforeGb));
        OnPropertyChanged(nameof(MemoryAfterGb));
        OnPropertyChanged(nameof(MemoryFreedGb));
        OnPropertyChanged(nameof(ProcessesTrimmed));
        OnPropertyChanged(nameof(ProcessesScanned));
    }
}
