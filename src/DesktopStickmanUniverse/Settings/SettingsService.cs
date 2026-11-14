using System.IO;
using System.Text.Json;

namespace DesktopStickmanUniverse.Settings;

/// <summary>
/// Persists user settings safely to %LocalAppData%\DesktopStickmanUniverse\settings.json.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsFilePath { get; }

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            SettingsFilePath = customPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            SettingsFilePath = Path.Combine(appData, "DesktopStickmanUniverse", "settings.json");
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return Normalize(loaded ?? new AppSettings());
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var normalized = Normalize(settings);
        string? dir = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = SettingsFilePath + ".tmp";
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsFilePath, overwrite: true);
    }

    public static AppSettings Normalize(AppSettings s)
    {
        s.CharacterCount = Math.Clamp(s.CharacterCount, 1, 10);
        s.AnimationSpeed = Math.Clamp(s.AnimationSpeed, 0.5, 2.0);
        if (s.CharacterColors is null || s.CharacterColors.Count == 0)
        {
            s.CharacterColors = new AppSettings().CharacterColors;
        }
        return s;
    }
}
