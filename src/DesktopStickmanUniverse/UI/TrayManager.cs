using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DesktopStickmanUniverse.AI;
using Forms = System.Windows.Forms;

namespace DesktopStickmanUniverse.UI;

/// <summary>
/// System tray controls for the simulation and its desktop mini-events.
/// Uses a programmatically rendered icon so no external asset files are required.
/// </summary>
public sealed class TrayManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseResumeItem;
    private readonly IntPtr _iconHandle;
    private readonly Icon _icon;
    private bool _disposed;

    public event Action? PauseResumeRequested;
    public event Action? SettingsRequested;
    public event Action? ScatterRequested;
    public event Action<MiniEventKind>? MiniEventRequested;
    public event Action? ExitRequested;

    public TrayManager()
    {
        (_icon, _iconHandle) = CreateStickmanTrayIcon();

        var menu = new Forms.ContextMenuStrip();

        _pauseResumeItem = new Forms.ToolStripMenuItem("Pause");
        _pauseResumeItem.Click += (_, _) => PauseResumeRequested?.Invoke();

        var settingsItem = new Forms.ToolStripMenuItem("Settings...");
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var scatterItem = new Forms.ToolStripMenuItem("Scatter Characters");
        scatterItem.Click += (_, _) => ScatterRequested?.Invoke();

        var miniEventsItem = new Forms.ToolStripMenuItem("Mini-Events");
        miniEventsItem.DropDownItems.Add(CreateMiniEventItem("Dance Party", MiniEventKind.DanceParty));
        miniEventsItem.DropDownItems.Add(CreateMiniEventItem("Tag Chase", MiniEventKind.TagChase));
        miniEventsItem.DropDownItems.Add(CreateMiniEventItem("Acrobatics Show", MiniEventKind.AcrobaticsShow));
        miniEventsItem.DropDownItems.Add(CreateMiniEventItem("Group Chat", MiniEventKind.GatheringChat));
        miniEventsItem.DropDownItems.Add(CreateMiniEventItem("Window Summit Climb", MiniEventKind.SummitClimb));
        miniEventsItem.DropDownItems.Add(CreateMiniEventItem("Spectated Duel", MiniEventKind.SpectatedDuel));

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(_pauseResumeItem);
        menu.Items.Add(scatterItem);
        menu.Items.Add(miniEventsItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Desktop Stickman Universe",
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    private Forms.ToolStripMenuItem CreateMiniEventItem(string label, MiniEventKind kind)
    {
        var item = new Forms.ToolStripMenuItem(label);
        item.Click += (_, _) => MiniEventRequested?.Invoke(kind);
        return item;
    }

    public void ShowMiniEventResult(MiniEventStartResult result)
    {
        string? message = result switch
        {
            MiniEventStartResult.DesktopUnavailable => "Resume the simulation and make the characters visible first.",
            MiniEventStartResult.EventInProgress => "Wait for the current mini-event to finish, then try again.",
            MiniEventStartResult.FightingDisabled => "Enable playful fighting in Settings to start a duel.",
            MiniEventStartResult.TooFewAvailableCharacters => "At least two characters must be awake, grounded, and free to join.",
            MiniEventStartResult.NoSuitableWindow => "Open a window with a wide, visible top edge to start a summit climb.",
            _ => null
        };

        if (!_disposed && message is not null)
        {
            _notifyIcon.ShowBalloonTip(4000, "Mini-Events", message, Forms.ToolTipIcon.Info);
        }
    }

    public void UpdatePausedState(bool isPaused)
    {
        if (_disposed)
        {
            return;
        }

        _pauseResumeItem.Text = isPaused ? "Resume" : "Pause";
        _notifyIcon.Text = isPaused
            ? "Desktop Stickman Universe (Paused)"
            : "Desktop Stickman Universe";
    }

    private static (Icon Icon, IntPtr Handle) CreateStickmanTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bgBrush = new SolidBrush(Color.FromArgb(245, 24, 29, 42));
            using var bgPath = CreateRoundedRect(new Rectangle(1, 1, 30, 30), 8);
            g.FillPath(bgBrush, bgPath);

            using var borderPen = new Pen(Color.FromArgb(140, 120, 175, 255), 1.4f);
            g.DrawPath(borderPen, bgPath);

            using var bodyPen = new Pen(Color.FromArgb(255, 74, 178, 255), 2.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var headBrush = new SolidBrush(Color.FromArgb(255, 244, 92, 92));

            // Head
            g.FillEllipse(headBrush, 12f, 5f, 8f, 8f);
            // Torso
            g.DrawLine(bodyPen, 16f, 13f, 16f, 21f);
            // Arms (raised cheerfully)
            g.DrawLine(bodyPen, 16f, 15.5f, 9.5f, 11.5f);
            g.DrawLine(bodyPen, 16f, 15.5f, 22.5f, 11.5f);
            // Legs
            g.DrawLine(bodyPen, 16f, 21f, 11f, 27.5f);
            g.DrawLine(bodyPen, 16f, 21f, 21.5f, 27.5f);
        }

        IntPtr hIcon = bmp.GetHicon();
        return (Icon.FromHandle(hIcon), hIcon);
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }
}
