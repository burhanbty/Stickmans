using System.Windows.Media;
using DesktopStickmanUniverse.Physics;

namespace DesktopStickmanUniverse.Settings;

public enum ActivityLevel
{
    Calm = 0,
    Normal = 1,
    Lively = 2
}

public sealed class AppSettings
{
    public bool CharactersEnabled { get; set; } = true;
    public int CharacterCount { get; set; } = 4;
    public List<string> CharacterColors { get; set; } = StickmanCharacter.DefaultPalette
        .Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}")
        .ToList();

    public double AnimationSpeed { get; set; } = 1.0;
    public ActivityLevel ActivityLevel { get; set; } = ActivityLevel.Normal;
    public bool EnableFighting { get; set; } = true;
    public bool EnableMouseInteraction { get; set; } = true;
    public bool IsPaused { get; set; } = false;
    public bool PerformanceMode { get; set; } = false;
    public bool LaunchAtStartup { get; set; } = false;

    public AppSettings Clone() => new()
    {
        CharactersEnabled = CharactersEnabled,
        CharacterCount = CharacterCount,
        CharacterColors = new List<string>(CharacterColors),
        AnimationSpeed = AnimationSpeed,
        ActivityLevel = ActivityLevel,
        EnableFighting = EnableFighting,
        EnableMouseInteraction = EnableMouseInteraction,
        IsPaused = IsPaused,
        PerformanceMode = PerformanceMode,
        LaunchAtStartup = LaunchAtStartup
    };

    public Color GetColorForIndex(int index)
    {
        if (index >= 0 && index < CharacterColors.Count)
        {
            try
            {
                if (ColorConverter.ConvertFromString(CharacterColors[index]) is Color parsed)
                {
                    return parsed;
                }
            }
            catch
            {
                // Fallback to palette below.
            }
        }

        return StickmanCharacter.DefaultPalette[index % StickmanCharacter.DefaultPalette.Count];
    }
}
