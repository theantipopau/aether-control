using System.Text.Json;

namespace AetherControl.Services.Hardware;

/// <summary>
/// Persists user-assigned display names for fan headers (e.g. "Fan #2" -> "Front Intake").
/// Ported from Portrait Stats' <c>FanLabelStore</c> — the Super I/O chip has no concept of header
/// naming, so there's nothing to auto-detect; the user assigns it once and this remembers it.
/// </summary>
public sealed class FanLabelStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _labels;

    public FanLabelStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "fan-labels.json");
        _labels = Load();
    }

    public string GetLabel(string id, string fallback) => _labels.GetValueOrDefault(id, fallback);

    public void SetLabel(string id, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            _labels.Remove(id);
        }
        else
        {
            _labels[id] = label;
        }

        Save();
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            }
        }
        catch
        {
            // Corrupt or unreadable — start fresh rather than crash the app over a label file.
        }

        return [];
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_labels));
        }
        catch
        {
            // Non-critical — labels just won't persist this session.
        }
    }
}
