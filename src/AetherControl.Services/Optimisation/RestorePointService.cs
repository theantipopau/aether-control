using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Optimisation;

/// <summary>
/// Wraps the <c>SystemRestore</c> WMI class so every optimisation task that
/// mutates system state can offer an undo point first, per the requirement
/// that all optimisation changes be reversible. Requires System Protection
/// to be enabled on the OS volume; if it isn't, this fails loudly rather than
/// silently skipping the safety net.
/// </summary>
public sealed class RestorePointService(ILogger<RestorePointService> logger)
{
    private const uint ModifySettings = 12;
    private const uint BeginSystemChange = 100;

    public bool TryCreate(string description, out string? failureReason)
    {
        try
        {
            using var restoreClass = new ManagementClass(@"root\default", "SystemRestore", null);
            using var inParams = restoreClass.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = ModifySettings;
            inParams["EventType"] = BeginSystemChange;

            using var result = restoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
            var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);

            if (returnValue != 0)
            {
                failureReason = $"CreateRestorePoint returned status code {returnValue}.";
                return false;
            }

            failureReason = null;
            return true;
        }
        catch (Exception ex) when (ex is ManagementException or COMException)
        {
            logger.LogWarning(ex, "Failed to create system restore point");
            failureReason = ex.Message;
            return false;
        }
    }
}
