using System.IO;
using System.Text.Json;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceTyper");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.CustomHotkeys ??= new List<CustomHotkeySetting>();
                return settings;
            }
        }
        catch
        {
            // Corrupted file — return defaults
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
