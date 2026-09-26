using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherControl.App.ViewModels;

/// <summary>
/// Backs the Diagnostics page. Answers, without a debugger attached, the exact questions this
/// session's audit kept needing a live capture to answer by hand: which LibreHardwareMonitorLib
/// version is actually loaded, is the process really elevated, and how many sensors of each kind
/// did this board's Super I/O chip actually report. "Export support bundle" packages that plus the
/// latest full snapshot and recent log files into one zip a user can attach to a bug report, instead
/// of walking them through opening %APPDATA% by hand.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly string _logDirectory;

    [ObservableProperty] private string appVersion = string.Empty;
    [ObservableProperty] private string libreHardwareMonitorVersion = string.Empty;
    [ObservableProperty] private string osDescription = string.Empty;
    [ObservableProperty] private bool isElevated;
    [ObservableProperty] private string cpuName = "Not detected";
    [ObservableProperty] private string gpuName = "Not detected";
    [ObservableProperty] private string motherboardModel = "Not detected";
    [ObservableProperty] private int voltageSensorCount;
    [ObservableProperty] private int fanSensorCount;
    [ObservableProperty] private int temperatureSensorCount;
    [ObservableProperty] private int driveCount;
    [ObservableProperty] private string logDirectoryDisplayPath = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string superIoPoisonText = "—";
    [ObservableProperty] private string motherboardAgeText = "—";
    [ObservableProperty] private string conflictingSoftwareText = "None detected";

    private readonly IFanControlService _fanControl;

    public DiagnosticsViewModel(IHardwareMonitorService hardwareMonitor, IFanControlService fanControl)
    {
        _hardwareMonitor = hardwareMonitor;
        _fanControl = fanControl;
        _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control", "logs");
        LogDirectoryDisplayPath = _logDirectory;

        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        // Reads the loaded assembly's own version rather than the csproj's <PackageReference> version
        // string — this is the one number that can't silently drift from what's actually running.
        LibreHardwareMonitorVersion = typeof(LibreHardwareMonitor.Hardware.Computer).Assembly.GetName().Version?.ToString() ?? "Unknown";
        OsDescription = RuntimeInformation.OSDescription;
        IsElevated = CheckIsElevated();

        RefreshFromSnapshot(_hardwareMonitor.LatestSnapshot);
    }

    private void RefreshFromSnapshot(HardwareSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        CpuName = string.IsNullOrWhiteSpace(snapshot.Cpu.Name) ? "Not detected" : snapshot.Cpu.Name;
        GpuName = string.IsNullOrWhiteSpace(snapshot.Gpu.Name) ? "Not detected" : snapshot.Gpu.Name;
        MotherboardModel = string.IsNullOrWhiteSpace(snapshot.Motherboard.Model) ? "Not detected" : snapshot.Motherboard.Model;
        VoltageSensorCount = snapshot.Motherboard.Voltages.Count;
        FanSensorCount = snapshot.Motherboard.FanSpeeds.Count;
        TemperatureSensorCount = snapshot.Motherboard.VrmTemperatures.Count;
        DriveCount = snapshot.Drives.Count;

        // Surfaced here because the fallback to last-known-good values hides poisoned Super I/O
        // reads everywhere else in the UI — a live log (2026-09-25) showed ~340 reopen cycles/hour
        // that nothing on screen gave any hint of.
        SuperIoPoisonText = snapshot.SuperIoPolls == 0
            ? "No Super I/O reads yet"
            : $"{snapshot.SuperIoPoisonedPolls:N0} of {snapshot.SuperIoPolls:N0} ({100.0 * snapshot.SuperIoPoisonedPolls / snapshot.SuperIoPolls:F1}%)";
        MotherboardAgeText = $"{snapshot.MotherboardAge.TotalSeconds:F0} s";

        try
        {
            var running = _fanControl.RunningConflictingSoftware();
            ConflictingSoftwareText = running.Count == 0 ? "None detected" : string.Join(", ", running);
        }
        catch
        {
            ConflictingSoftwareText = "Unknown";
        }
    }

    private static bool CheckIsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // Best-effort — a failure here (e.g. sandboxed context) shouldn't stop the rest of the
            // page from rendering, it should just report "unknown" via the false default.
            return false;
        }
    }

    [RelayCommand]
    private void ExportSupportBundle()
    {
        RefreshFromSnapshot(_hardwareMonitor.LatestSnapshot);

        try
        {
            var bundleDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aether Control");
            Directory.CreateDirectory(bundleDirectory);
            var zipPath = Path.Combine(bundleDirectory, $"AetherControl-SupportBundle-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");

            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, "capability-report.txt", BuildCapabilityReport());

                var snapshot = _hardwareMonitor.LatestSnapshot;
                if (snapshot is not null)
                {
                    WriteTextEntry(archive, "latest-snapshot.json", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
                }

                foreach (var logFile in RecentLogFiles())
                {
                    archive.CreateEntryFromFile(logFile, Path.Combine("logs", Path.GetFileName(logFile)));
                }
            }

            StatusMessage = $"Support bundle saved to {zipPath}";
            RevealInExplorer(zipPath);
        }
        catch (Exception ex)
        {
            // A failed export is disappointing, never fatal — this command must not be able to take
            // the app down, the same convention as every other best-effort I/O path in this codebase.
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private IEnumerable<string> RecentLogFiles()
    {
        if (!Directory.Exists(_logDirectory))
        {
            return [];
        }

        // Last 3 days' worth is enough to catch a just-reproduced issue without bloating the bundle
        // with weeks of unrelated history.
        return Directory.EnumerateFiles(_logDirectory, "aether-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(3);
    }

    private string BuildCapabilityReport()
    {
        var snapshot = _hardwareMonitor.LatestSnapshot;
        return $"""
            Aether Control support bundle
            Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}

            App version: {AppVersion}
            LibreHardwareMonitorLib version: {LibreHardwareMonitorVersion}
            OS: {OsDescription}
            Running elevated: {IsElevated}

            CPU: {CpuName}
            GPU: {GpuName}
            Motherboard: {MotherboardModel}
            Voltage sensors reported: {VoltageSensorCount}
            Fan sensors reported: {FanSensorCount}
            Temperature sensors reported: {TemperatureSensorCount}
            Drives detected: {DriveCount}
            Super I/O poisoned reads: {SuperIoPoisonText}
            Motherboard data age: {MotherboardAgeText}
            Vendor software sharing the sensor chip: {ConflictingSoftwareText}
            Latest snapshot timestamp (UTC): {(snapshot is null ? "no snapshot yet" : snapshot.TimestampUtc.ToString("O"))}
            """;
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void RevealInExplorer(string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch
        {
            // Purely a convenience — the status message already told the user where the file is.
        }
    }
}
