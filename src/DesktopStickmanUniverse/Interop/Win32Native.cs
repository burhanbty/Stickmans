using System.Runtime.InteropServices;
using System.Text;

namespace DesktopStickmanUniverse.Interop;

/// <summary>
/// Centralized Win32 P/Invoke declarations for window tracking, DWM bounds,
/// per-monitor DPI, taskbar state, fullscreen detection, and overlay input control.
/// </summary>
internal static class Win32Native
{
    internal const int GWL_EXSTYLE = -20;

    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const int WM_MOUSEACTIVATE = 0x0021;
    internal const int MA_NOACTIVATE = 3;
    internal const int WM_DPICHANGED = 0x02E0;
    internal const int WM_DISPLAYCHANGE = 0x007E;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;

    internal const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const uint DWMWA_CLOAKED = 14;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    internal const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    internal const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    internal const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    internal const uint EVENT_OBJECT_CREATE = 0x8000;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    internal const uint EVENT_OBJECT_CLOAKED = 0x8017;
    internal const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    internal const uint ABM_GETSTATE = 0x00000004;
    internal const uint ABM_GETTASKBARPOS = 0x00000005;
    internal const int ABS_AUTOHIDE = 0x0000001;

    internal const int ABE_LEFT = 0;
    internal const int ABE_TOP = 1;
    internal const int ABE_RIGHT = 2;
    internal const int ABE_BOTTOM = 3;

    internal const int MDT_EFFECTIVE_DPI = 0;
    internal const int MONITORINFOF_PRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    internal const int VK_LBUTTON = 0x01;
    internal const int VK_RBUTTON = 0x02;

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    internal delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    internal enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    internal static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    internal static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("shell32.dll")]
    internal static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    internal static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int len = GetClassName(hWnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    internal static string GetWindowTitleText(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(len + 1, 256));
        int read = GetWindowText(hWnd, sb, sb.Capacity);
        return read > 0 ? sb.ToString() : string.Empty;
    }

    internal static bool IsWindowCloaked(IntPtr hWnd)
    {
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, Marshal.SizeOf<int>());
        return hr == 0 && cloaked != 0;
    }

    internal static bool TryGetExtendedWindowRect(IntPtr hWnd, out RECT rect)
    {
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
        if (hr == 0 && rect.Width > 0 && rect.Height > 0)
        {
            return true;
        }

        return GetWindowRect(hWnd, out rect) && rect.Width > 0 && rect.Height > 0;
    }

    internal static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    internal static void ApplyRoundedCorners(IntPtr hwnd)
    {
        try
        {
            int pref = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, Marshal.SizeOf<int>());
        }
        catch
        {
            // Ignore on older builds or unsupported surfaces.
        }
    }
}
