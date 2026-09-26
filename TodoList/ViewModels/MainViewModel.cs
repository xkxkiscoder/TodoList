using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using TodoList.Models;
using TodoList.Services;

namespace TodoList.ViewModels;

public enum AppViewMode
{
    All = 0,
    Today = 1,
    Calendar = 2
}

/// <summary>行内状态/编辑/删除消息。</summary>
public sealed record TodoStateChangedMsg(TodoItemVm Vm);

public sealed record TodoEditMsg(TodoItemVm Vm);

public sealed record TodoDeleteMsg(TodoItemVm Vm);

/// <summary>主视图模型：列表数据、筛选、日历与设置。</summary>
public partial class MainViewModel : ObservableObject,
    IRecipient<TodoStateChangedMsg>,
    IRecipient<TodoEditMsg>,
    IRecipient<TodoDeleteMsg>
{
    private readonly TodoStore _store = new();
    private readonly StartupService _startup = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Dictionary<string, bool> _groupExpanded = new();
    private TodoItem? _pendingDelete;
    private readonly DispatcherQueueTimer _snackTimer;

    /// <summary>主列表分组视图（XAML 绑定源）。</summary>
    [ObservableProperty]
    public partial CollectionViewSource GroupsView { get; set; }

    /// <summary>日历分组视图（XAML 绑定源）。</summary>
    [ObservableProperty]
    public partial CollectionViewSource DayGroupsView { get; set; }

    private void RecreateGroupViews()
    {
        GroupsView = new CollectionViewSource
        {
            Source = Groups,
            IsSourceGrouped = true,
            ItemsPath = new Microsoft.UI.Xaml.PropertyPath(nameof(StatusGroup.Items))
        };
        DayGroupsView = new CollectionViewSource
        {
            Source = DayGroups,
            IsSourceGrouped = true,
            ItemsPath = new Microsoft.UI.Xaml.PropertyPath(nameof(StatusGroup.Items))
        };
    }

    public MainViewModel()
    {
        RecreateGroupViews();
        WeakReferenceMessenger.Default.RegisterAll(this);
        LoadItems();
        SyncSettingsToState();

        _snackTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _snackTimer.Interval = TimeSpan.FromSeconds(5);
        _snackTimer.Tick += (_, _) =>
        {
            _pendingDelete = null;
            SnackVisible = false;
        };
    }

    public AppSettings Settings => _settings;
    public TodoStore Store => _store;

    public event EventHandler<TodoItemVm>? EditRequested;
    public event EventHandler? GroupsChanged;

    [ObservableProperty]
    public partial AppViewMode CurrentView { get; set; } = AppViewMode.All;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HideCompleted { get; set; }

    [ObservableProperty]
    public partial bool AlwaysOnTop { get; set; } = true;

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool SettingsOpen { get; set; }

    [ObservableProperty]
    public partial string ListTitle { get; set; } = "全部待办";

    [ObservableProperty]
    public partial string ListCountText { get; set; } = "0";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "就绪";

    [ObservableProperty]
    public partial string SnackText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SnackVisible { get; set; }

    /// <summary>撤销条可见性（供 XAML 绑定）。</summary>
    public Visibility SnackVisibility =>
        SnackVisible ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSnackVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(SnackVisibility));
        _snackTimer.Stop();
        if (value)
            _snackTimer.Start();
    }

    [ObservableProperty]
    public partial bool EmptyVisible { get; set; }

    [ObservableProperty]
    public partial int SelectedDayIndex { get; set; } = DateTime.Today.Day;

    [ObservableProperty]
    public partial string MonthTitle { get; set; } = $"{DateTime.Today.Year}年{DateTime.Today.Month}月";

    [ObservableProperty]
    public partial string DayTitle { get; set; } = $"{DateTime.Today.Month}月{DateTime.Today.Day}日";

    [ObservableProperty]
    public partial string QuickTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateOnly? QuickPlannedDate { get; set; }

    [ObservableProperty]
    public partial TodoPriority QuickPriority { get; set; } = TodoPriority.Medium;

    [ObservableProperty]
    public partial int StatusFilterIndex { get; set; }

    /// <summary>主列表（分组前的扁平数据）。</summary>
    public ObservableCollection<TodoItemVm> Items { get; } = new();

    /// <summary>日历当日列表。</summary>
    public ObservableCollection<TodoItemVm> DayItems { get; } = new();

    /// <summary>主列表分组源。</summary>
    public ObservableCollection<StatusGroup> Groups { get; } = new();

    /// <summary>日历分组源。</summary>
    public ObservableCollection<StatusGroup> DayGroups { get; } = new();

    public DateOnly VisibleMonth { get; set; } =
        new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public DateOnly SelectedDay { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    private void SyncSettingsToState()
    {
        HideCompleted = _settings.HideCompleted;
        AlwaysOnTop = _settings.AlwaysOnTop;
        StartWithWindows = _startup.IsEnabled || _settings.StartWithWindows;
    }

    private void LoadItems()
    {
        Items.Clear();
        IReadOnlyList<TodoItem> source = CurrentView == AppViewMode.Today
            ? _store.GetToday()
            : _store.GetGlobal(HideCompleted, SearchText, ReadStatusFilter());

        foreach (var item in source)
            Items.Add(Wrap(item));

        RebuildGroups();
        RecreateGroupViews();
        UpdateChrome();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    private TodoItemVm Wrap(TodoItem item)
    {
        var vm = new TodoItemVm(item);
        return vm;
    }

    private TodoStatus? ReadStatusFilter() => StatusFilterIndex switch
    {
        1 => TodoStatus.NotStarted,
        2 => TodoStatus.InProgress,
        3 => TodoStatus.Completed,
        _ => null
    };

    partial void OnSearchTextChanged(string value) => LoadItems();

    partial void OnHideCompletedChanged(bool value)
    {
        _settings.HideCompleted = value;
        _settings.Save();
        if (CurrentView != AppViewMode.Calendar)
            LoadItems();
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _settings.AlwaysOnTop = value;
        _settings.Save();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settings.StartWithWindows = value;
        _startup.SetEnabled(value);
        _settings.Save();
    }

    partial void OnSettingsOpenChanged(bool value)
    {
        // no-op hook
    }

    partial void OnStatusFilterIndexChanged(int value)
    {
        if (CurrentView != AppViewMode.Calendar)
            LoadItems();
    }

    partial void OnCurrentViewChanged(AppViewMode value)
    {
        ListTitle = value == AppViewMode.Today ? "今日计划" : "全部待办";
        _settings.LastView = (int)value;
        _settings.Save();
        if (value == AppViewMode.Calendar)
            LoadCalendarDay();
        else
            LoadItems();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SwitchToAll() => CurrentView = AppViewMode.All;

    [RelayCommand]
    private void SwitchToToday() => CurrentView = AppViewMode.Today;

    [RelayCommand]
    private void SwitchToCalendar()
    {
        CurrentView = AppViewMode.Calendar;
        LoadCalendarDay();
    }

    [RelayCommand]
    private void OpenSettings() => SettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => SettingsOpen = false;

    /// <summary>快速添加。</summary>
    [RelayCommand]
    private void AddTodo()
    {
        var title = QuickTitle?.Trim();
        if (string.IsNullOrEmpty(title)) return;

        _store.Add(title, QuickPriority, QuickPlannedDate);
        QuickTitle = string.Empty;
        QuickPlannedDate = null;
        LoadItems();
        if (CurrentView == AppViewMode.Calendar)
            LoadCalendarDay();
    }

    [RelayCommand]
    private void UndoDelete()
    {
        if (_pendingDelete is not null)
        {
            _store.Restore(_pendingDelete);
            _pendingDelete = null;
        }

        SnackVisible = false;
        LoadItems();
        if (CurrentView == AppViewMode.Calendar)
            LoadCalendarDay();
    }

    /// <summary>Esc 关闭撤销条。</summary>
    public void DismissSnack()
    {
        _pendingDelete = null;
        SnackVisible = false;
    }

    [RelayCommand]
    private void PrevMonth()
    {
        VisibleMonth = VisibleMonth.AddMonths(-1);
        MonthTitle = $"{VisibleMonth.Year}年{VisibleMonth.Month}月";
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NextMonth()
    {
        VisibleMonth = VisibleMonth.AddMonths(1);
        MonthTitle = $"{VisibleMonth.Year}年{VisibleMonth.Month}月";
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GoToday()
    {
        SelectedDay = DateOnly.FromDateTime(DateTime.Today);
        VisibleMonth = new DateOnly(SelectedDay.Year, SelectedDay.Month, 1);
        MonthTitle = $"{VisibleMonth.Year}年{VisibleMonth.Month}月";
        DayTitle = $"{SelectedDay.Month}月{SelectedDay.Day}日";
        LoadCalendarDay();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>日历点选某日。</summary>
    public void SelectDay(DateOnly date)
    {
        SelectedDay = date;
        DayTitle = $"{date.Month}月{date.Day}日";
        LoadCalendarDay();
    }

    [RelayCommand]
    private void AddToSelectedDay()
    {
        QuickPlannedDate = SelectedDay;
        CurrentView = AppViewMode.All;
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadCalendarDay()
    {
        DayItems.Clear();
        foreach (var item in _store.GetForDate(SelectedDay))
            DayItems.Add(Wrap(item));
        FillGroupCollection(DayGroups, DayItems);
        RecreateGroupViews();
        UpdateChrome();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RebuildGroups()
    {
        if (CurrentView == AppViewMode.Calendar)
            FillGroupCollection(DayGroups, DayItems);
        else
            FillGroupCollection(Groups, Items);

        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FillGroupCollection(ObservableCollection<StatusGroup> target, IEnumerable<TodoItemVm> source)
    {
        StatusGroup Make(string title, TodoStatus status)
        {
            if (!_groupExpanded.TryGetValue(title, out var isOpen))
            {
                isOpen = true;
                _groupExpanded[title] = true;
            }

            return new StatusGroup(title, source.Where(i => i.Status == status), isOpen);
        }

        target.Clear();
        foreach (var g in new[]
                 {
                     Make("未开始", TodoStatus.NotStarted),
                     Make("进行中", TodoStatus.InProgress),
                     Make("已完成", TodoStatus.Completed)
                 })
        {
            if (g.Count > 0)
                target.Add(g);
        }
    }

    private void UpdateChrome()
    {
        ListCountText = Items.Count.ToString();
        EmptyVisible = Items.Count == 0 && CurrentView != AppViewMode.Calendar;
    }

    public void SetStatusFilter(TodoStatus? filter)
    {
        // 预留：与 StatusFilter 下拉联动
        _ = filter;
    }

    // ───────── messenger ─────────

    public void Receive(TodoStateChangedMsg message)
    {
        var vm = message.Vm;
        _store.Update(vm.Source);
        vm.RefreshAll();
        RebuildGroups();

        if (CurrentView == AppViewMode.All && HideCompleted && vm.Status == TodoStatus.Completed)
        {
            Items.Remove(vm);
            RebuildGroups();
            UpdateChrome();
        }
    }

    public void Receive(TodoEditMsg message) => EditRequested?.Invoke(this, message.Vm);

    public void Receive(TodoDeleteMsg message)
    {
        var vm = message.Vm;
        _store.Remove(vm.Source.Id, out _);
        Items.Remove(vm);
        DayItems.Remove(vm);
        _pendingDelete = vm.Source;
        SnackText = $"已删除「{vm.Title}」";
        SnackVisible = true;
        RebuildGroups();
        UpdateChrome();
    }

    /// <summary>编辑保存后刷新。</summary>
    public void ReloadAfterEdit()
    {
        LoadItems();
        if (CurrentView == AppViewMode.Calendar)
            LoadCalendarDay();
    }

    public void PersistSettings()
    {
        _settings.HideCompleted = HideCompleted;
        _settings.AlwaysOnTop = AlwaysOnTop;
        _settings.StartWithWindows = StartWithWindows;
        _settings.Save();
    }

    private static int StatusRank(TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => 0,
        TodoStatus.InProgress => 1,
        _ => 2
    };

    public void SortItemsByStatus(ObservableCollection<TodoItemVm> collection)
    {
        var ordered = collection
            .OrderBy(i => StatusRank(i.Status))
            .ThenBy(i => i.SortOrder)
            .ToList();
        collection.Clear();
        foreach (var item in ordered)
            collection.Add(item);
        RebuildGroups();
    }
}
