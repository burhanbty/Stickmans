using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Interop;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Rendering;
using static DesktopStickmanUniverse.Interop.Win32Native;

namespace DesktopStickmanUniverse.Overlay;

public partial class OverlayWindow : Window
{
    public string MonitorDeviceName { get; private set; }
    public OverlayCanvasElement Canvas { get; } = new();

    private IntPtr _hwnd;
    private bool _isClickThrough = true;
    private MonitorInfoData _monitor;
    private bool _hasLastDrawnContent;

    public event Action<OverlayWindow, Point>? OverlayMouseLeftDown;
    public event Action<OverlayWindow, Point>? OverlayMouseMove;
    public event Action<OverlayWindow, Point>? OverlayMouseLeftUp;
    public event Action<OverlayWindow, Point>? OverlayMouseRightUp;

    public OverlayWindow(MonitorInfoData monitor)
    {
        InitializeComponent();
        _monitor = monitor;
        MonitorDeviceName = monitor.DeviceName;
        RootGrid.Children.Add(Canvas);

        Left = monitor.Bounds.Left / Math.Max(0.5, monitor.DpiScale);
        Top = monitor.Bounds.Top / Math.Max(0.5, monitor.DpiScale);
        Width = monitor.Bounds.Width / Math.Max(0.5, monitor.DpiScale);
        Height = monitor.Bounds.Height / Math.Max(0.5, monitor.DpiScale);

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        _isClickThrough = true;

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyMonitorBounds(_monitor);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(() => ApplyMonitorBounds(_monitor));
    }

    public void ApplyMonitorBounds(MonitorInfoData monitor)
    {
        _monitor = monitor;
        MonitorDeviceName = monitor.DeviceName;

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            _hwnd,
            HWND_TOPMOST,
            (int)Math.Round(monitor.Bounds.Left),
            (int)Math.Round(monitor.Bounds.Top),
            (int)Math.Round(monitor.Bounds.Width),
            (int)Math.Round(monitor.Bounds.Height),
            SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }

    public void SetSuppressed(bool suppressed)
    {
        if (suppressed)
        {
            if (Visibility != Visibility.Collapsed)
            {
                ClearCanvas();
                Visibility = Visibility.Collapsed;
            }
        }
        else if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            if (_hwnd != IntPtr.Zero)
            {
                ApplyMonitorBounds(_monitor);
            }
        }
    }

    public void SetClickThrough(bool clickThrough)
    {
        if (_hwnd == IntPtr.Zero || _isClickThrough == clickThrough)
        {
            return;
        }

        long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        if (clickThrough)
        {
            exStyle |= WS_EX_TRANSPARENT;
            Cursor = System.Windows.Input.Cursors.Arrow;
        }
        else
        {
            exStyle &= ~WS_EX_TRANSPARENT;
            Cursor = System.Windows.Input.Cursors.Hand;
        }

        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        _isClickThrough = clickThrough;
    }

    public void RenderFrame(
        CharacterRenderer renderer,
        IReadOnlyList<StickmanCharacter> characters,
        IReadOnlyList<ParticleEffect> particles,
        int? hoveredCharacterId,
        bool showParticles)
    {
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0.1 ? dpi.DpiScaleX : _monitor.DpiScale;
        double scaleY = dpi.DpiScaleY > 0.1 ? dpi.DpiScaleY : _monitor.DpiScale;

        bool hasContent = characters.Any(c => IsNearMonitor(c, _monitor, scaleX))
            || (showParticles && particles.Any(p => IsNearMonitor(p.X, p.Y, _monitor, scaleX)));

        if (!hasContent && !_hasLastDrawnContent)
        {
            return;
        }

        renderer.RenderMonitor(
            Canvas,
            _monitor,
            scaleX,
            scaleY,
            characters,
            particles,
            hoveredCharacterId,
            showParticles);

        _hasLastDrawnContent = hasContent;
    }

    public void ClearCanvas()
    {
        if (!_hasLastDrawnContent)
        {
            return;
        }

        using var dc = Canvas.RenderOpen();
        _hasLastDrawnContent = false;
    }

    public Point ToPhysicalScreenPoint(Point localDipPoint)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0.1 ? dpi.DpiScaleX : _monitor.DpiScale;
        double scaleY = dpi.DpiScaleY > 0.1 ? dpi.DpiScaleY : _monitor.DpiScale;

        return new Point(
            _monitor.Bounds.Left + localDipPoint.X * scaleX,
            _monitor.Bounds.Top + localDipPoint.Y * scaleY);
    }

    private static bool IsNearMonitor(StickmanCharacter c, MonitorInfoData mon, double dpiScale)
        => IsNearMonitor(c.X, c.Y, mon, dpiScale);

    private static bool IsNearMonitor(double x, double y, MonitorInfoData mon, double dpiScale)
    {
        double m = 90.0 * dpiScale;
        return x >= mon.Bounds.Left - m &&
               x <= mon.Bounds.Right + m &&
               y >= mon.Bounds.Top - m &&
               y <= mon.Bounds.Bottom + m;
    }

    private void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OverlayMouseLeftDown?.Invoke(this, ToPhysicalScreenPoint(e.GetPosition(this)));
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        OverlayMouseMove?.Invoke(this, ToPhysicalScreenPoint(e.GetPosition(this)));
    }

    private void OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OverlayMouseLeftUp?.Invoke(this, ToPhysicalScreenPoint(e.GetPosition(this)));
    }

    private void OnMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OverlayMouseRightUp?.Invoke(this, ToPhysicalScreenPoint(e.GetPosition(this)));
    }
}
