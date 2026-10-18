using System.Text.Json;

namespace AetherControl.Services.Hardware;

public sealed class ThermalAuraSettings
{
    /// <summary>Main window ambient glow. Default on — it is subtle by design.</summary>
    public bool MainGlowEnabled { get; set; } = true;
    /// <summary>Portrait Mode glow. Default off — an always-on panel pays GPU cost per frame.</summary>
    public bool PortraitGlowEnabled { get; set; }
}

/// <summary>
/// Persists the Thermal Aura toggles. A small JSON file like <see cref="AlertSettingsStore"/>,
/// not an <c>AppSettings</c> column — same reasoning: two bools don't need a schema migration.
/// </summary>
public sealed class ThermalAuraSettingsStore
{
    private readonly string _path;
    private ThermalAuraSettings _settings;

    public ThermalAuraSettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "thermal-aura-settings.json");
        _settings = Load();
    }

    public ThermalAuraSettings Current => _settings;

    public void Save(ThermalAuraSettings settings)
    {
        _settings = settings;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(settings));
        }
        catch
        {
            // Non-critical — settings just won't persist this session.
        }
    }

    private ThermalAuraSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<ThermalAuraSettings>(json) ?? new ThermalAuraSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable — start with defaults rather than crash the app over this.
        }

        return new ThermalAuraSettings();
    }
}
