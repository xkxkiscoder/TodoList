using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TodoList.Services;

/// <summary>
/// 「缩小为气泡」悬浮窗：点击主窗口标题栏气泡按钮时，隐藏主窗口并弹出一个
/// 小圆形悬浮气泡，显示未完成待办数；单击气泡恢复主窗口，按住可拖动。
/// 实测：对 WinUI 的第二个 XAML Window 做 SetWindowLong/SetWindowRgn 会触发
/// XAML stowed exception 崩溃（0xc000027b），故气泡改用纯 Win32 自绘窗口
/// （与 TrayIconService 同一模式），完全不与 XAML 合成层冲突。
/// 窗口在 UI 线程创建，回调天然在 UI 线程。
/// </summary>
public sealed class BubbleWindowService : IDisposable
{
    /// <summary>单击气泡请求恢复主窗口。</summary>
    public event Action? RestoreRequested;

    private IntPtr _hwnd;
    private NativeMethods.WndProcDelegate? _wndProc; // 保活，防止被 GC
    private IntPtr _font;
    private bool _visible;
    private bool _hover;
    private bool _disposed;

    // 拖动状态
    private bool _captured;
    private bool _dragged;
    private int _grabX, _grabY;

    private string _countText = "0";

    // 物理像素尺寸（创建时按主窗口 DPI 计算）
    private int _size = 56;
    private int _circle = 50;

    /// <summary>当前是否处于气泡模式（主窗口已隐藏、气泡显示中）。</summary>
    public bool IsActive => _visible;

    /// <summary>隐藏主窗口并显示气泡；气泡位置优先保留在主窗口原右上角附近。</summary>
    public void EnterBubbleMode(Window main, int pendingCount)
    {
        if (_disposed) return;
        EnsureCreated(main);
        _countText = FormatCount(pendingCount);
        PositionNear(main);
        NativeMethods.ShowWindow(WindowNative.GetWindowHandle(main), NativeMethods.SW_HIDE);
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        // SW_SHOWNOACTIVATE 后兜底置顶（不抢焦点）
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        _visible = true;
    }

    /// <summary>恢复主窗口并收起气泡（气泡窗口保留复用，仅隐藏）。</summary>
    public void ExitBubbleMode(Window main)
    {
        if (_hwnd != IntPtr.Zero)
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        _visible = false;
        _hover = false;

        var mainHwnd = WindowNative.GetWindowHandle(main);
        NativeMethods.ShowWindow(mainHwnd, NativeMethods.SW_RESTORE);
        main.Activate();
    }

    /// <summary>刷新气泡上的待办数（主窗口侧数据变更后调用）。</summary>
    public void UpdateCount(int pendingCount)
    {
        var text = FormatCount(pendingCount);
        if (text == _countText) return;
        _countText = text;
        if (_hwnd != IntPtr.Zero)
            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private static string FormatCount(int pendingCount)
        => pendingCount > 99 ? "99+" : pendingCount.ToString();

    // ───────── 纯 Win32 自绘窗口 ─────────

    private static bool _classRegistered;

    private void EnsureCreated(Window main)
    {
        if (_hwnd != IntPtr.Zero) return;

        var instance = NativeMethods.GetModuleHandleW(null);
        if (!_classRegistered)
        {
            _wndProc = WndProc;
            var wc = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = instance,
                hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_ARROW),
                lpszClassName = "TodoListBubbleWndClass"
            };
            if (NativeMethods.RegisterClassExW(ref wc) == 0)
                throw new InvalidOperationException("气泡窗口类注册失败");
            _classRegistered = true;
        }

