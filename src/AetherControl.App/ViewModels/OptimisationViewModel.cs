using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Hardware;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace AetherControl.App.ViewModels;

public sealed partial class OptimisationViewModel : ObservableObject, IDisposable
{
    private readonly IOptimisationService _optimisationService;
    private readonly IFanControlService _fanControlService;
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
        FanLabelStore fanLabelStore,
        IGameProfileService gameProfileService,
        IHardwareMonitorService hardwareMonitor)
    {
        _optimisationService = optimisationService;
        _fanControlService = fanControlService;
        _fanLabelStore = fanLabelStore;
        _gameProfileService = gameProfileService;
        _hardwareMonitor = hardwareMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        Tasks = _optimisationService.GetAvailableTasks();
        _ = LoadStartupEntriesAsync();
        RefreshFanChannels();
        _ = ScanCleanupAsync();

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
        IsFanConflictWarningVisible = _fanControlService.IsConflictingVendorSoftwareRunning();
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
    /// two parameters isn't idiomatic MVVM Toolkit, and this is simple enough not to need one.</summary>
    public void SetFanPercent(string channelId, int percent) => _fanControlService.SetPercent(channelId, percent);

    [RelayCommand]
    private void ResetFanToAutomatic(FanControlChannel channel)
    {
        _fanControlService.ResetToAutomatic(channel.Id);
        RefreshFanChannels();
    }

    [RelayCommand]
    private void ResetAllFansToAutomatic()
    {
        _fanControlService.ResetAllToAutomatic();
        RefreshFanChannels();
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
