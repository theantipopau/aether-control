using System.Management;

namespace AetherControl.Services.Hardware;

/// <summary>
/// LibreHardwareMonitor's Memory hardware node exposes usage but not the
/// module speed (that's SPD data, not something the OS-level sensor APIs
/// surface) — WMI's <c>Win32_PhysicalMemory.Speed</c> does have it.
/// </summary>
internal static class MemorySpeedProbe
{
    public static double QuerySpeedMhz()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory");
            double maxSpeed = 0;
            foreach (ManagementObject module in searcher.Get())
            {
                if (module["Speed"] is { } speed)
                {
                    maxSpeed = Math.Max(maxSpeed, Convert.ToDouble(speed));
                }
            }

            return maxSpeed;
        }
        catch (ManagementException)
        {
            return 0;
        }
    }
}