        // 按主窗口 DPI 缩放尺寸与字体
        double scale;
        try
        {
            scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(main)) / 96.0;
        }
        catch
        {
            scale = 1.0;
        }
        _size = (int)Math.Round(56 * scale);
        _circle = (int)Math.Round(50 * scale);
        _font = NativeMethods.CreateFontW(
            -(int)Math.Round(18 * scale), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 0, 0, "Segoe UI");

        _hwnd = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST,
            "TodoListBubbleWndClass",
            "TodoList 气泡",
            NativeMethods.WS_POPUP,
            0, 0, _size, _size,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("气泡窗口创建失败");

        // 裁成圆形：自有 Win32 窗口上 SetWindowRgn 安全（崩溃仅发生在 XAML 窗口上）
        var rgn = NativeMethods.CreateRoundRectRgn(0, 0, _size, _size, _size, _size);
        _ = NativeMethods.SetWindowRgn(_hwnd, rgn, false); // 所有权移交系统，不释放
    }

    /// <summary>气泡出现在主窗口右上角附近，并夹取到最近显示器工作区内。</summary>
    private void PositionNear(Window main)
    {
        var mainHwnd = WindowNative.GetWindowHandle(main);
        NativeMethods.GetWindowRect(mainHwnd, out var mr);

        int x = mr.Right - _size - 8;
        int y = mr.Top + 8;

        // 夹取到主窗口所在显示器的工作区
        var monitor = NativeMethods.MonitorFromWindow(mainHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            var wa = info.rcWork;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            if (x + _size > wa.Right) x = wa.Right - _size;
            if (y + _size > wa.Bottom) y = wa.Bottom - _size;
        }

        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_PAINT:
                OnPaint(hwnd);
                return IntPtr.Zero;

            case NativeMethods.WM_ERASEBKGND:
                return (IntPtr)1; // 自绘全圆，跳过背景填充避免闪烁

            case NativeMethods.WM_LBUTTONDOWN:
                _captured = true;
                _dragged = false;
                _grabX = (short)(lParam.ToInt64() & 0xFFFF); // 客户区坐标
                _grabY = (short)(lParam.ToInt64() >> 16);
                _ = NativeMethods.SetCapture(hwnd);
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSEMOVE:
                OnMouseMove(hwnd);
                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONUP:
                if (_captured)
                {
                    _ = NativeMethods.ReleaseCapture();
                    _captured = false;
                    if (!_dragged)
                        RestoreRequested?.Invoke(); // 单击（未拖动）→ 恢复主窗口
                    _dragged = false;
                }
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSELEAVE:
                if (_hover)
                {
                    _hover = false;
                    NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                return IntPtr.Zero;

            case NativeMethods.WM_DESTROY:
                _hwnd = IntPtr.Zero;
                return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnMouseMove(IntPtr hwnd)
    {
        if (_captured)
        {
            // 按住拖动：光标位置 - 抓取偏移 = 新窗口位置
            if (!NativeMethods.GetCursorPos(out var p)) return;
            var nx = p.X - _grabX;
            var ny = p.Y - _grabY;
            if (!_dragged && Math.Abs(nx) + Math.Abs(ny) > 4)
                _dragged = true;
            if (_dragged)
            {
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, nx, ny, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
            }
            return;
        }

        // 悬停高亮（TrackMouseEvent 重复注册无害）
        if (!_hover)
        {
            _hover = true;
            NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, true);
        }
        var tme = new NativeMethods.TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
            dwFlags = NativeMethods.TME_LEAVE,
            hwndTrack = hwnd
        };
        _ = NativeMethods.TrackMouseEvent(ref tme);
    }

    private void OnPaint(IntPtr hwnd)
    {
        if (!NativeMethods.BeginPaint(hwnd, out var ps)) return;
        try
        {
            NativeMethods.GetClientRect(hwnd, out var rc);

            // 深色圆（悬停微亮）；COLORREF = 0x00BBGGRR
            var color = (uint)(_hover ? 0x403A2E : 0x33241F);
            var bg = NativeMethods.CreateSolidBrush(color);
            var pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, color);
            var oldBrush = NativeMethods.SelectObject(ps.hdc, bg);
            var oldPen = NativeMethods.SelectObject(ps.hdc, pen);

            var inset = (_size - _circle) / 2;
            NativeMethods.Ellipse(ps.hdc, inset, inset, rc.Right - inset, rc.Bottom - inset);

            // 数字 / 全部完成时的对勾
            if (_font != IntPtr.Zero)
            {
                var oldFont = NativeMethods.SelectObject(ps.hdc, _font);
                NativeMethods.SetBkMode(ps.hdc, NativeMethods.TRANSPARENT);
                var showCheck = _countText == "0";
                NativeMethods.SetTextColor(ps.hdc, (uint)(showCheck ? 0xFFB49C : 0xFFFFFF));
                var text = showCheck ? "✓" : _countText;
                var trc = rc;
                NativeMethods.DrawTextW(ps.hdc, text, -1, ref trc,
                    NativeMethods.DT_CENTER | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE);
                _ = NativeMethods.SelectObject(ps.hdc, oldFont);
            }

            _ = NativeMethods.SelectObject(ps.hdc, oldBrush);
            _ = NativeMethods.SelectObject(ps.hdc, oldPen);
            _ = NativeMethods.DeleteObject(bg);
            _ = NativeMethods.DeleteObject(pen);
        }
        finally
        {
            NativeMethods.EndPaint(hwnd, ref ps);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RestoreRequested = null;
        if (_hwnd != IntPtr.Zero)
        {
            _ = NativeMethods.DestroyWindow(_hwnd); // WndProc 收 WM_DESTROY 后清 _hwnd
            _hwnd = IntPtr.Zero;
        }
        if (_font != IntPtr.Zero)
        {
            _ = NativeMethods.DeleteObject(_font);
            _font = IntPtr.Zero;
        }
    }
}

/// <summary>气泡自绘窗口专用 Win32 声明（勿与 MainWindow.NativeMethods 混用）。</summary>
internal static class NativeMethods
{
    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    // 窗口
    public const uint WS_POPUP = 0x80000000u;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;

    // 消息
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_MOUSELEAVE = 0x02A2;

    // GDI
    public const int PS_SOLID = 0;
    public const int TRANSPARENT = 1;
    public const int DT_CENTER = 0x0001;
    public const int DT_VCENTER = 0x0004;
    public const int DT_SINGLELINE = 0x0020;

    // 其它
    public const uint TME_LEAVE = 0x0002;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int IDC_ARROW = 32512;

    public const int SW_HIDE = 0;
    public const int SW_RESTORE = 9;
    public const int SW_SHOWNOACTIVATE = 4;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOZORDER = 0x0004;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, int lpCursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation,
        int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
        uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(int iStyle, int cWidth, uint crColor);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    public static extern int SetTextColor(IntPtr hdc, uint color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [DllImport("user32.dll")]
    public static extern bool BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hWnd, in PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);
}
