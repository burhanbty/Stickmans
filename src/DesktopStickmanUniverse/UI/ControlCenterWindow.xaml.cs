using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopStickmanUniverse.AI;
using DesktopStickmanUniverse.Core;
using DesktopStickmanUniverse.Settings;

namespace DesktopStickmanUniverse.UI;

public partial class ControlCenterWindow : Window
{
    private readonly StickmanUniverseEngine _engine;
    private readonly DispatcherTimer _refreshTimer;
    private CrewMember[] _crew = Array.Empty<CrewMember>();
    private bool _syncing;

    public event Action? SettingsRequested;

    public ControlCenterWindow(StickmanUniverseEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        NameEditor.MaxLength = AppSettings.MaxCharacterNameLength;
        EventChoices.ItemsSource = MiniEventPresentation.All;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                RefreshRuntime();
            }
        };
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _engine.SettingsChanged += SyncSettings;
        _engine.FocusChanged += RefreshRuntime;
        SyncSettings(_engine.Settings);
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _engine.SettingsChanged -= SyncSettings;
        _engine.FocusChanged -= RefreshRuntime;
    }

    private void SyncSettings(AppSettings settings)
    {
        _syncing = true;
        try
        {
            AutomaticEventsCheck.IsChecked = settings.EnableAutomaticMiniEvents;
            WorkspaceReactionsCheck.IsChecked = settings.EnableWorkspaceReactions;
            var crew = _engine.Characters.Select(c => new CrewMember(c.Id, c.Name, c.Personality.ArchetypeName, c.PrimaryColor)).ToArray();
            if (!_crew.SequenceEqual(crew))
            {
                int? selectedId = (CrewList.SelectedItem as CrewMember)?.Id;
                _crew = crew;
                CrewList.ItemsSource = _crew;
                CrewList.SelectedItem = _crew.FirstOrDefault(c => c.Id == selectedId) ?? _crew.FirstOrDefault();
                CrewCount.Text = _crew.Length.ToString();
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshRuntime();
    }

    private void RefreshRuntime()
    {
        var playback = _engine.PlaybackState;
        bool running = playback == DesktopPlaybackState.Running;
        (PlaybackLabel.Text, PlaybackButton.Content) = playback switch
        {
            DesktopPlaybackState.Focusing => ("Focusing", "End focus"),
            DesktopPlaybackState.Hidden => ("Crew hidden", "Show crew"),
            DesktopPlaybackState.Paused => ("Paused", "Resume"),
            DesktopPlaybackState.Unavailable => ("Overlay unavailable", "Pause"),
            _ => ("Desktop live", "Pause")
        };

        var activeEvent = _engine.ActiveMiniEvent;
        bool hasEvent = activeEvent != MiniEventKind.None;
        EventChoices.IsEnabled = running && !hasEvent;
        StopEventButton.Visibility = hasEvent ? Visibility.Visible : Visibility.Collapsed;
        EventHeading.Text = hasEvent ? MiniEventPresentation.Get(activeEvent).Title : "Desktop stage";
        EventStatus.Text = hasEvent
            ? (running ? $"Playing · {Math.Ceiling(_engine.MiniEventRemainingSeconds / _engine.Settings.AnimationSpeed):0}s remaining" : "Event on hold")
            : running ? "Choose a mini-event to get the crew moving." : "Show the crew to play a mini-event.";

        bool focusing = _engine.IsFocusActive;
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(_engine.FocusTimeRemaining.TotalSeconds));
        FocusStatus.Text = focusing ? $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} remaining" : "Hide the crew for a set time.";
        FocusStatus.FontSize = focusing ? 23 : 14;
        FocusDurations.Visibility = focusing ? Visibility.Collapsed : Visibility.Visible;
        FocusButton.Content = focusing ? "End focus" : "Start focus";
        FindButton.IsEnabled = running && CrewList.SelectedItem is not null;
    }

    private void OnPlaybackClick(object sender, RoutedEventArgs e)
    {
        if (_engine.IsFocusActive)
        {
            _engine.EndFocus();
        }
        else if (!_engine.Settings.CharactersEnabled)
        {
            var updated = _engine.Settings.Clone();
            updated.CharactersEnabled = true;
            TrySaveSettings(updated);
        }
        else
        {
            var updated = _engine.Settings.Clone();
            updated.IsPaused = !updated.IsPaused;
            TrySaveSettings(updated);
        }
    }

    private void OnFocusClick(object sender, RoutedEventArgs e)
    {
        if (_engine.IsFocusActive)
        {
            _engine.EndFocus();
        }
        else
        {
            int minutes = Focus15.IsChecked == true ? 15 : Focus50.IsChecked == true ? 50 : 25;
            _engine.StartFocus(TimeSpan.FromMinutes(minutes));
        }
    }

    private void OnStartEventClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MiniEventPresentation miniEvent })
        {
            EventFeedback.Text = MiniEventPresentation.GetFeedback(_engine.TryStartMiniEvent(miniEvent.Kind)) ?? string.Empty;
            RefreshRuntime();
        }
    }

    private void OnStopEventClick(object sender, RoutedEventArgs e)
    {
        _engine.StopMiniEvent();
        EventFeedback.Text = string.Empty;
        RefreshRuntime();
    }

    private void OnStageSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsLoaded)
        {
            return;
        }
        var updated = _engine.Settings.Clone();
        updated.EnableAutomaticMiniEvents = AutomaticEventsCheck.IsChecked == true;
        updated.EnableWorkspaceReactions = WorkspaceReactionsCheck.IsChecked == true;
        TrySaveSettings(updated);
    }

    private void TrySaveSettings(AppSettings settings)
    {
        try
        {
            _engine.ApplySettings(settings);
            EventFeedback.Text = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SyncSettings(_engine.Settings);
            EventFeedback.Text = "Settings could not be saved. Try again.";
        }
    }

    private void OnCrewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NameEditor is null)
        {
            return;
        }

        var member = CrewList.SelectedItem as CrewMember;
        NameEditor.Text = member is null ? string.Empty : GetNickname(member);
        NameEditor.IsEnabled = member is not null;
        CrewFeedback.Text = string.Empty;
        UpdateSaveButton();
        FindButton.IsEnabled = member is not null && _engine.PlaybackState == DesktopPlaybackState.Running;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) => UpdateSaveButton();

    private void UpdateSaveButton()
    {
        if (SaveNameButton is not null)
        {
            SaveNameButton.IsEnabled = CrewList.SelectedItem is CrewMember member && NameEditor.Text.Trim() != GetNickname(member);
        }
    }

    private string GetNickname(CrewMember member) => _engine.Settings.CharacterNames.ElementAtOrDefault(member.Id - 1) ?? string.Empty;

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SaveNameButton.IsEnabled)
        {
            SaveNickname();
            e.Handled = true;
        }
    }

    private void OnSaveNameClick(object sender, RoutedEventArgs e) => SaveNickname();

    private void SaveNickname()
    {
        if (CrewList.SelectedItem is not CrewMember member)
        {
            return;
        }

        try
        {
            _engine.SetCharacterName(member.Id, NameEditor.Text);
            NameEditor.Text = GetNickname(member);
            CrewFeedback.Text = "Nickname saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrewFeedback.Text = "Nickname could not be saved. Try again.";
        }
    }

    private void OnFindClick(object sender, RoutedEventArgs e)
    {
        if (CrewList.SelectedItem is CrewMember member)
        {
            CrewFeedback.Text = _engine.TryLocateCharacter(member.Id) ? $"Look for {member.Name} on your desktop." : "That character is currently hidden.";
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private sealed record CrewMember(int Id, string Name, string Archetype, Color Color)
    {
        public string ArchetypeLabel => Name == Archetype ? string.Empty : Archetype;
    }
}
