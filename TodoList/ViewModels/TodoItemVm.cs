using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TodoList.Models;
using Windows.UI;

namespace TodoList.ViewModels;

/// <summary>列表绑定用的可通知包装。</summary>
public sealed class TodoItemVm : INotifyPropertyChanged
{
    public TodoItem Source { get; }

    public TodoItemVm(TodoItem source) => Source = source;

    public Guid Id => Source.Id;

    public string Title
    {
        get => Source.Title;
        set { Source.Title = value; OnPropertyChanged(); }
    }

    public string Notes
    {
        get => Source.Notes;
        set
        {
            Source.Notes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotesVisibility));
        }
    }

    public TodoStatus Status
    {
        get => Source.Status;
        set
        {
            if (Source.Status == value) return;
            Source.Status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StatusGlyph));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(TitleBrush));
            OnPropertyChanged(nameof(StatusBackground));
            OnPropertyChanged(nameof(StatusBorder));
        }
    }

    public TodoPriority Priority
    {
        get => Source.Priority;
        set
        {
            if (Source.Priority == value) return;
            Source.Priority = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PriorityDisplay));
            OnPropertyChanged(nameof(PriorityBrush));
            OnPropertyChanged(nameof(PriorityChipBackground));
        }
    }

    public DateOnly? PlannedDate
    {
        get => Source.PlannedDate;
        set
        {
            if (Source.PlannedDate == value) return;
            Source.PlannedDate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlannedDateDisplay));
            OnPropertyChanged(nameof(PlannedDateBrush));
        }
    }

    public int SortOrder
    {
        get => Source.SortOrder;
        set
        {
            if (Source.SortOrder == value) return;
            Source.SortOrder = value;
            OnPropertyChanged();
        }
    }

    public string StatusDisplay => Source.StatusDisplay;
    public string StatusGlyph => Source.StatusGlyph;
    public string PriorityDisplay => Source.PriorityDisplay;
    public string PlannedDateDisplay => Source.PlannedDateDisplay;

    public Visibility NotesVisibility =>
        string.IsNullOrWhiteSpace(Source.Notes) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OverdueDotVisibility =>
        Source.IsOverdue ? Visibility.Visible : Visibility.Collapsed;

    public Brush StatusBrush => Parse(Source.Status.ToColorHex());
    public Brush PriorityBrush => Parse(Source.Priority.ToColorHex());

    /// <summary>主文/次文跟主题，已完成再降一档。</summary>
    public Brush TitleBrush => ThemeBrush(
        Source.IsCompleted ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush",
        Source.IsCompleted ? "#801B1B1F" : "#1B1B1F");

    /// <summary>已完成标题加删除线。</summary>
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

    private static Brush Parse(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

        static byte P(string v, int i) => System.Convert.ToByte(v.Substring(i, 2), 16);
        var color = Color.FromArgb(P(s, 0), P(s, 2), P(s, 4), P(s, 6));
        return new SolidColorBrush(color);
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(PriorityBrush));
        OnPropertyChanged(nameof(TitleBrush));
        OnPropertyChanged(nameof(StrikeVisibility));
        OnPropertyChanged(nameof(PlannedDateBrush));
        OnPropertyChanged(nameof(StatusBackground));
        OnPropertyChanged(nameof(StatusBorder));
        OnPropertyChanged(nameof(PriorityChipBackground));
        OnPropertyChanged(nameof(NotesVisibility));
        OnPropertyChanged(nameof(OverdueDotVisibility));
    }
}
