using AetherControl.Core.Interfaces;
using AetherControl.Data;
using AetherControl.Data.Repositories;
using AetherControl.Plugins.Abstractions;
using AetherControl.Services.Devices;
using AetherControl.Services.Firmware;
using AetherControl.Services.Hardware;
using AetherControl.Services.History;
using AetherControl.Services.Network;
using AetherControl.Services.Optimisation;
using AetherControl.Services.Plugins;
using AetherControl.Services.Processes;
using AetherControl.Services.Rgb;
using AetherControl.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace AetherControl.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAetherControlServices(this IServiceCollection services, string? databasePath = null)
    {
        services.AddSingleton(new SqliteConnectionFactory(databasePath ?? SqliteConnectionFactory.GetDefaultDatabasePath()));
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<HistoryRepository>();
        services.AddSingleton<LayoutRepository>();
        services.AddSingleton<OptimisationLogRepository>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IHistoryService, HistoryService>();

        services.AddHttpClient<INetworkMonitorService, NetworkMonitorService>();
        services.AddSingleton<FanRpmProbeService>();
        services.AddSingleton<FanLabelStore>();
        services.AddSingleton<AlertSettingsStore>();
        // HardwareMonitorService implements both interfaces but must stay a single instance —
        // it owns the one LibreHardwareMonitor Computer session the whole process is allowed to
        // have open (see the class's own doc comment). Resolving IFanControlService separately
        // with its own AddSingleton<,> registration would silently construct a second Computer.
        services.AddSingleton<HardwareMonitorService>();
        services.AddSingleton<IHardwareMonitorService>(sp => sp.GetRequiredService<HardwareMonitorService>());
        services.AddSingleton<IFanControlService>(sp => sp.GetRequiredService<HardwareMonitorService>());

        services.AddSingleton<WindowsCleanupService>();
        services.AddSingleton<StartupAnalysisService>();
        services.AddSingleton<RestorePointService>();
        services.AddSingleton<GamingProfileService>();
        services.AddSingleton<IOptimisationService, OptimisationService>();
        services.AddSingleton<IGameProfileService, GameProfileService>();

        services.AddSingleton<OpenRgbService>();
        services.AddSingleton<CorsairHidDirectService>();
        services.AddSingleton<IRgbService, CompositeRgbService>();
        services.AddSingleton<IFirmwareDriverService, FirmwareDriverService>();
        services.AddSingleton<IPeripheralDetectionService, PeripheralDetectionService>();
        services.AddSingleton<ProcessRankerService>();
        services.AddSingleton<IFpsSource, RtssFpsSource>();
        services.AddSingleton<IPluginManager, PluginManager>();

        return services;
    }
}
