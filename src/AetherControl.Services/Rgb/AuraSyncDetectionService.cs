using System.Diagnostics;
using Microsoft.Win32;

namespace AetherControl.Services.Rgb;

/// <summary>
/// Detects an existing ASUS Aura Sync / Armoury Crate Lighting Service
/// installation and can launch it, per the spec's explicit instruction not
/// to reimplement Aura's proprietary device control.
/// </summary>
internal static class AuraSyncDetectionService
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static bool IsInstalled() => TryFindInstallPath() is not null;

    public static void Launch()
    {
        var path = TryFindInstallPath();
        if (path is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string? TryFindInstallPath()
    {
        foreach (var root in UninstallRoots)
        {
            using var uninstallKey = Registry.LocalMachine.OpenSubKey(root);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                if (displayName is null)
                {
                    continue;
                }

                if (displayName.Contains("Aura", StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains("Armoury Crate", StringComparison.OrdinalIgnoreCase))
                {
                    var installLocation = subKey?.GetValue("InstallLocation") as string;
                    var candidate = FindLauncherExecutable(installLocation);
                    if (candidate is not null)
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static string? FindLauncherExecutable(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path).Contains("Aura", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(path).Contains("ArmouryCrate", StringComparison.OrdinalIgnoreCase));
    }
}
