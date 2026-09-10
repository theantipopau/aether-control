using System.Diagnostics;
using System.Text.Json;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Optimisation;

/// <summary>Ported in spirit from OmenCore's <c>GameProfileService</c>, but not its exact
/// mechanism — OmenCore uses a WMI process-start trace via a dedicated <c>ProcessMonitoringService</c>;
/// this uses a simple periodic <see cref="Process.GetProcesses"/> diff instead, matching the
/// technique <see cref="Processes.ProcessRankerService"/> already uses elsewhere in this codebase,
/// since a 3-second detection latency is more than good enough for "apply gaming profile" and
/// avoids a second process-watching mechanism in the same app.</summary>
public sealed class GameProfileService : IGameProfileService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(3);

    private readonly IOptimisationService _optimisationService;
    private readonly ILogger<GameProfileService> _logger;
    private readonly string _path;
    private readonly List<GameProfile> _profiles;
    private readonly HashSet<string> _runningProfileIds = [];
    private Timer? _timer;

    public event EventHandler? ProfilesChanged;

    public GameProfileService(IOptimisationService optimisationService, ILogger<GameProfileService> logger)
    {
        _optimisationService = optimisationService;
        _logger = logger;

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "game-profiles.json");
        _profiles = Load();
    }

    public void Start() => _timer ??= new Timer(_ => SafeScan(), null, TimeSpan.Zero, ScanInterval);

    public IReadOnlyList<GameProfile> GetProfiles() => _profiles;

    public async Task AddProfileAsync(string name, string executableName, CancellationToken ct = default)
    {
        _profiles.Add(new GameProfile { Name = name, ExecutableName = NormalizeExecutableName(executableName) });
        await SaveAsync(ct);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveProfileAsync(string id, CancellationToken ct = default)
    {
        _profiles.RemoveAll(p => p.Id == id);
        _runningProfileIds.Remove(id);
        await SaveAsync(ct);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        profile.IsEnabled = enabled;
        await SaveAsync(ct);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SafeScan()
    {
        try
        {
            Scan();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Game profile scan failed");
        }
    }

    private void Scan()
    {
        var enabledProfiles = _profiles.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ExecutableName)).ToList();
        if (enabledProfiles.Count == 0)
        {
            return;
        }

        var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    runningNames.Add(process.ProcessName);
                }
                catch
                {
                    // Access denied (protected system process) or already exited — skip.
                }
            }
        }

        var nowRunning = new HashSet<string>();
        foreach (var profile in enabledProfiles)
        {
            var isRunning = runningNames.Contains(profile.ExecutableName);
            profile.IsRunning = isRunning;
            if (isRunning)
            {
                nowRunning.Add(profile.Id);
            }
        }

        var wasAnyRunning = _runningProfileIds.Count > 0;
        var isAnyRunningNow = nowRunning.Count > 0;
        var changed = !nowRunning.SetEquals(_runningProfileIds);

        // Gaming Profile is a single on/off switch, not per-profile — apply it the moment the
        // *first* tracked game launches, revert it only once *every* tracked game has exited, so
        // two tracked games launched together don't fight over toggling it back off prematurely.
        if (!wasAnyRunning && isAnyRunningNow)
        {
            _ = _optimisationService.ApplyGamingProfileAsync(true);
            _logger.LogInformation("Game launch detected — Gaming Profile applied automatically");
        }
        else if (wasAnyRunning && !isAnyRunningNow)
        {
            _ = _optimisationService.ApplyGamingProfileAsync(false);
            _logger.LogInformation("All tracked games exited — Gaming Profile reverted automatically");
        }

        _runningProfileIds.Clear();
        foreach (var id in nowRunning)
        {
            _runningProfileIds.Add(id);
        }

        if (changed)
        {
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string NormalizeExecutableName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }

    private List<GameProfile> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<GameProfile>>(json) ?? [];
            }
        }
        catch
        {
            // Corrupt or unreadable — start fresh rather than crash the app over this.
        }

        return [];
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(_profiles), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save game profiles");
        }
    }

    public void Dispose() => _timer?.Dispose();
}
