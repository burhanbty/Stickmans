using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopStickmanUniverse.Core;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Overlay;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Settings;
using DesktopStickmanUniverse.UI;
using Xunit;

namespace DesktopStickmanUniverse.Tests;

public sealed class WindowsIntegrationTests
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [Fact]
    public void WindowTracker_DetectsRealMonitorsAndTaskbar()
    {
        RunOnStaThread(() =>
        {
            using var tracker = new WindowTracker(Dispatcher.CurrentDispatcher);
            tracker.Start();
            var snap = tracker.CurrentSnapshot;

            Assert.NotEmpty(snap.Monitors);
            Assert.All(snap.Monitors, m =>
            {
                Assert.True(m.Bounds.Width > 0);
                Assert.True(m.Bounds.Height > 0);
                Assert.True(m.DpiScale > 0);
            });

            Assert.Contains(snap.WalkableSurfaces, s => s.Kind is SurfaceKind.Taskbar or SurfaceKind.MonitorFloor);
        });
    }

    [Fact]
    public void OverlayWindow_AppliesAndTogglesWin32ClickThroughStyles()
    {
        RunOnStaThread(() =>
        {
            var monitor = new MonitorInfoData(
                @"\\.\DISPLAY_TEST",
                new PixelRect(0, 0, 640, 480),
                new PixelRect(0, 0, 640, 440),
                1.0,
                true,
                false);

            var overlay = new OverlayWindow(monitor);
            overlay.Show();
            try
            {
                IntPtr hwnd = new WindowInteropHelper(overlay).Handle;
                Assert.NotEqual(IntPtr.Zero, hwnd);

                long initialEx = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
                Assert.True((initialEx & WS_EX_LAYERED) != 0);
                Assert.True((initialEx & WS_EX_TOOLWINDOW) != 0);
                Assert.True((initialEx & WS_EX_NOACTIVATE) != 0);
                Assert.True((initialEx & WS_EX_TRANSPARENT) != 0);

                overlay.SetClickThrough(false);
                long interactiveEx = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
                Assert.True((interactiveEx & WS_EX_TRANSPARENT) == 0);

                overlay.SetClickThrough(true);
                long restoredEx = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
                Assert.True((restoredEx & WS_EX_TRANSPARENT) != 0);
            }
            finally
            {
                overlay.Close();
            }
        });
    }

    [Fact]
    public void Engine_Tray_And_SettingsWindow_RunAndDisposeCleanly()
    {
        RunOnStaThread(() =>
        {
            string tempSettings = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dsu_int_{Guid.NewGuid():N}.json");
            try
            {
                using var tray = new TrayManager();
                using var engine = new StickmanUniverseEngine(Dispatcher.CurrentDispatcher, new SettingsService(tempSettings));
                engine.Start();

                Assert.Equal(4, engine.Characters.Count);

                // Pump dispatcher briefly so tick timer runs real physics/AI/render cycles.
                PumpDispatcher(220);

                var settingsWin = new SettingsWindow(engine);
                settingsWin.Show();
                PumpDispatcher(120);
                settingsWin.Close();

                tray.UpdatePausedState(true);
                engine.TogglePause();
                Assert.True(engine.Settings.IsPaused);
            }
            finally
            {
                if (System.IO.File.Exists(tempSettings))
                {
                    System.IO.File.Delete(tempSettings);
                }
            }
        });
    }

    [Fact]
    public void WindowTracker_And_Physics_TrackRealExternalWindowMovementAndClose()
    {
        RunOnStaThread(() =>
        {
            const string testTitle = "DSU_RealExternalWindow_Probe";
            string psCmd = "Add-Type -AssemblyName System.Windows.Forms; " +
                           "$f = New-Object System.Windows.Forms.Form; " +
                           $"$f.Text = '{testTitle}'; " +
                           "$f.StartPosition = 'Manual'; " +
                           "$f.SetBounds(280, 240, 420, 280); " +
                           "$f.TopMost = $true; " +
                           "[System.Windows.Forms.Application]::Run($f)";

            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -STA -Command \"{psCmd}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var externalProc = Process.Start(psi)!;
            try
            {
                using var tracker = new WindowTracker(Dispatcher.CurrentDispatcher);
                tracker.Start();

                TrackedWindow? detected = null;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    var snap = tracker.RefreshNow();
                    detected = snap.Windows.FirstOrDefault(w => w.Title == testTitle);
                    if (detected is not null)
                    {
                        break;
                    }
                    Thread.Sleep(80);
                }

                Assert.NotNull(detected);
                var snap1 = tracker.CurrentSnapshot;
                var topSurface = snap1.WalkableSurfaces.FirstOrDefault(s => s.SourceHwnd == detected!.Hwnd && s.Kind == SurfaceKind.WindowTop);
                Assert.NotNull(topSurface);

                var physics = new PhysicsEngine();
                var particles = new List<ParticleEffect>();
                double startX = (topSurface!.Left + topSurface.Right) * 0.5;
                double startY = topSurface.Y;
                var stickman = new StickmanCharacter
                {
                    Id = 1,
                    X = startX,
                    Y = startY,
                    IsGrounded = true,
                    GroundedSurface = topSurface,
                    State = StickmanState.Idle
                };
                physics.Step(new[] { stickman }, particles, snap1, 0.016);

                // Move real external OS window smoothly by (+40, +16) pixels (within normal carry speed).
                SetWindowPos(
                    detected!.Hwnd,
                    IntPtr.Zero,
                    (int)Math.Round(detected.Bounds.Left) + 40,
                    (int)Math.Round(detected.Bounds.Top) + 16,
                    0,
                    0,
                    0x0001 | 0x0004 | 0x0010); // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE

                Thread.Sleep(60);
                var snap2 = tracker.RefreshNow();
                var movedSurf = snap2.WalkableSurfaces.FirstOrDefault(s => s.SourceHwnd == detected.Hwnd && s.Kind == SurfaceKind.WindowTop);
                Assert.NotNull(movedSurf);
                Assert.True(movedSurf!.DeltaX >= 25);

                physics.Step(new[] { stickman }, particles, snap2, 0.016);
                Assert.True(stickman.IsGrounded);
                Assert.True(stickman.X > startX + 20);
                Assert.Equal(movedSurf.Y, stickman.Y, 1);

                // Close external window and verify character falls cleanly.
                externalProc.Kill();
                externalProc.WaitForExit(3000);
                Thread.Sleep(60);

                var snap3 = tracker.RefreshNow();
                physics.Step(new[] { stickman }, particles, snap3, 0.02);
                Assert.False(stickman.IsGrounded);
                Assert.Equal(StickmanState.Fall, stickman.State);
            }
            finally
            {
                if (!externalProc.HasExited)
                {
                    externalProc.Kill();
                    externalProc.WaitForExit(2000);
                }
            }
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new AggregateException("STA integration test failed.", captured);
        }
    }

    private static void PumpDispatcher(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var stopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        stopTimer.Tick += (_, _) =>
        {
            stopTimer.Stop();
            frame.Continue = false;
        };
        stopTimer.Start();
        Dispatcher.PushFrame(frame);
    }
}
