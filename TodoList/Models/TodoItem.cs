using System.Text.Json.Serialization;

namespace TodoList.Models;

public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>标题（必填）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>备注，可空。</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>三态状态。</summary>
    public TodoStatus Status { get; set; } = TodoStatus.NotStarted;

    public TodoPriority Priority { get; set; } = TodoPriority.Medium;

    /// <summary>单一计划日；空表示未排期。</summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>拖拽顺序；越小越靠前。同创建时间下按添加顺序递增。</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool IsCompleted => Status == TodoStatus.Completed;

    [JsonIgnore]
    public bool IsOverdue =>
        PlannedDate is not null
        && PlannedDate.Value < DateOnly.FromDateTime(DateTime.Today)
        && Status != TodoStatus.Completed;

    [JsonIgnore]
    public bool IsDueToday => PlannedDate == DateOnly.FromDateTime(DateTime.Today);

    /// <summary>今日计划是否收录：计划日=今天，或逾期未完成。</summary>
    [JsonIgnore]
    public bool BelongsToToday => IsDueToday || IsOverdue;

    /// <summary>切换状态；仅当状态真正变化且目标为已完成时归属当天。
    /// 已完成 → 已完成（如加载时回放）不得改写计划日，否则跨天后历史排期会被刷成今天。</summary>
    public void ApplyStatus(TodoStatus status)
    {
        if (Status == status)
            return;
        Status = status;
        if (status == TodoStatus.Completed)
            PlannedDate = DateOnly.FromDateTime(DateTime.Today);
        UpdatedAt = DateTimeOffset.Now;
    }

    [JsonIgnore]
    public string StatusDisplay => Status.ToDisplay();

    [JsonIgnore]
    public string StatusGlyph => Status.ToGlyph();

    [JsonIgnore]
    public string StatusColor => Status.ToColorHex();

    [JsonIgnore]
    public string PriorityDisplay => Priority.ToDisplay();

    [JsonIgnore]
    public string PriorityColor => Priority.ToColorHex();

    [JsonIgnore]
    public string PlannedDateDisplay => PlannedDate is null
        ? "未排期"
        : PlannedDate.Value.ToDateTime(TimeOnly.MinValue).ToString("M月d日 ddd");

    [JsonIgnore]
    public string PlannedDateShort => PlannedDate is null ? "—" : PlannedDate.Value.ToString("M/d");

    /// <summary>逾期时计划日显示红色。</summary>
    [JsonIgnore]
    public string PlannedDateColor => IsOverdue ? "#FF4D4F" : "#B3FFFFFF";

    public TodoItem Clone() => new()
    {
        Id = Id,
        Title = Title,
        Notes = Notes,
        Status = Status,
        Priority = Priority,
        PlannedDate = PlannedDate,
        SortOrder = SortOrder,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };

    public void CopyFrom(TodoItem other)
    {
        Title = other.Title;
        Notes = other.Notes;
        Status = other.Status;
        Priority = other.Priority;
        PlannedDate = other.PlannedDate;
        SortOrder = other.SortOrder;
        UpdatedAt = DateTimeOffset.Now;
    }
}
