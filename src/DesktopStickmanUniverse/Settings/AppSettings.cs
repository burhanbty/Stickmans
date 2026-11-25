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
    public const int MaxCharacterNameLength = 24;

    public bool CharactersEnabled { get; set; } = true;
    public int CharacterCount { get; set; } = 4;
    public List<string> CharacterColors { get; set; } = StickmanCharacter.DefaultPalette
        .Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}")
        .ToList();
    public List<string> CharacterNames { get; set; } = new();

    public double AnimationSpeed { get; set; } = 1.0;
    public ActivityLevel ActivityLevel { get; set; } = ActivityLevel.Normal;
    public bool EnableFighting { get; set; } = true;
    public bool EnableMouseInteraction { get; set; } = true;
    public bool EnableAutomaticMiniEvents { get; set; } = true;
    public bool EnableWorkspaceReactions { get; set; } = true;
    public bool IsPaused { get; set; } = false;
    public bool PerformanceMode { get; set; } = false;
    public bool LaunchAtStartup { get; set; } = false;

    public AppSettings Clone() => new()
    {
        CharactersEnabled = CharactersEnabled,
        CharacterCount = CharacterCount,
        CharacterColors = new List<string>(CharacterColors),
        CharacterNames = new List<string>(CharacterNames),
        AnimationSpeed = AnimationSpeed,
        ActivityLevel = ActivityLevel,
        EnableFighting = EnableFighting,
        EnableMouseInteraction = EnableMouseInteraction,
        EnableAutomaticMiniEvents = EnableAutomaticMiniEvents,
        EnableWorkspaceReactions = EnableWorkspaceReactions,
        IsPaused = IsPaused,
        PerformanceMode = PerformanceMode,
        LaunchAtStartup = LaunchAtStartup
    };

    public string GetNameForIndex(int index)
    {
        if (index < CharacterNames.Count && !string.IsNullOrEmpty(CharacterNames[index]))
        {
            return CharacterNames[index];
        }

        var profiles = StickmanCharacter.DefaultPersonalities;
        string name = profiles[index % profiles.Count].ArchetypeName;
        return index < profiles.Count ? name : $"{name}-{index + 1}";
    }

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
