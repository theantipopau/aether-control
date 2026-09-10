using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Optimisation;

public sealed class OptimisationService(
    ILogger<OptimisationService> logger,
    WindowsCleanupService cleanupService,
    StartupAnalysisService startupAnalysisService,
    RestorePointService restorePointService,
    GamingProfileService gamingProfileService,
    OptimisationLogRepository logRepository) : IOptimisationService
{
    private const string StandbyMemoryTaskId = "memory.clear-standby";
    private const string SelectedCleanupTaskId = "cleanup.selected-locations";

    private bool _gamingProfileActive;

    public IReadOnlyList<OptimisationTaskDescriptor> GetAvailableTasks() =>
    [
        new()
        {
            Id = StandbyMemoryTaskId,
            Title = "Optimise memory",
            Description = "Trims every accessible process's working set, then purges the standby page list Windows is holding speculatively. Never terminates or otherwise interferes with any process.",
            Category = OptimisationCategory.Memory,
            RequiresElevation = false,
            IsReversible = true
        }
        // Temp/cache cleanup used to be a single blind "clean everything" task here. It's now the
        // Storage Cleaner section below instead — scan, see each location's actual size, choose
        // which to clear — since deleting unknown-sized files with no preview is exactly the kind
        // of thing a "premium" tool doesn't do blind.
    ];

    public async Task<OptimisationResult> RunAsync(string taskId, bool createRestorePoint, CancellationToken ct = default)
    {
        string? restorePointDescription = null;
        if (createRestorePoint)
        {
            var description = $"Aether Control — before {taskId}";
            if (restorePointService.TryCreate(description, out var failureReason))
            {
                restorePointDescription = description;
            }
            else
            {
                logger.LogWarning("Restore point creation failed: {Reason}", failureReason);
            }
        }

        var result = taskId switch
        {
            StandbyMemoryTaskId => RunClearStandbyMemory(),
            _ => new OptimisationResult { Success = false, Message = $"Unknown task '{taskId}'." }
        };

        result.RestorePointDescription = restorePointDescription;
        await logRepository.LogAsync(taskId, result.Success, result.Message, result.BytesReclaimed, restorePointDescription, ct)
            .ConfigureAwait(false);

        return result;
    }

    public Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken ct = default)
        => startupAnalysisService.GetEntriesAsync(ct);

    public Task SetStartupEntryEnabledAsync(StartupEntry entry, bool enabled, CancellationToken ct = default)
        => startupAnalysisService.SetEnabledAsync(entry, enabled, ct);

    public Task<OptimisationResult> ApplyGamingProfileAsync(bool enable, CancellationToken ct = default)
    {
        var success = enable ? gamingProfileService.Enable() : gamingProfileService.Disable();
        _gamingProfileActive = enable && success;

        return Task.FromResult(new OptimisationResult
        {
            Success = success,
            Message = success
                ? (enable ? "Gaming profile applied." : "Gaming profile reverted.")
                : "One or more gaming profile settings could not be changed."
        });
    }

    private OptimisationResult RunClearStandbyMemory()
    {
        var beforeGb = RamTrimmer.GetUsedMemoryGb();
        var (trimmed, total) = RamTrimmer.TrimAllWorkingSets();

        string? standbyError = null;
        try
        {
            NativeMemoryMethods.PurgeStandbyList();
        }
        catch (InvalidOperationException ex)
        {
            standbyError = ex.Message;
        }

        // Give Windows a moment to actually reclaim the pages we just freed before re-measuring —
        // matches Radium PCs Companion's own settle delay around the equivalent operation.
        Thread.Sleep(600);

        var afterGb = RamTrimmer.GetUsedMemoryGb();
        var freedGb = Math.Max(0, beforeGb - afterGb);

        var message = standbyError is null
            ? $"Trimmed {trimmed}/{total} accessible process working sets and cleared the standby list. {freedGb:F2} GB reported freed."
            : $"Trimmed {trimmed}/{total} accessible process working sets. Standby list clear needs administrator privileges ({standbyError}). {freedGb:F2} GB reported freed.";

        return new OptimisationResult
        {
            Success = true,
            Message = message,
            BytesReclaimed = (long)(freedGb * 1024 * 1024 * 1024),
            MemoryBeforeGb = beforeGb,
            MemoryAfterGb = afterGb,
            ProcessesScanned = total,
            ProcessesTrimmed = trimmed
        };
    }

    public Task<IReadOnlyList<CleanupLocationPreview>> ScanCleanupLocationsAsync(CancellationToken ct = default)
    {
        // Directory enumeration for a large browser cache can take real time — never block the
        // UI thread computing sizes just to render a list of checkboxes.
        return Task.Run<IReadOnlyList<CleanupLocationPreview>>(() =>
            cleanupService.GetLocations()
                .Select(location => new CleanupLocationPreview
                {
                    Name = location.Name,
                    Path = location.Path,
                    SizeBytes = cleanupService.CalculateReclaimableBytes([location]),
                    SafeToDeleteWhileRunning = location.SafeToDeleteWhileRunning
                })
                .ToList(),
            ct);
    }

    public async Task<OptimisationResult> CleanLocationsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        var selected = cleanupService.GetLocations().Where(l => paths.Contains(l.Path)).ToList();
        var freed = await Task.Run(() => cleanupService.Clean(selected), ct);

        var result = new OptimisationResult
        {
            Success = true,
            Message = $"Freed {freed / 1024.0 / 1024.0:F1} MB from {selected.Count} selected location(s).",
            BytesReclaimed = freed
        };

        await logRepository.LogAsync(SelectedCleanupTaskId, result.Success, result.Message, result.BytesReclaimed, restorePointDescription: null, ct)
            .ConfigureAwait(false);

        return result;
    }
}
