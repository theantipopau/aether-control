using System.Diagnostics;
using System.Management;
using AetherControl.Core.Models;
using Microsoft.Win32;

namespace AetherControl.Services.Optimisation;

/// <summary>
/// Enumerates startup entries via WMI's <c>Win32_StartupCommand</c> (which
/// already unions the HKCU/HKLM Run keys and the Startup folders) and toggles
/// them the same way Task Manager does — by flipping the enable/disable byte
/// in the <c>StartupApproved</c> registry keys rather than deleting the
/// underlying Run value, which keeps the action reversible.
///
/// Impact is estimated from the target executable's on-disk size, since the
/// boot-trace data Task Manager uses for its own impact column isn't exposed
/// through a public API — a simplification, not a claim of parity.
/// </summary>
public sealed class StartupAnalysisService
{
    private const string StartupApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private static readonly byte[] EnabledValue = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] DisabledValue = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public Task<IReadOnlyList<StartupEntry>> GetEntriesAsync(CancellationToken ct = default)
    {
        var entries = new List<StartupEntry>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Command, Location FROM Win32_StartupCommand");
            foreach (ManagementObject item in searcher.Get())
            {
                var name = item["Name"]?.ToString() ?? "Unknown";
                var command = item["Command"]?.ToString() ?? string.Empty;
                var location = item["Location"]?.ToString() ?? string.Empty;
                var isFolderEntry = location.Contains("Startup", StringComparison.OrdinalIgnoreCase);
                var executablePath = ExtractExecutablePath(command);

                entries.Add(new StartupEntry
                {
                    Name = name,
                    Command = command,
                    Location = location,
                    Publisher = GetPublisher(executablePath),
                    ImpactEstimate = EstimateImpact(executablePath),
                    IsEnabled = IsApproved(name, isFolderEntry)
                });
            }
        }
        catch (ManagementException)
        {
            // Falls through with whatever entries were collected before the failure.
        }

        return Task.FromResult<IReadOnlyList<StartupEntry>>(entries);
    }

    public Task SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken ct = default)
    {
        var isFolderEntry = entry.Location.Contains("Startup", StringComparison.OrdinalIgnoreCase);
        var keyPath = isFolderEntry ? StartupApprovedFolderKey : StartupApprovedRunKey;

        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(entry.Name, enabled ? EnabledValue : DisabledValue, RegistryValueKind.Binary);
        return Task.CompletedTask;
    }

    private static bool IsApproved(string name, bool isFolderEntry)
    {
        var keyPath = isFolderEntry ? StartupApprovedFolderKey : StartupApprovedRunKey;
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        if (key?.GetValue(name) is byte[] { Length: > 0 } bytes)
        {
            return bytes[0] == 0x02;
        }

        return true; // No override recorded yet — Windows treats unmanaged entries as enabled.
    }

    private static string EstimateImpact(string? executablePath)
    {
        if (executablePath is null || !File.Exists(executablePath))
        {
            return "Unknown";
        }

        var sizeMb = new FileInfo(executablePath).Length / 1024.0 / 1024.0;
        return sizeMb switch
        {
            > 5 => "High",
            > 1 => "Medium",
            _ => "Low"
        };
    }

    private static string GetPublisher(string? executablePath)
    {
        if (executablePath is null || !File.Exists(executablePath))
        {
            return string.Empty;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath).CompanyName?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static string? ExtractExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 0 ? trimmed[1..closingQuote] : null;
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }
}
