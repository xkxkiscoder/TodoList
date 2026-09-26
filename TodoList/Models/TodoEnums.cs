namespace TodoList.Models;

/// <summary>待办生命周期：未开始 → 进行中 → 已完成（可循环回到未开始）。</summary>
public enum TodoStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}

public enum TodoPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public static class TodoStatusExtensions
{
    public static string ToDisplay(this TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => "未开始",
        TodoStatus.InProgress => "进行中",
        TodoStatus.Completed => "已完成",
        _ => "未开始"
    };

    /// <summary>状态胶囊循环：未开始 → 进行中 → 已完成 → 未开始。</summary>
    public static TodoStatus Next(this TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => TodoStatus.InProgress,
        TodoStatus.InProgress => TodoStatus.Completed,
        _ => TodoStatus.NotStarted
    };

    public static string ToGlyph(this TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => "○",
        TodoStatus.InProgress => "●",
        TodoStatus.Completed => "✓",
        _ => "○"
    };

    public static string ToColorHex(this TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => "#A8B3C4",
        TodoStatus.InProgress => "#5B8CFF",
        TodoStatus.Completed => "#3DD68C",
        _ => "#A8B3C4"
    };
}

public static class TodoPriorityExtensions
{
    public static string ToDisplay(this TodoPriority priority) => priority switch
    {
        TodoPriority.Low => "低",
        TodoPriority.Medium => "中",
        TodoPriority.High => "高",
        TodoPriority.Critical => "紧急",
        _ => "中"
    };

    public static string ToColorHex(this TodoPriority priority) => priority switch
    {
        TodoPriority.Low => "#9AA3B2",
        TodoPriority.Medium => "#FFB020",
        TodoPriority.High => "#FF7A45",
        TodoPriority.Critical => "#FF4D4F",
        _ => "#FFB020"
    };
}
