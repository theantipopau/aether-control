using System.Reflection;
using System.Runtime.Loader;
using AetherControl.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Plugins;

/// <summary>
/// Discovers plugin assemblies (one subdirectory per plugin under the plugins
/// root, each containing a DLL implementing <see cref="IAetherPlugin"/>),
/// loads each in its own collectible <see cref="AssemblyLoadContext"/> so
/// plugins can be unloaded independently, and drives their lifecycle. This is
/// the mechanism the roadmap's "future expansion" modules (Fan Control,
/// Benchmarking, NAS monitoring, Home Assistant, ...) are expected to plug
/// into without touching the core app.
/// </summary>
public sealed class PluginManager(IServiceProvider hostServices, ILogger<PluginManager> logger) : IPluginManager
{
    private readonly List<(IAetherPlugin Plugin, AssemblyLoadContext Context)> _loaded = [];

    public IReadOnlyList<IAetherPlugin> LoadedPlugins => _loaded.Select(x => x.Plugin).ToList();

    public async Task LoadPluginsAsync(string pluginsDirectory, CancellationToken ct = default)
    {
        if (!Directory.Exists(pluginsDirectory))
        {
            return;
        }

        foreach (var pluginDirectory in Directory.EnumerateDirectories(pluginsDirectory))
        {
            var dllPath = Directory.EnumerateFiles(pluginDirectory, "*.dll")
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), Path.GetFileName(pluginDirectory), StringComparison.OrdinalIgnoreCase))
                ?? Directory.EnumerateFiles(pluginDirectory, "*.dll").FirstOrDefault();

            if (dllPath is null)
            {
                continue;
            }

            try
            {
                await LoadPluginAssemblyAsync(dllPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ReflectionTypeLoadException or FileLoadException)
            {
                logger.LogError(ex, "Failed to load plugin from {Path}", dllPath);
            }
        }
    }

    private async Task LoadPluginAssemblyAsync(string dllPath, CancellationToken ct)
    {
        var context = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(dllPath);

        var pluginType = assembly.GetTypes().FirstOrDefault(t =>
            typeof(IAetherPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (pluginType is null)
        {
            context.Unload();
            return;
        }

        if (Activator.CreateInstance(pluginType) is not IAetherPlugin plugin)
        {
            context.Unload();
            return;
        }

        var context1 = new PluginContext(Path.GetDirectoryName(dllPath)!, hostServices, logger);
        await plugin.InitializeAsync(context1, ct).ConfigureAwait(false);

        _loaded.Add((plugin, context));
        logger.LogInformation("Loaded plugin {Name} v{Version}", plugin.Metadata.Name, plugin.Metadata.Version);
    }

    public async Task UnloadAllAsync()
    {
        foreach (var (plugin, context) in _loaded)
        {
            await plugin.ShutdownAsync().ConfigureAwait(false);
            context.Unload();
        }

        _loaded.Clear();
    }

    private sealed class PluginContext(string dataDirectory, IServiceProvider hostServices, ILogger logger) : IPluginContext
    {
        public string PluginDataDirectory { get; } = dataDirectory;
        public IServiceProvider HostServices { get; } = hostServices;
        public void Log(string message) => logger.LogInformation("[Plugin] {Message}", message);
    }
}
