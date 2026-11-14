using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopStickmanUniverse.Core;
using DesktopStickmanUniverse.Interop;
using DesktopStickmanUniverse.Settings;
using Forms = System.Windows.Forms;

namespace DesktopStickmanUniverse.UI;

public partial class SettingsWindow : Window
{
    private readonly StickmanUniverseEngine _engine;
    private readonly DispatcherTimer _telemetryTimer;
    private bool _isLoadingUi = true;

    public event Action? ExitApplicationRequested;

    public SettingsWindow(StickmanUniverseEngine engine)
    {
        InitializeComponent();
        _engine = engine;

        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                Win32Native.ApplyRoundedCorners(hwnd);
            }
        };

        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _telemetryTimer.Tick += (_, _) => RefreshTelemetry();

        Loaded += (_, _) =>
        {
            SyncFromSettings(_engine.Settings);
            RefreshTelemetry();
            _telemetryTimer.Start();
        };

        Closed += (_, _) => _telemetryTimer.Stop();
    }

    public void SyncFromSettings(AppSettings s)
    {
        _isLoadingUi = true;
        try
        {
            ChkCharactersEnabled.IsChecked = s.CharactersEnabled;
            ChkPaused.IsChecked = s.IsPaused;
            SldCharacterCount.Value = s.CharacterCount;
            TxtCharCountValue.Text = s.CharacterCount.ToString();

            SldAnimSpeed.Value = s.AnimationSpeed;
            TxtAnimSpeedValue.Text = $"{s.AnimationSpeed:0.0}x";

            CmbActivityLevel.SelectedIndex = (int)s.ActivityLevel;
            ChkEnableFighting.IsChecked = s.EnableFighting;
            ChkEnableMouse.IsChecked = s.EnableMouseInteraction;
            ChkPerformanceMode.IsChecked = s.PerformanceMode;
            ChkStartup.IsChecked = StartupManager.IsEnabled() || s.LaunchAtStartup;

            RebuildColorSwatches(s);
        }
        finally
        {
            _isLoadingUi = false;
        }
    }

    private void RebuildColorSwatches(AppSettings s)
    {
        ColorSwatchesPanel.Children.Clear();

        for (int i = 0; i < s.CharacterCount; i++)
        {
            int index = i;
            Color color = s.GetColorForIndex(index);

            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 8, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"Character #{index + 1} color — click to change"
            };

            swatch.Child = new TextBlock
            {
                Text = (index + 1).ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(16, 20, 30)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            swatch.MouseLeftButtonUp += (_, _) => PickColorForCharacter(index);
            ColorSwatchesPanel.Children.Add(swatch);
        }
    }

    private void PickColorForCharacter(int index)
    {
        Color current = _engine.Settings.GetColorForIndex(index);
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        var owner = hwnd != IntPtr.Zero ? new HwndOwnerWrapper(hwnd) : null;
        if ((owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == Forms.DialogResult.OK)
        {
            var copy = _engine.Settings.Clone();
            while (copy.CharacterColors.Count <= index)
            {
                var def = copy.GetColorForIndex(copy.CharacterColors.Count);
                copy.CharacterColors.Add($"#{def.R:X2}{def.G:X2}{def.B:X2}");
            }

            copy.CharacterColors[index] = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            _engine.ApplySettings(copy);
            SyncFromSettings(_engine.Settings);
        }
    }

    private sealed class HwndOwnerWrapper : Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public HwndOwnerWrapper(IntPtr handle) => Handle = handle;
    }

    private void OnCharacterCountChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingUi)
        {
            return;
        }

        PushSettingsFromUi();
        RebuildColorSwatches(_engine.Settings);
    }

    private void OnSettingControlChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi)
        {
            return;
        }

        PushSettingsFromUi();
    }

    private void OnSettingControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingUi)
        {
            return;
        }

        PushSettingsFromUi();
    }

    private void OnSettingControlChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingUi)
        {
            return;
        }

        PushSettingsFromUi();
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi)
        {
            return;
        }

        bool enable = ChkStartup.IsChecked == true;
        StartupManager.SetEnabled(enable);
        PushSettingsFromUi();
    }

    private void PushSettingsFromUi()
    {
        if (!IsLoaded)
        {
            return;
        }

        var updated = _engine.Settings.Clone();
        updated.CharactersEnabled = ChkCharactersEnabled.IsChecked == true;
        updated.IsPaused = ChkPaused.IsChecked == true;
        updated.CharacterCount = (int)Math.Round(SldCharacterCount.Value);
        updated.AnimationSpeed = Math.Round(SldAnimSpeed.Value, 1);
        updated.ActivityLevel = (ActivityLevel)Math.Clamp(CmbActivityLevel.SelectedIndex, 0, 2);
        updated.EnableFighting = ChkEnableFighting.IsChecked == true;
        updated.EnableMouseInteraction = ChkEnableMouse.IsChecked == true;
        updated.PerformanceMode = ChkPerformanceMode.IsChecked == true;
        updated.LaunchAtStartup = ChkStartup.IsChecked == true;

        TxtCharCountValue.Text = updated.CharacterCount.ToString();
        TxtAnimSpeedValue.Text = $"{updated.AnimationSpeed:0.0}x";

        _engine.ApplySettings(updated);
    }

    private void RefreshTelemetry()
    {
        var perf = _engine.CurrentPerformance;
        TxtStatFps.Text = $"{perf.Fps:0.0} / {perf.TargetFps} FPS";
        TxtStatCpu.Text = $"{perf.CpuPercent:0.00}%";
        TxtStatMem.Text = $"{perf.MemoryMb:0.0} MB";
        TxtStatFrameMs.Text = $"{perf.FrameTimeMs:0.00} ms";
        TxtStatScanMs.Text = $"{perf.WindowScanMs:0.00} ms";
        TxtStatOverlays.Text = $"{perf.ActiveOverlays} active";
    }

    private void OnScatterClick(object sender, RoutedEventArgs e) => _engine.ScatterCharacters();

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        StartupManager.SetEnabled(defaults.LaunchAtStartup);
        _engine.ApplySettings(defaults);
        SyncFromSettings(_engine.Settings);
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        ExitApplicationRequested?.Invoke();
    }
}
