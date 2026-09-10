using System.Diagnostics;
using AetherControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Hardware;

/// <summary>
/// Fan RPM is deliberately not read from <see cref="HardwareMonitorService"/>'s
/// long-lived LibreHardwareMonitor instance. On real ASUS boards,
/// <c>AsusFanControlService</c> reclaims the Super I/O LPC ports right after
/// the first read, so a long-lived instance's fan sensors stick at 0 forever
/// after poll one. Instead this launches <c>AetherControl.FanHelper.exe</c> —
/// a tiny console app that opens a fresh, motherboard-only Computer, prints
/// "Name&#9;Rpm" lines, and exits — on its own short cadence. A fresh process
/// each time means a clean driver load that outruns the contention, and
/// because LibreHardwareMonitor's Ring0/MSR access is global per-process,
/// running it out-of-process guarantees it can never corrupt the main app's
/// own CPU/GPU sensor state either.
/// </summary>
public sealed class FanRpmProbeService : IDisposable
{
    private readonly ILogger<FanRpmProbeService> _logger;
    private readonly string _helperExePath;
    private Timer? _timer;

    public FanRpmProbeService(ILogger<FanRpmProbeService> logger, string? helperExePath = null)
    {
        _logger = logger;
        _helperExePath = helperExePath ?? Path.Combine(AppContext.BaseDirectory, "AetherControl.FanHelper.exe");
    }

    public IReadOnlyList<NamedSensorValue> LatestFanSpeeds { get; private set; } = [];

    public void Start(TimeSpan interval)
    {
        if (!File.Exists(_helperExePath))
        {
            _logger.LogWarning("Fan helper not found at {Path}; motherboard fan RPM will be unavailable", _helperExePath);
            return;
        }

        _timer?.Dispose();
        _timer = new Timer(_ => SafeProbe(), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SafeProbe()
    {
        try
        {
            LatestFanSpeeds = RunHelper();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fan RPM probe failed");
        }
    }

    private List<NamedSensorValue> RunHelper()
    {
        var startInfo = new ProcessStartInfo(_helperExePath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return [];
        }

        var results = new List<NamedSensorValue>();
        string? line;
        while ((line = process.StandardOutput.ReadLine()) is not null)
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && float.TryParse(parts[1], out var rpm))
            {
                results.Add(new NamedSensorValue { Name = parts[0], Value = rpm, Unit = "RPM" });
            }
        }

        process.WaitForExit(2000);
        return results;
    }

    public void Dispose() => Stop();
}
