using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TodoList.Models;
using TodoList.Services;
using TodoList.ViewModels;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace TodoList;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private enum ViewMode
    {
        All,
        Today,
        Calendar
    }

    private readonly StartupService _startup = new();
    private readonly AppSettings _settings = AppSettings.Load();

    private readonly ObservableCollection<TodoItemVm> _allItems = new();
    private readonly ObservableCollection<TodoItemVm> _dayItems = new();

    private ViewMode _view = ViewMode.All;
    private DateOnly _selectedDay = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _visibleMonth = new(DateOnly.FromDateTime(DateTime.Today).Year, DateOnly.FromDateTime(DateTime.Today).Month, 1);
    private bool _suppressSettingsEvents;
    private DispatcherTimer? _statusCollapseTimer;
    private TodoItemVm? _expandedStatusVm;
    private readonly Dictionary<string, bool> _groupExpanded = new();

    private TrayIconService? _tray;
    private bool _exitRequested; // 托盘菜单"退出"时置位，区别于点 X 隐藏到托盘

    public MainWindow()
    {
        InitializeComponent();

        SetupCustomTitleBar();
        ApplyWindowSettings();
        RestoreWindowSize();

        _suppressSettingsEvents = true;
        TopMostSwitch.IsOn = _settings.AlwaysOnTop;
        HideCompletedSwitch.IsOn = _settings.HideCompleted;
        StartupSwitch.IsOn = _startup.IsEnabled || _settings.StartWithWindows;
        _suppressSettingsEvents = false;

        Root.KeyDown += Root_KeyDown;
        Closed += MainWindow_Closed;

        // WAS 1.5 的 Window 无 Closing 事件：子类化 WndProc 拦截 WM_CLOSE（Alt+F4/系统关闭）
        HookWindowClose();

        // 托盘：窗口不在任务栏/Alt-Tab 显示，常驻通知区域
        AppWindow.IsShownInSwitchers = false;
        _tray = new TrayIconService();
        _tray.ActivateRequested += ShowMainWindowFromTray;
        _tray.ExitRequested += ExitFromTray;
        _tray.Start();

        ViewModel.EditRequested += (_, vm) => OpenDetail(vm);
        ViewModel.GroupsChanged += (_, _) => UpdateEmptyState();

        ShowView(_settings.LastView switch
        {
            1 => ViewMode.Today,
            2 => ViewMode.Calendar,
            _ => ViewMode.All
        });
    }

    /// <summary>从托盘恢复：WinUI 3 的 Window 无 Show()，用 user32 ShowWindow 恢复。</summary>
    private void ShowMainWindowFromTray()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            Activate();
        });
    }

    private void ExitFromTray()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _exitRequested = true;
            Close();
        });
    }

    // ───────── 关闭 → 隐藏到托盘 ─────────

    private NativeMethods.WndProcDelegate? _hookedWndProc; // 保活，防止被 GC

    /// <summary>
    /// 子类化主窗口 WndProc：拦截 WM_CLOSE（Alt+F4、系统关闭），未请求退出时改为隐藏到托盘。
    /// WAS 1.5 的 Window 没有 Closing 事件，只能在 Win32 层拦截。
    /// </summary>
    private void HookWindowClose()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _hookedWndProc = WndProcHook;
        var newProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_hookedWndProc);
        _prevWndProc = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_WNDPROC, newProc);
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WmClose = 0x0010;
        if (msg == WmClose && !_exitRequested)
        {
            // WAS 1.5 的 Window 无 Hide()，用 user32 隐藏到托盘，不销毁窗口
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_HIDE);
            return IntPtr.Zero;
        }
        return NativeMethods.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    private IntPtr _prevWndProc;

    // ───────── window chrome ─────────

    private AppWindow GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    /// <summary>
    /// 自定义标题栏：不用 SetTitleBar 全覆盖（会导致还原态按钮点不动），
    /// 改用拖拽矩形覆盖工具栏空白区，排除右侧窗口按钮。
    /// </summary>
    private void SetupCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        var titleBar = GetAppWindow().TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
        titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 60, 60, 60);
        titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 20, 20, 20);
        try
        {
            titleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        }
        catch
        {
            // ignore
        }

        SizeChanged += (_, _) =>
        {
            ApplyAdaptiveStates();
            DispatcherQueue.TryEnqueue(UpdateDragRegions);
        };
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateDragRegions);
        if (Root is not null)
            Root.Loaded += (_, _) =>
            {
                ApplyAdaptiveStates();
                DispatcherQueue.TryEnqueue(UpdateDragRegions);
            };
        UpdateDragRegions();
    }

    /// <summary>
    /// 自适应布局（实测结论见 AGENTS.md §3.1）：
    /// AdaptiveTrigger / GoToState 在本项目（WinUI 3 Window 直接内容、无 Page）
    /// 均不可用——状态组能读到但既不自动评估、GoToState 也返回 false。
    /// 故用 SizeChanged 驱动的集中函数切换，规则本身仍按状态组组织。
    /// </summary>
    private void ApplyAdaptiveStates()
    {
        UpdateQuickAddLayout();
        UpdateCalendarLayout();
    }

    private bool? _quickAddNarrow;

    /// <summary>空间不足时快速添加行换行为两行：标题独占第一行。</summary>
    private void UpdateQuickAddLayout()
    {
        if (QuickAddRow is null || QuickTitle is null || QuickPriority is null
            || QuickDate is null || QuickAddButton is null)
            return;
        if (QuickAddRow.ActualWidth <= 0)
            return;

        var narrow = QuickAddRow.ActualWidth < 520;
        if (_quickAddNarrow == narrow)
            return;
        _quickAddNarrow = narrow;

        var cols = QuickAddRow.ColumnDefinitions;
        if (narrow)
        {
            // 列宽：优先级按内容收窄，计划日吃满剩余直到按钮
            cols[0].Width = new GridLength(1, GridUnitType.Auto);
            cols[1].Width = new GridLength(1, GridUnitType.Star);
            cols[2].Width = new GridLength(0);

            // 第一行：标题占满整行
            Grid.SetRow(QuickTitle, 0);
            Grid.SetColumn(QuickTitle, 0);
            Grid.SetColumnSpan(QuickTitle, 4);

            // 第二行：优先级 + 计划日 + 添加按钮
            Grid.SetRow(QuickPriority, 1);
            Grid.SetColumn(QuickPriority, 0);
            Grid.SetColumnSpan(QuickPriority, 1);
            Grid.SetRow(QuickDate, 1);
            Grid.SetColumn(QuickDate, 1);
            Grid.SetColumnSpan(QuickDate, 2);
            Grid.SetRow(QuickAddButton, 1);
            Grid.SetColumn(QuickAddButton, 3);
            Grid.SetColumnSpan(QuickAddButton, 1);
        }
        else
        {
            cols[0].Width = new GridLength(1, GridUnitType.Star);
            cols[1].Width = new GridLength(110);
            cols[2].Width = new GridLength(120);

            Grid.SetRow(QuickTitle, 0);
            Grid.SetColumn(QuickTitle, 0);
            Grid.SetColumnSpan(QuickTitle, 1);
            Grid.SetRow(QuickPriority, 0);
            Grid.SetColumn(QuickPriority, 1);
            Grid.SetColumnSpan(QuickPriority, 1);
            Grid.SetRow(QuickDate, 0);
            Grid.SetColumn(QuickDate, 2);
            Grid.SetColumnSpan(QuickDate, 1);
            Grid.SetRow(QuickAddButton, 0);
            Grid.SetColumn(QuickAddButton, 3);
            Grid.SetColumnSpan(QuickAddButton, 1);
        }
    }

    private bool? _calendarNarrow;

    /// <summary>空间不足时日历改上下堆叠：月历在第一行，当日计划在第二行。</summary>
    private void UpdateCalendarLayout()
    {
        if (CalendarPanel is null || MonthCard is null || DayPlanCard is null)
            return;
        if (CalendarPanel.ActualWidth <= 0)
            return;

        var narrow = CalendarPanel.ActualWidth < 600;
        if (_calendarNarrow == narrow)
            return;
        _calendarNarrow = narrow;

        var cols = CalendarPanel.ColumnDefinitions;
        if (narrow)
        {
            cols[1].Width = new GridLength(0);

            Grid.SetRow(MonthCard, 0);
            Grid.SetColumn(MonthCard, 0);
            Grid.SetColumnSpan(MonthCard, 2);

            Grid.SetRow(DayPlanCard, 1);
            Grid.SetColumn(DayPlanCard, 0);
            Grid.SetColumnSpan(DayPlanCard, 2);
        }
        else
        {
            cols[1].Width = new GridLength(240);

            Grid.SetRow(MonthCard, 0);
            Grid.SetColumn(MonthCard, 0);
            Grid.SetColumnSpan(MonthCard, 1);

            Grid.SetRow(DayPlanCard, 0);
            Grid.SetColumn(DayPlanCard, 1);
            Grid.SetColumnSpan(DayPlanCard, 1);
        }
    }

    /// <summary>工具栏空白区可拖动；右侧按钮区排除在外，保证单击可用。</summary>
    private void UpdateDragRegions()
    {
        if (ToolbarHost is null || ActionsPanel is null || Root is null)
            return;

        try
        {
            double scale = 1.0;
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                // DPI via GetDpiForWindow if available - fallback 1
                scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
            }
            catch
            {
                scale = 1.0;
            }

            var toRoot = ToolbarHost.TransformToVisual(Root);
            var tb = toRoot.TransformBounds(new Windows.Foundation.Rect(
                0, 0, ToolbarHost.ActualWidth, ToolbarHost.ActualHeight));

            var actToRoot = ActionsPanel.TransformToVisual(Root);
            var act = actToRoot.TransformBounds(new Windows.Foundation.Rect(
                0, 0, ActionsPanel.ActualWidth, ActionsPanel.ActualHeight));

            var segsToRoot = SegsPanel.TransformToVisual(Root);
            var segs = segsToRoot.TransformBounds(new Windows.Foundation.Rect(
                0, 0, SegsPanel.ActualWidth, SegsPanel.ActualHeight));

            var winToRoot = Root.TransformToVisual(null);
            var rootInWin = winToRoot.TransformBounds(new Windows.Foundation.Rect(
                0, 0, Root.ActualWidth, Root.ActualHeight));

            var tbLeft = rootInWin.X + tb.X;
            var tbTop = rootInWin.Y + tb.Y;
            var actLeft = rootInWin.X + act.X;
            var segsLeft = rootInWin.X + segs.X;
            var segsRight = rootInWin.X + segs.X + segs.Width;

            var height = tb.Height;
            var rects = new List<Windows.Graphics.RectInt32>();

            void AddRect(double x1, double x2)
            {
                var w = x2 - x1;
                if (w < 8) return;
                rects.Add(new Windows.Graphics.RectInt32(
                    (int)(x1 * scale),
                    (int)(tbTop * scale),
                    (int)(w * scale),
                    (int)(height * scale)));
            }

            AddRect(tbLeft, segsLeft - 6);
            // 右侧拖拽区只到按钮区左边界之前；折叠态下边界取设置按钮右侧，
            // 让折叠关闭按钮与下方浮层都可点击（act.X 已是 ActionsPanel 整体左边界）。
            AddRect(segsRight + 6, actLeft - 8);

            AppWindow.TitleBar.SetDragRectangles(
                rects.Count > 0 ? rects.ToArray() : Array.Empty<Windows.Graphics.RectInt32>());
        }
        catch
        {
            // 忽略变换失败
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_exitRequested)
        {
            Close();
            return;
        }
        // 点 X 隐藏到托盘（图标常驻，可恢复或从菜单退出）
        NativeMethods.ShowWindow(WindowNative.GetWindowHandle(this), NativeMethods.SW_HIDE);
    }

    private void ApplyWindowSettings()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (_settings.AlwaysOnTop)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            SetTopMost(hwnd, true);
        }

        TryMoveWindow(appWindow);
    }

    private static void SetTopMost(IntPtr hwnd, bool topMost)
    {
        var insertAfter = topMost ? new IntPtr(-1) : new IntPtr(-2);
        _ = NativeMethods.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void TryMoveWindow(AppWindow appWindow)
    {
        if (double.IsNaN(_settings.WindowLeft) || double.IsNaN(_settings.WindowTop))
            return;

        try
        {
            appWindow.Move(new Windows.Graphics.PointInt32(
                (int)_settings.WindowLeft,
                (int)_settings.WindowTop));
        }
        catch
        {
            // ignore invalid placement
        }
    }

    private void RestoreWindowSize()
    {
        var appWindow = GetAppWindow();

        // 始终以普通（非最大化）窗口启动
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Maximized
                || presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        }

        // 工作区，用于判断是否接近全屏
        Windows.Graphics.RectInt32 work = default;
        var hasWork = false;
        try
        {
            work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            hasWork = work.Width > 0 && work.Height > 0;
        }
        catch
        {
            // ignore
        }

        double width = _settings.WindowWidth;
        double height = _settings.WindowHeight;

        // 非法或接近全屏的尺寸 → 回落到默认 920×680
        const double defaultW = 920;
        const double defaultH = 680;
        if (double.IsNaN(width) || width < 480)
            width = defaultW;
        if (double.IsNaN(height) || height < 360)
            height = defaultH;

        if (hasWork)
        {
            if (width >= work.Width * 0.96)
                width = Math.Min(defaultW, work.Width * 0.85);
            if (height >= work.Height * 0.96)
                height = Math.Min(defaultH, work.Height * 0.85);
        }

        appWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Round(width),
            (int)Math.Round(height)));

        // 默认停靠右上角（工作区），避免居中/残留旧坐标
        if (hasWork)
        {
            var left = work.X + work.Width - (int)Math.Round(width) - 16;
            var top = work.Y + 16;
            if (left < work.X) left = work.X;
            if (top < work.Y) top = work.Y;
            try
            {
                appWindow.Move(new Windows.Graphics.PointInt32(left, top));
            }
            catch
            {
                // ignore
            }
        }
        else if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
        {
            try
            {
                appWindow.Move(new Windows.Graphics.PointInt32(
                    (int)_settings.WindowLeft,
                    (int)_settings.WindowTop));
            }
            catch
            {
                // ignore
            }
        }

        DispatcherQueue.TryEnqueue(UpdateDragRegions);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        PersistWindowBounds();
        _settings.AlwaysOnTop = TopMostSwitch.IsOn;
        _settings.HideCompleted = HideCompletedSwitch.IsOn;
        _settings.StartWithWindows = StartupSwitch.IsOn;
        _settings.LastView = _view switch
        {
            ViewMode.Today => 1,
            ViewMode.Calendar => 2,
            _ => 0
        };
        _settings.Save();

        // 真正退出时清理托盘图标与消息窗口
        _tray?.Dispose();
        _tray = null;
    }

    private void PersistWindowBounds()
    {
        var appWindow = GetAppWindow();

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Maximized)
        {
            // 最大化时不写入尺寸，避免下次按最大化尺寸启动
            return;
        }

        var pos = appWindow.Position;
        var size = appWindow.Size;

        try
        {
            var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            // 接近全屏不写入，避免下次“默认全屏”
            if (work.Width > 0 && work.Height > 0
                && (size.Width >= work.Width * 0.96 || size.Height >= work.Height * 0.96))
            {
                return;
            }
        }
        catch
        {
            // ignore
        }

        _settings.WindowLeft = pos.X;
        _settings.WindowTop = pos.Y;
        _settings.WindowWidth = size.Width;
        _settings.WindowHeight = size.Height;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl && e.Key == VirtualKey.N)
        {
            QuickTitle.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (ctrl && e.Key == VirtualKey.F)
        {
            if (_view != ViewMode.Calendar)
                SearchBox.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (ctrl && e.Key == VirtualKey.Number1)
        {
            ShowView(ViewMode.All);
            e.Handled = true;
        }
        else if (ctrl && e.Key == VirtualKey.Number2)
        {
            ShowView(ViewMode.Today);
            e.Handled = true;
        }
        else if (ctrl && e.Key == VirtualKey.Number3)
        {
            ShowView(ViewMode.Calendar);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            if (DetailPanel.Visibility == Visibility.Visible)
            {
                CloseDetail();
            }
            else
            {
                CloseSettings();
                ViewModel.DismissSnack();
            }
            e.Handled = true;
        }
    }

    // ───────── views ─────────

    private void ShowView(ViewMode mode)
    {
        _view = mode;
        SegAll.IsChecked = mode == ViewMode.All;
        SegToday.IsChecked = mode == ViewMode.Today;
        SegCalendar.IsChecked = mode == ViewMode.Calendar;

        // 从详情页切回列表视图
        _detailVm = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        QuickAddRow.Visibility = Visibility.Visible;

        ListPanel.Visibility = mode == ViewMode.Calendar ? Visibility.Collapsed : Visibility.Visible;
        CalendarPanel.Visibility = mode == ViewMode.Calendar ? Visibility.Visible : Visibility.Collapsed;

        // 面板首次可见时 ActualWidth 要等布局 pass 完成，入队补算（否则窄态不生效）
        if (mode == ViewMode.Calendar)
            DispatcherQueue.TryEnqueue(UpdateCalendarLayout);

        ListTitle.Text = mode == ViewMode.Today ? "今日计划" : "全部待办";
        Refresh();
    }

    private void SegAll_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.All);
    private void SegToday_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.Today);
    private void SegCalendar_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.Calendar);

    // ───────── data ─────────

    private void Refresh()
    {
        if (this.MainListView is null || ListCount is null)
            return;

        if (_view == ViewMode.Calendar)
        {
            ViewModel.CurrentView = AppViewMode.Calendar;
            RefreshCalendar();
            return;
        }

        // 走 ViewModel：加载 + 分组；ItemsSource 由 XAML x:Bind 提供
        if (_view == ViewMode.Today)
            ViewModel.CurrentView = AppViewMode.Today;
        else
            ViewModel.CurrentView = AppViewMode.All;

        ListCount.Text = ViewModel.ListCountText;
        UpdateEmptyState();
    }

    /// <summary>按状态分组绑定到列表（未开始 / 进行中 / 已完成 + 数量，标题可折叠）。</summary>
    private void BindGroupedItems()
    {
        BindGroupsTo(this.MainListView, _allItems);
    }

    private void BindDayGroupedItems()
    {
        BindGroupsTo(DayList, _dayItems);
    }

    private void BindGroupsTo(ListView listView, IEnumerable<TodoItemVm> source)
    {
        StatusGroup Make(string title, TodoStatus status)
        {
            if (!_groupExpanded.TryGetValue(title, out var expanded))
            {
                expanded = true;
                _groupExpanded[title] = true;
            }

            return new StatusGroup(
                title,
                source.Where(i => i.Status == status),
                expanded);
        }

        var groups = new List<StatusGroup>
        {
            Make("未开始", TodoStatus.NotStarted),
            Make("进行中", TodoStatus.InProgress),
            Make("已完成", TodoStatus.Completed)
        };

        groups.RemoveAll(g => g.Count == 0);

        var view = new CollectionViewSource
        {
            Source = groups,
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath(nameof(StatusGroup.Items))
        };
        listView.ItemsSource = view.View;
    }

    private void GroupHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StatusGroup group })
            group.ToggleExpandCommand.Execute(null);
        e.Handled = true;
    }

    private TodoStatus? ReadStatusFilter() => null;

    private void RefreshCalendar()
    {
        MonthTitle.Text = $"{_visibleMonth.Year}年{_visibleMonth.Month}月";
        BuildCalendarDays();

        ViewModel.SelectDay(_selectedDay);
        DayTitle.Text = $"{_selectedDay.Month}月{_selectedDay.Day}日";
    }

    private void BuildCalendarDays()
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (var r = 0; r < 6; r++)
            CalendarGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 40
            });
        for (var c = 0; c < 7; c++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var first = _visibleMonth;
        var start = ((int)first.DayOfWeek + 6) % 7; // Monday-first
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var prevDays = first.AddMonths(-1).Day;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var dateCounts = ViewModel.Store.GetDatesWithTodos(first.Year, first.Month)
            .ToDictionary(d => d, d => ViewModel.Store.GetForDate(d).Count);

        for (var i = 0; i < 42; i++)
        {
            int day;
            var isOther = false;
            if (i < start)
            {
                day = prevDays - start + 1 + i;
                isOther = true;
            }
            else if (i - start < daysInMonth)
            {
                day = i - start + 1;
            }
            else
            {
                day = i - start - daysInMonth + 1;
                isOther = true;
            }

            var date = isOther
                ? (i < start ? first.AddMonths(-1) : first.AddMonths(1)).AddDays(day - 1)
                : first.AddDays(day - 1);

            var btn = new Button
            {
                Tag = date,
                MinHeight = 36,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = BuildDayContent(day, date, today, dateCounts, isOther)
            };
            if (isOther)
                btn.Opacity = 0.35;
            if (date == today && !isOther)
            {
                btn.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(0x88, 0x6B, 0x8C, 0xFF));
                btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(0x24, 0x6B, 0x8C, 0xFF));
            }
            else if (date == _selectedDay)
            {
                btn.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(0xD9, 0x6B, 0x8C, 0xFF));
                btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(0x47, 0x6B, 0x8C, 0xFF));
            }

            btn.Click += DayButton_Click;
            Grid.SetRow(btn, i / 7);
            Grid.SetColumn(btn, i % 7);
            CalendarGrid.Children.Add(btn);
        }
    }

    private static StackPanel BuildDayContent(
        int day,
        DateOnly date,
        DateOnly today,
        Dictionary<DateOnly, int> counts,
        bool isOther)
    {
        var panel = new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = day.ToString(),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        if (!isOther && counts.TryGetValue(date, out var n) && n > 0)
        {
            var dots = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            for (var k = 0; k < Math.Min(n, 3); k++)
            {
                dots.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        k == 0 && date < today ? Color.FromArgb(0xFF, 0xFF, 0x7A, 0x45) : Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF))
                });
            }
            panel.Children.Add(dots);
        }

        return panel;
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateOnly date })
        {
            _selectedDay = date;
            RefreshCalendar();
        }
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(-1);
        RefreshCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(1);
        RefreshCalendar();
    }

    private void GoToday_Click(object sender, RoutedEventArgs e)
    {
        _selectedDay = DateOnly.FromDateTime(DateTime.Today);
        _visibleMonth = new DateOnly(_selectedDay.Year, _selectedDay.Month, 1);
        RefreshCalendar();
    }

    // ───────── quick add / CRUD ─────────

    private TodoPriority ReadQuickPriority()
    {
        if (QuickPriority.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            return tag switch
            {
                "Low" => TodoPriority.Low,
                "High" => TodoPriority.High,
                "Critical" => TodoPriority.Critical,
                _ => TodoPriority.Medium
            };
        }
        return TodoPriority.Medium;
    }

    private DateOnly? ReadQuickDate()
    {
        if (QuickDate.Date is null) return null;
        return DateOnly.FromDateTime(QuickDate.Date.Value.LocalDateTime.Date);
    }

    private void QuickAddButton_Click(object sender, RoutedEventArgs e) => AddFromQuick();

    private void QuickTitle_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => AddFromQuick();

    private void QuickTitle_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // keep empty
    }

    private void AddFromQuick()
    {
        var title = QuickTitle.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(title))
        {
            QuickTitle.Focus(FocusState.Programmatic);
            return;
        }

        // 走 ViewModel：避免与主数据源脱节
        ViewModel.QuickTitle = title;
        ViewModel.QuickPriority = ReadQuickPriority();
        ViewModel.QuickPlannedDate = ReadQuickDate()
            ?? (_view == ViewMode.Calendar ? _selectedDay : null);
        ViewModel.AddTodoCommand.Execute(null);

        QuickTitle.Text = string.Empty;
        QuickDate.Date = null;
        Refresh();
        QuickTitle.Focus(FocusState.Programmatic);
    }

    // ───────── detail page ─────────

    private TodoItemVm? _detailVm;

    /// <summary>行点击：跳转详情页（点击行内按钮/状态胶囊时不触发）。</summary>
    private void Item_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;
        if (IsInsideButton(source, sender as DependencyObject))
            return;
        if ((sender as FrameworkElement)?.DataContext is TodoItemVm vm)
            OpenDetail(vm);
    }

    /// <summary>沿视觉树向上查找，判断点击源是否落在按钮内。</summary>
    private static bool IsInsideButton(DependencyObject source, DependencyObject? boundary)
    {
        var current = source;
        while (current is not null && current != boundary)
        {
            if (current is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OpenDetail(TodoItemVm vm)
    {
        _detailVm = vm;

        DetailTitleBox.Text = vm.Title;
        DetailNotesBox.Text = vm.Notes;
        SelectDetailStatus(vm.Status);
        SelectDetailPriority(vm.Priority);
        if (vm.PlannedDate is null)
        {
            DetailDatePick.Date = null;
            DetailDatePick.PlaceholderText = "未排期";
        }
        else
        {
            DetailDatePick.Date = new DateTimeOffset(
                vm.PlannedDate.Value.ToDateTime(TimeOnly.MinValue));
        }

        DetailMetaText.Text = $"创建于 {vm.Source.CreatedAt:yyyy-MM-dd HH:mm} · 更新于 {vm.Source.UpdatedAt:yyyy-MM-dd HH:mm}";

        ShowDetail(true);
    }

    private void ShowDetail(bool visible)
    {
        DetailPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // 详情页与列表/日历互斥显示，快速添加行一并隐藏
        QuickAddRow.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        if (visible)
        {
            ListPanel.Visibility = Visibility.Collapsed;
            CalendarPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            _ = _view; // 恢复由 ShowView 负责
        }
    }

    private void DetailBack_Click(object sender, RoutedEventArgs e) => CloseDetail();

    private void CloseDetail()
    {
        ShowDetail(false);
        _detailVm = null;
        ShowView(_view); // 恢复原视图可见性
    }

    private void DetailSave_Click(object sender, RoutedEventArgs e)
    {
        if (_detailVm is null) return;

        var title = DetailTitleBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(title))
        {
            DetailTitleBox.Focus(FocusState.Programmatic);
            return;
        }

        var vm = _detailVm;
        vm.Title = title;
        vm.Notes = DetailNotesBox.Text?.Trim() ?? string.Empty;
        vm.Priority = ReadDetailPriority();
        // 状态可能触发“完成→今天”，先写状态再按用户选择覆盖计划日
        vm.Status = ReadDetailStatus();
        vm.PlannedDate = DetailDatePick.Date is null
            ? null
            : DateOnly.FromDateTime(DetailDatePick.Date.Value.LocalDateTime.Date);

        ViewModel.Store.Update(vm.Source);
        vm.RefreshAll();
        ViewModel.ReloadAfterEdit();
        Refresh();
        CloseDetail();
    }

    private void DetailDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_detailVm is null) return;
        var vm = _detailVm;
        CloseDetail();
        WeakReferenceMessenger.Default.Send(new TodoDeleteMsg(vm));
    }

    private void DetailClearDate_Click(object sender, RoutedEventArgs e)
    {
        DetailDatePick.Date = null;
        DetailDatePick.PlaceholderText = "未排期";
    }

    private void SelectDetailStatus(TodoStatus status)
    {
        DetailStatusCombo.SelectedIndex = status switch
        {
            TodoStatus.InProgress => 1,
            TodoStatus.Completed => 2,
            _ => 0
        };
    }

    private void SelectDetailPriority(TodoPriority priority)
    {
        DetailPriorityCombo.SelectedIndex = priority switch
        {
            TodoPriority.Low => 0,
            TodoPriority.High => 2,
            TodoPriority.Critical => 3,
            _ => 1
        };
    }

    private TodoStatus ReadDetailStatus()
    {
        if (DetailStatusCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "InProgress" => TodoStatus.InProgress,
                "Completed" => TodoStatus.Completed,
                _ => TodoStatus.NotStarted
            };
        }
        return TodoStatus.NotStarted;
    }

    private TodoPriority ReadDetailPriority()
    {
        if (DetailPriorityCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "Low" => TodoPriority.Low,
                "High" => TodoPriority.High,
                "Critical" => TodoPriority.Critical,
                _ => TodoPriority.Medium
            };
        }
        return TodoPriority.Medium;
    }

    private static int StatusRank(TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => 0,
        TodoStatus.InProgress => 1,
        _ => 2
    };

    /// <summary>状态变更后的窗口侧刷新（分组与 ItemsSource 交给 ViewModel / XAML 绑定）。</summary>
    private void AfterStatusChanged(TodoItemVm vm)
    {
        vm.RefreshAll();
        ViewModel.ReloadAfterEdit();
        UpdateCounts();
        UpdateEmptyState();
    }

    private void StatusChip_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItemVm vm)
            return;

        _statusCollapseTimer?.Stop();

        // 同时只展开一条，切到新行时立刻收起上一条
        if (_expandedStatusVm is not null && !ReferenceEquals(_expandedStatusVm, vm))
            _expandedStatusVm.StatusExpanded = false;

        _expandedStatusVm = vm;
        vm.StatusExpanded = true;
    }

    private void StatusChip_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItemVm vm)
            return;
        if (!ReferenceEquals(_expandedStatusVm, vm))
            return;

        _statusCollapseTimer?.Stop();
        _statusCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        var target = vm;
        _statusCollapseTimer.Tick += (_, _) =>
        {
            _statusCollapseTimer?.Stop();
            if (ReferenceEquals(_expandedStatusVm, target))
            {
                target.StatusExpanded = false;
                _expandedStatusVm = null;
            }
        };
        _statusCollapseTimer.Start();
    }

    private void StatusOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TodoItemVm vm)
            return;
        if (fe.Tag is not string tag)
            return;

        vm.Status = tag switch
        {
            "InProgress" => TodoStatus.InProgress,
            "Completed" => TodoStatus.Completed,
            _ => TodoStatus.NotStarted
        };
        vm.StatusExpanded = false;
        _expandedStatusVm = null;
        AfterStatusChanged(vm);
    }

    private void UpdateCounts()
    {
        if (ListCount is null) return;
        ListCount.Text = ViewModel.ListCountText;
    }

    private void UpdateEmptyState()
    {
        if (EmptyPanel is null || this.MainListView is null) return;
        var empty = ViewModel.Items.Count == 0;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        this.MainListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MainListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var ordered = _allItems.Select(i => i.Id).ToList();
        for (var i = 0; i < _allItems.Count; i++)
            _allItems[i].SortOrder = i;
        ViewModel.Store.Reorder(ordered);
    }

    // ───────── search / filter ─────────

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text ?? string.Empty;
        if (_view != ViewMode.Calendar)
            Refresh();
    }

    // ───────── settings ─────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OpenSettings()
    {
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSettings()
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SettingsScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CloseSettings();
        e.Handled = true;
    }

    private void SettingsCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // 阻止点击冒泡到遮罩
        e.Handled = true;
    }

    private void TopMostSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;
        _settings.AlwaysOnTop = TopMostSwitch.IsOn;
        var hwnd = WindowNative.GetWindowHandle(this);
        SetTopMost(hwnd, TopMostSwitch.IsOn);
        _settings.Save();
    }

    private void HideCompletedSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;
        _settings.HideCompleted = HideCompletedSwitch.IsOn;
        _settings.Save();
        if (_view != ViewMode.Calendar)
            Refresh();
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;
        _settings.StartWithWindows = StartupSwitch.IsOn;
        _startup.SetEnabled(StartupSwitch.IsOn);
        _settings.Save();
    }

    private void AddToDay_Click(object sender, RoutedEventArgs e)
    {
        QuickDate.Date = new DateTimeOffset(_selectedDay.ToDateTime(TimeOnly.MinValue));
        QuickTitle.Focus(FocusState.Programmatic);
        if (_view == ViewMode.Calendar)
            ShowView(ViewMode.All);
        QuickDate.Date = new DateTimeOffset(_selectedDay.ToDateTime(TimeOnly.MinValue));
    }
}

internal static class NativeMethods
{
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const int SW_RESTORE = 9;
    public const int SW_HIDE = 0;
    public const int GWLP_WNDPROC = -4;

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
