using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace AetherControl.Services.Optimisation;

/// <summary>
/// The reversible half of "Gaming Optimisation": flips Windows' own Game Mode
/// toggle, switches to the High Performance power plan (remembering the prior
/// scheme so it can be restored), and raises the current foreground
/// application's process priority for the session. Nothing here is
/// permanent — disabling the profile puts every setting back exactly where
/// it was found.
/// </summary>
public sealed class GamingProfileService(ILogger<GamingProfileService> logger)
{
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private static readonly Guid HighPerformancePlan = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private Guid? _previousPowerPlan;
    private int? _boostedProcessId;
    private ProcessPriorityClass? _previousPriority;

    public bool Enable()
    {
        try
        {
            SetGameModeRegistryValue(enabled: true);
            _previousPowerPlan = GetActivePowerPlan();
            SetActivePowerPlan(HighPerformancePlan);
            BoostForegroundProcess();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to fully apply gaming profile");
            return false;
        }
    }

    public bool Disable()
    {
        try
        {
            SetGameModeRegistryValue(enabled: false);
            if (_previousPowerPlan is { } previousPlan)
            {
                SetActivePowerPlan(previousPlan);
                _previousPowerPlan = null;
            }

            RestoreForegroundProcessPriority();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to fully revert gaming profile");
            return false;
        }
    }

    private static void SetGameModeRegistryValue(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(GameBarKey, writable: true);
        key.SetValue("AutoGameModeEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    private void BoostForegroundProcess()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return;
        }

        GetWindowThreadProcessId(handle, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            _boostedProcessId = process.Id;
            _previousPriority = process.PriorityClass;
            process.PriorityClass = ProcessPriorityClass.High;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Foreground process exited or is a protected system process — nothing to boost.
        }
    }

    private void RestoreForegroundProcessPriority()
    {
        if (_boostedProcessId is not { } processId || _previousPriority is not { } priority)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.PriorityClass = priority;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
        finally
        {
            _boostedProcessId = null;
            _previousPriority = null;
        }
    }

    private static Guid GetActivePowerPlan()
    {
        using var process = RunPowerCfg("/getactivescheme");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var start = output.IndexOf(':') + 1;
        var end = output.IndexOf('(');
        if (start <= 0 || end <= start)
        {
            throw new InvalidOperationException("Could not parse the active power scheme.");
        }

        return Guid.Parse(output[start..end].Trim());
    }

    private static void SetActivePowerPlan(Guid schemeGuid)
    {
        using var process = RunPowerCfg($"/setactive {schemeGuid:D}");
        process.WaitForExit();
    }

    private static Process RunPowerCfg(string arguments)
    {
        var startInfo = new ProcessStartInfo("powercfg.exe", arguments)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start powercfg.exe");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
