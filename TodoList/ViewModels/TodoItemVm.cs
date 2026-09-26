using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TodoList.Models;
using Windows.UI;

namespace TodoList.ViewModels;

/// <summary>列表绑定用的可通知包装（CommunityToolkit.Mvvm + partial 属性 + RelayCommand）。</summary>
public partial class TodoItemVm : ObservableObject
{
    public TodoItem Source { get; }

    public TodoItemVm(TodoItem source)
    {
        Source = source;
        Title = source.Title;
        Notes = source.Notes;
        Priority = source.Priority;
        PlannedDate = source.PlannedDate;
        SortOrder = source.SortOrder;
        Status = source.Status;
    }

    public Guid Id => Source.Id;

    [RelayCommand]
    private void ToggleStatus()
    {
        Status = Status.Next();
        StatusExpanded = false;
        WeakReferenceMessenger.Default.Send(new TodoStateChangedMsg(this));
    }

    [RelayCommand]
    private void SetNotStarted()
    {
        Status = TodoStatus.NotStarted;
        StatusExpanded = false;
        WeakReferenceMessenger.Default.Send(new TodoStateChangedMsg(this));
    }

    [RelayCommand]
    private void SetInProgress()
    {
        Status = TodoStatus.InProgress;
        StatusExpanded = false;
        WeakReferenceMessenger.Default.Send(new TodoStateChangedMsg(this));
    }

    [RelayCommand]
    private void SetCompleted()
    {
        Status = TodoStatus.Completed;
        StatusExpanded = false;
        WeakReferenceMessenger.Default.Send(new TodoStateChangedMsg(this));
    }

    [RelayCommand]
    private void OpenEditor() =>
        WeakReferenceMessenger.Default.Send(new TodoEditMsg(this));

    [RelayCommand]
    private void Remove() =>
        WeakReferenceMessenger.Default.Send(new TodoDeleteMsg(this));

    [RelayCommand]
    private void ExpandStatus() => StatusExpanded = true;

