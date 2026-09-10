using System.Text.Json;

namespace AetherControl.Services.Hardware;

public sealed class AlertSettings
{
    public bool Enabled { get; set; } = true;
    public double CpuTemperatureThreshold { get; set; } = 85;
    public double GpuTemperatureThreshold { get; set; } = 85;
}

/// <summary>
/// Persists temperature-alert thresholds. A separate small JSON file rather than a column on
/// <c>AppSettings</c>/the SQLite schema — same reasoning as <see cref="FanLabelStore"/>: this is
/// simple key-value state that doesn't need a relational shape, and avoids a schema migration
/// for two numbers and a bool.
/// </summary>
public sealed class AlertSettingsStore
{
    private readonly string _path;
    private AlertSettings _settings;

    public AlertSettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "alert-settings.json");
        _settings = Load();
    }

    public AlertSettings Current => _settings;

    public void Save(AlertSettings settings)
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

    private AlertSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AlertSettings>(json) ?? new AlertSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable — start with defaults rather than crash the app over this.
        }

        return new AlertSettings();
    }
}
