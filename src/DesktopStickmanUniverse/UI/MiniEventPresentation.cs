using DesktopStickmanUniverse.AI;

namespace DesktopStickmanUniverse.UI;

public sealed record MiniEventPresentation(MiniEventKind Kind, string Title, string Description, string Accent)
{
    public static IReadOnlyList<MiniEventPresentation> All { get; } = Array.AsReadOnly(new[]
    {
        new MiniEventPresentation(MiniEventKind.DanceParty, "Dance Party", "A dance floor on your desktop", "#98EDCE"),
        new MiniEventPresentation(MiniEventKind.TagChase, "Tag Chase", "One runner, one pursuer", "#FFCC86"),
        new MiniEventPresentation(MiniEventKind.AcrobaticsShow, "Acrobatics Show", "Tricks with a cheering crowd", "#CBB0FF"),
        new MiniEventPresentation(MiniEventKind.GatheringChat, "Group Chat", "The crew stops for a chat", "#9ECAFF"),
        new MiniEventPresentation(MiniEventKind.SummitClimb, "Window Summit", "A climb to the highest ledge", "#74DFEA"),
        new MiniEventPresentation(MiniEventKind.SpectatedDuel, "Spectated Duel", "A playful spar with spectators", "#FFA9B7")
    });

    public static MiniEventPresentation Get(MiniEventKind kind) => All.First(item => item.Kind == kind);

    public static string? GetFeedback(MiniEventStartResult result) => result switch
    {
        MiniEventStartResult.DesktopUnavailable => "Make your characters visible to start an event.",
        MiniEventStartResult.EventInProgress => "Stop the current event or wait for it to finish.",
        MiniEventStartResult.FightingDisabled => "Enable playful fighting in Settings to start a duel.",
        MiniEventStartResult.TooFewAvailableCharacters => "At least two characters must be awake, grounded, and free to join.",
        MiniEventStartResult.NoSuitableWindow => "Open a window with a wide, visible top edge to start a summit climb.",
        _ => null
    };
}