    [RelayCommand]
    private void CollapseStatus() => StatusExpanded = false;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; }

    [ObservableProperty]
    public partial TodoStatus Status { get; set; }

    [ObservableProperty]
    public partial TodoPriority Priority { get; set; }

    [ObservableProperty]
    public partial DateOnly? PlannedDate { get; set; }

    [ObservableProperty]
    public partial int SortOrder { get; set; }

    /// <summary>左侧状态胶囊是否展开。</summary>
    [ObservableProperty]
    public partial bool StatusExpanded { get; set; }

    partial void OnTitleChanged(string value) => Source.Title = value;

    partial void OnNotesChanged(string value)
    {
        Source.Notes = value;
        OnPropertyChanged(nameof(NotesVisibility));
    }

    partial void OnStatusChanged(TodoStatus value)
    {
        // 标记完成时自动归属当天
        Source.ApplyStatus(value);
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(TitleBrush));
        OnPropertyChanged(nameof(StrikeVisibility));
        OnPropertyChanged(nameof(StatusBackground));
        OnPropertyChanged(nameof(StatusBorder));
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(NotStartedDotBrush));
        OnPropertyChanged(nameof(InProgressDotBrush));
        OnPropertyChanged(nameof(CompletedDotBrush));
        OnPropertyChanged(nameof(NotStartedCapsuleBg));
        OnPropertyChanged(nameof(InProgressCapsuleBg));
        OnPropertyChanged(nameof(CompletedCapsuleBg));
        OnPropertyChanged(nameof(StatusChipVis));
        OnPropertyChanged(nameof(StatusOptionsVis));
        OnPropertyChanged(nameof(PlannedDate));
        OnPropertyChanged(nameof(PlannedDateDisplay));
        OnPropertyChanged(nameof(PlannedDateBrush));
    }

    partial void OnPriorityChanged(TodoPriority value)
    {
        Source.Priority = value;
        OnPropertyChanged(nameof(PriorityDisplay));
        OnPropertyChanged(nameof(PriorityBrush));
        OnPropertyChanged(nameof(PriorityChipBackground));
    }

    partial void OnPlannedDateChanged(DateOnly? value)
    {
        Source.PlannedDate = value;
        OnPropertyChanged(nameof(PlannedDateDisplay));
        OnPropertyChanged(nameof(PlannedDateBrush));
    }

    partial void OnSortOrderChanged(int value) => Source.SortOrder = value;

    partial void OnStatusExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusChipVis));
        OnPropertyChanged(nameof(StatusOptionsVis));
    }

    public string StatusDisplay => Source.StatusDisplay;
    public string StatusGlyph => Source.StatusGlyph;
    public string PriorityDisplay => Source.PriorityDisplay;
    public string PlannedDateDisplay => Source.PlannedDateDisplay;

    public Visibility NotesVisibility =>
        string.IsNullOrWhiteSpace(Source.Notes) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OverdueDotVisibility =>
        Source.IsOverdue ? Visibility.Visible : Visibility.Collapsed;

    public Brush StatusDotBrush => Parse(Source.Status.ToColorHex());
    public Brush PriorityBrush => Parse(Source.Priority.ToColorHex());

    public Brush NotStartedDotBrush => Parse("#8E8E93");
    public Brush InProgressDotBrush => Parse("#2F5FD0");
    public Brush CompletedDotBrush => Parse("#3DCF8E");

    public Brush NotStartedCapsuleBg =>
        Status == TodoStatus.NotStarted ? Parse("#294C6FFF") : Parse("#CCEEF2F8");

    public Brush InProgressCapsuleBg =>
        Status == TodoStatus.InProgress ? Parse("#294C6FFF") : Parse("#CCEEF2F8");

    public Brush CompletedCapsuleBg =>
        Status == TodoStatus.Completed ? Parse("#294C6FFF") : Parse("#CCEEF2F8");

    public Brush StatusChipBg => Parse("#CCEEF2F8");
    public Brush StatusChipStroke => Parse("#33000000");

    public Brush TitleBrush => ThemeBrush(
        Source.IsCompleted ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush",
        Source.IsCompleted ? "#801B1B1F" : "#1B1B1F");

    public Visibility StrikeVisibility =>
        Source.IsCompleted ? Visibility.Visible : Visibility.Collapsed;

    public Brush PlannedDateBrush => Source.IsOverdue
        ? Parse("#C42B1C")
        : ThemeBrush("TextFillColorSecondaryBrush", "#A61B1B1F");

    public Brush StatusBackground => Source.Status switch
    {
        TodoStatus.NotStarted => Parse("#14A8B3C4"),
        TodoStatus.InProgress => Parse("#295B8CFF"),
        _ => Parse("#243DD68C")
    };

    public Brush StatusBorder => Source.Status switch
    {
        TodoStatus.NotStarted => Parse("#38A8B3C4"),
        TodoStatus.InProgress => Parse("#525B8CFF"),
        _ => Parse("#4D3DD68C")
    };

    public Brush PriorityChipBackground => Parse(Source.Priority switch
    {
        TodoPriority.Low => "#149AA3B2",
        TodoPriority.Medium => "#14FFB020",
        TodoPriority.High => "#1CFF7A45",
        _ => "#1CFF4D4F"
    });

    public Visibility StatusChipVis =>
        StatusExpanded ? Visibility.Collapsed : Visibility.Visible;

    public Visibility StatusOptionsVis =>
        StatusExpanded ? Visibility.Visible : Visibility.Collapsed;

    private static Brush Parse(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

        static byte P(string v, int i) => Convert.ToByte(v.Substring(i, 2), 16);
        return new SolidColorBrush(Color.FromArgb(P(s, 0), P(s, 2), P(s, 4), P(s, 6)));
    }

    private static Brush ThemeBrush(string key, string fallbackHex)
    {
        try
        {
            var resources = Application.Current?.Resources;
            if (resources is not null && resources.ContainsKey(key) && resources[key] is Brush brush)
                return brush;
        }
        catch
        {
            // fall through
        }

        return Parse(fallbackHex);
    }

    public void RefreshAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(PlannedDate));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(PriorityDisplay));
        OnPropertyChanged(nameof(PlannedDateDisplay));
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(PriorityBrush));
        OnPropertyChanged(nameof(TitleBrush));
        OnPropertyChanged(nameof(StrikeVisibility));
        OnPropertyChanged(nameof(PlannedDateBrush));
        OnPropertyChanged(nameof(StatusBackground));
        OnPropertyChanged(nameof(StatusBorder));
        OnPropertyChanged(nameof(PriorityChipBackground));
        OnPropertyChanged(nameof(NotesVisibility));
        OnPropertyChanged(nameof(OverdueDotVisibility));
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(NotStartedDotBrush));
        OnPropertyChanged(nameof(InProgressDotBrush));
        OnPropertyChanged(nameof(CompletedDotBrush));
        OnPropertyChanged(nameof(NotStartedCapsuleBg));
        OnPropertyChanged(nameof(InProgressCapsuleBg));
        OnPropertyChanged(nameof(CompletedCapsuleBg));
        OnPropertyChanged(nameof(StatusChipBg));
        OnPropertyChanged(nameof(StatusChipStroke));
        OnPropertyChanged(nameof(StatusChipVis));
        OnPropertyChanged(nameof(StatusOptionsVis));
    }
}
