using System.Runtime.InteropServices;

namespace TodoList.Services;

/// <summary>
/// 系统托盘图标服务（原生 Shell_NotifyIcon，零第三方依赖）。
/// 独立 message-only 窗口接收回调，不干扰 WinUI 主窗口的消息处理。
/// 交互：双击/左键 → 恢复主窗口；右键 → 菜单（显示主窗口 / 退出）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    /// <summary>用户请求显示/恢复主窗口（双击、左键、菜单"显示主窗口"）。</summary>
    public event Action? ActivateRequested;

    /// <summary>用户选择退出（菜单"退出"）。</summary>
    public event Action? ExitRequested;

    private const uint WmAppCallback = 0x8000 + 1; // WM_APP+1，托盘回调消息
    private const uint WmCommand = 0x0111;
    private const uint WmNull = 0x0000;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint NinSelect = 0x0400; // NIN_SELECT (WM_USER)

    private const uint CmdShow = 1001;
    private const uint CmdExit = 1002;

    private const uint NimAdd = 0x0;
    private const uint NimDelete = 0x2;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;

    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;

    private const string WindowClassName = "TodoListTrayMessageWindow";

    private readonly IntPtr _hInstance;
    private IntPtr _msgHwnd;
    private IntPtr _iconHandle;
    private bool _iconShared; // LoadIcon 返回共享句柄，Dispose 时不可 DestroyIcon
    private bool _registered;

    // 保活：托管委托被原生回调引用期间不能被 GC 回收
    private WndProcDelegate? _wndProc;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private bool _disposed;

    public TrayIconService()
    {
        _hInstance = GetModuleHandle(null);
    }

    /// <summary>创建消息窗口并添加托盘图标。应用启动时调用一次。</summary>
    public void Start()
    {
        if (_msgHwnd != IntPtr.Zero)
            return;

        _wndProc = WndProc;
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = WindowClassName
        };
        _registered = RegisterClassEx(ref wc) != 0;

        _msgHwnd = CreateWindowEx(
            0, WindowClassName, string.Empty, 0,
            0, 0, 0, 0,
            new IntPtr(-3) /* HWND_MESSAGE */,
            IntPtr.Zero, _hInstance, IntPtr.Zero);

        if (_msgHwnd == IntPtr.Zero)
            return;

        _iconHandle = CreateAppIcon();

        var nid = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _msgHwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmAppCallback,
            hIcon = _iconHandle,
            szTip = "TodoList"
        };
        var ok = Shell_NotifyIcon(NimAdd, ref nid);
        _ = ok; // 托盘添加失败时窗口功能不受影响，忽略
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmAppCallback)
        {
            var mouseMsg = (uint)lParam.ToInt64();
            if (mouseMsg is WmLButtonDblClk or WmLButtonUp or NinSelect)
            {
                ActivateRequested?.Invoke();
                return IntPtr.Zero;
            }
            if (mouseMsg == WmRButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }
        else if (msg == WmCommand)
        {
            var cmd = (uint)(wParam.ToInt64() & 0xFFFF);
            if (cmd == CmdShow)
            {
                ActivateRequested?.Invoke();
                return IntPtr.Zero;
            }
            if (cmd == CmdExit)
            {
                ExitRequested?.Invoke();
                return IntPtr.Zero;
            }
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        AppendMenu(menu, MfString, new IntPtr(CmdShow), "显示主窗口");
        AppendMenu(menu, MfString, new IntPtr(CmdExit), "退出");

        GetCursorPos(out var pt);
        // 必须先激活消息窗口，否则弹出菜单点击后不会自动关闭（Win32 已知行为）
        SetForegroundWindow(_msgHwnd);
        TrackPopupMenu(menu, TpmRightButton, pt.X, pt.Y, 0, _msgHwnd, IntPtr.Zero);
        PostMessage(_msgHwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    /// <summary>
    /// 绘制品牌图标：蓝底圆角方块 + 白色 "T"。
    /// 用 32bpp DIB section（CreateIconIndirect 对 DDB 会失败，DIB + alpha 是标准路径）；
    /// GDI 不写 alpha 通道，绘制后按像素颜色补 0xFF。
    /// GDI 句柄按规范全量释放（DeleteObject → DeleteDC → DestroyIcon 由 Dispose 负责）。
    /// </summary>
    private IntPtr CreateAppIcon()
    {
        const int size = 32;
        const int color = 0x00FF6F4C; // RGB(76,111,255) → BGR（AccentBrush #4C6FFF）

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);

        // 32bpp DIB section
        var bmi = new BitmapInfoHeader
        {
            biSize = 40,
            biWidth = size,
            biHeight = size,
            biPlanes = 1,
            biBitCount = 32
        };
        var dib = CreateDIBSection(memDc, ref bmi, 0 /*DIB_RGB_COLORS*/, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            _iconShared = true;
            return LoadIcon(IntPtr.Zero, new IntPtr(32512));
        }

        // DIB 内存未初始化，先清零（透明角落）
        Marshal.Copy(new byte[size * size * 4], 0, bits, size * size * 4);

        var oldBmp = SelectObject(memDc, dib);
        var brush = CreateSolidBrush(color);
        var oldBrush = SelectObject(memDc, brush);
        var oldPen = SelectObject(memDc, GetStockObject(5 /*NULL_PEN*/));
        RoundRect(memDc, 1, 1, size - 1, size - 1, 8, 8);

        SetBkMode(memDc, 1 /*TRANSPARENT*/);
        SetTextColor(memDc, 0x00FFFFFF);
        var font = CreateFont(-17, 0, 0, 0, 700 /*FW_BOLD*/, 0, 0, 0, 1 /*DEFAULT_CHARSET*/,
            0, 0, 0, 0, "Segoe UI");
        var oldFont = SelectObject(memDc, font);
        TextOut(memDc, 11, 5, "T", 1);

        // 补 alpha：非黑像素（绘制过的）→ 不透明；未绘制 → 全透明
        var buf = new byte[size * size * 4];
        Marshal.Copy(bits, buf, 0, buf.Length);
        for (var i = 0; i < buf.Length; i += 4)
            buf[i + 3] = (byte)((buf[i] | buf[i + 1] | buf[i + 2]) != 0 ? 0xFF : 0x00);
        Marshal.Copy(buf, 0, bits, buf.Length);

        SelectObject(memDc, oldBmp);
        SelectObject(memDc, oldFont);
        SelectObject(memDc, oldPen);
        SelectObject(memDc, oldBrush);

        // mask 必须是 CreateBitmap 的新鲜位图、不可绘制（PatBlt/FillRect 会使 CreateIconIndirect 失败，
        // 已用实验矩阵验证）；新分配内存实践为零 → 全显示，透明度由 DIB alpha 通道决定
        var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);

        var info = new IconInfo
        {
            fIcon = 1,
            hbmMask = mask,
            hbmColor = dib
        };
        var icon = CreateIconIndirect(ref info);
        if (icon == IntPtr.Zero)
        {
            // 兜底1：经典 LoadIcon（共享句柄，无需销毁）
            icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
            if (icon != IntPtr.Zero)
                _iconShared = true;
        }
        if (icon == IntPtr.Zero)
        {
            // 兜底2：LoadImage OEM 资源
            icon = LoadImage(IntPtr.Zero, new IntPtr(32512),
                1 /* IMAGE_ICON */, 32, 32, 0x0040 /* LR_DEFAULTSIZE */);
        }

        DeleteObject(font);
        DeleteObject(brush);
        DeleteObject(mask);
        DeleteObject(dib);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
        return icon;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_msgHwnd != IntPtr.Zero)
        {
            var nid = new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = _msgHwnd,
                uID = 1
            };
            Shell_NotifyIcon(NimDelete, ref nid);
            DestroyWindow(_msgHwnd);
            _msgHwnd = IntPtr.Zero;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            // LoadIcon 的共享句柄不可销毁（系统全局复用）
            if (!_iconShared)
                DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        if (_registered)
        {
            UnregisterClass(WindowClassName, _hInstance);
            _registered = false;
        }

        GC.KeepAlive(_wndProc);
        _wndProc = null;
    }

    // ───────── native ─────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
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
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    /// <summary>NOTIFYICONDATA 到 szTip 为止（cbSize 按本结构大小，系统向后兼容）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    /// <summary>
    /// ICONINFO：fIcon 必须用 int（4 字节 1）——实测 bool（即使 [MarshalAs(Bool)]、结构大小同为 24）
    /// 在 CreateIconIndirect 下稳定失败；位图也不能复用（每次调用需新建）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int fIcon;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint iUsage,
        out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, int nPlanes, int nBitsPerPixel, IntPtr lpBits);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int i);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool RoundRect(IntPtr hdc, int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr hdc, int color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int cHeight, int cWidth, int cEscapement, int cOrientation,
        int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
        uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFacename);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOut(IntPtr hdc, int x, int y, string lpString, int c);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo piconinfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
