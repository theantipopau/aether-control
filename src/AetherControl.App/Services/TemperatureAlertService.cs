using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;
using AetherControl.Services.Hardware;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AetherControl.App.Services;

/// <summary>
/// Fires a Windows toast the moment CPU or GPU temperature crosses its configured threshold.
/// Edge-triggered with hysteresis, not "notify on every poll while above threshold" — without
/// that, a CPU sitting right at 85°C would spam a new toast every second. Requires
/// <see cref="AppNotificationManager.Register"/> to have been called once at app startup
/// (see <c>App.xaml.cs</c>) — unpackaged WinUI3 apps need that before <c>Show</c> will work.
/// </summary>
public sealed class TemperatureAlertService : IDisposable
{
    // Must drop this far below the threshold before re-arming — prevents a temperature
    // oscillating by a fraction of a degree right at the line from re-firing every poll.
    private const double HysteresisCelsius = 5;

    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly AlertSettingsStore _settingsStore;
    private bool _cpuTripped;
    private bool _gpuTripped;

    public TemperatureAlertService(IHardwareMonitorService hardwareMonitor, AlertSettingsStore settingsStore)
    {
        _hardwareMonitor = hardwareMonitor;
        _settingsStore = settingsStore;
        _hardwareMonitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    private void OnSnapshotUpdated(object? sender, SensorsUpdatedEventArgs e)
    {
        var settings = _settingsStore.Current;
        if (!settings.Enabled)
        {
            return;
        }

        CheckThreshold(e.Snapshot.Cpu.TemperatureCelsius, settings.CpuTemperatureThreshold, "CPU", ref _cpuTripped);
        CheckThreshold(e.Snapshot.Gpu.TemperatureCelsius, settings.GpuTemperatureThreshold, "GPU", ref _gpuTripped);
    }

    private void CheckThreshold(double currentTemp, double threshold, string label, ref bool tripped)
    {
        if (!tripped && currentTemp >= threshold)
        {
            tripped = true;
            ShowAlert($"{label} running hot", $"{label} temperature has reached {currentTemp:F0}°C (threshold {threshold:F0}°C).");
        }
        else if (tripped && currentTemp <= threshold - HysteresisCelsius)
        {
            tripped = false;
        }
    }

    private static void ShowAlert(string title, string message)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // A failed toast (e.g. notifications disabled in Windows Settings) must never take
            // down the hardware-monitoring loop this is riding on.
        }
    }

    public void Dispose() => _hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;
}
