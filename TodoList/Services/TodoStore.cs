using System.Text.Json;
using System.Text.Json.Serialization;
using TodoList.Models;

namespace TodoList.Services;

/// <summary>
/// 本地 JSON 持久化。默认写到 %LOCALAPPDATA%\TodoList\todos.json。
/// 排序规则：默认添加顺序（SortOrder）；拖拽会改写 SortOrder。
/// </summary>
public sealed class TodoStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly object _gate = new();

    public List<TodoItem> Items { get; private set; } = new();

    public event EventHandler? Changed;

    public string FilePath => _filePath;

    public TodoStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TodoList");
        Directory.CreateDirectory(dir);
        _filePath = filePath ?? Path.Combine(dir, "todos.json");
        Load();
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                Items = new List<TodoItem>();
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                Items = JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions) ?? new List<TodoItem>();
            }
            catch
            {
                Items = new List<TodoItem>();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(Items, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
    }

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>追加到末尾（添加顺序）。</summary>
    public TodoItem Add(string title, TodoPriority priority = TodoPriority.Medium,
        DateOnly? plannedDate = null, string notes = "")
    {
        var nextSort = Items.Count == 0 ? 0 : Items.Max(i => i.SortOrder) + 1;
        var item = new TodoItem
        {
            Title = title.Trim(),
            Priority = priority,
            PlannedDate = plannedDate,
            Notes = notes.Trim(),
            SortOrder = nextSort
        };
        Items.Add(item);
        NotifyChanged();
        return item;
    }

    public void Update(TodoItem item)
    {
        item.UpdatedAt = DateTimeOffset.Now;
        NotifyChanged();
    }

    public void Remove(Guid id) => Remove(id, out _);

    public bool Remove(Guid id, out TodoItem? removed)
    {
        removed = Items.FirstOrDefault(i => i.Id == id);
        if (removed is null) return false;
        Items.RemoveAll(i => i.Id == id);
        NotifyChanged();
        return true;
    }

    /// <summary>撤销删除：插回原位置。</summary>
    public void Restore(TodoItem item)
    {
        if (Items.Any(i => i.Id == item.Id)) return;
        var index = Items.Count(i => i.SortOrder < item.SortOrder);
        Items.Insert(Math.Clamp(index, 0, Items.Count), item);
        NotifyChanged();
    }

    /// <summary>状态胶囊循环。</summary>
    public void CycleStatus(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        item.ApplyStatus(item.Status.Next());
        NotifyChanged();
    }

    public void SetStatus(Guid id, TodoStatus status)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        item.ApplyStatus(status);
        NotifyChanged();
    }

    public void SetPriority(Guid id, TodoPriority priority)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        item.Priority = priority;
        item.UpdatedAt = DateTimeOffset.Now;
        NotifyChanged();
    }

    public void SetPlannedDate(Guid id, DateOnly? plannedDate)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        item.PlannedDate = plannedDate;
        item.UpdatedAt = DateTimeOffset.Now;
        NotifyChanged();
    }

    /// <summary>按 ID 列表重排（拖拽落盘）。</summary>
    public void Reorder(IReadOnlyList<Guid> orderedIds)
    {
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var item = Items.FirstOrDefault(x => x.Id == orderedIds[i]);
            if (item is not null)
                item.SortOrder = i;
        }
        NotifyChanged();
    }

    private static int StatusRank(TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => 0,
        TodoStatus.InProgress => 1,
        _ => 2
    };

    /// <summary>全局清单：未开始 → 进行中 → 已完成，组内按添加顺序。</summary>
    public IReadOnlyList<TodoItem> GetGlobal(bool hideCompleted = false, string? search = null, TodoStatus? statusFilter = null)
    {
        IEnumerable<TodoItem> query = Items
            .OrderBy(i => StatusRank(i.Status))
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAt);

        if (hideCompleted)
            query = query.Where(i => i.Status != TodoStatus.Completed);

        if (statusFilter is not null)
            query = query.Where(i => i.Status == statusFilter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i =>
                i.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.Notes.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    /// <summary>今日计划：计划日=今天 ∪ 逾期；未开始 → 进行中 → 已完成。</summary>
    public IReadOnlyList<TodoItem> GetToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return Items
            .Where(i => i.PlannedDate == today || i.IsOverdue)
            .OrderBy(i => StatusRank(i.Status))
            .ThenBy(i => i.IsOverdue ? 0 : 1)
            .ThenBy(i => i.SortOrder)
            .ToList();
    }

    public IReadOnlyList<TodoItem> GetForDate(DateOnly date)
    {
        return Items
            .Where(i => i.PlannedDate == date)
            .OrderBy(i => StatusRank(i.Status))
            .ThenBy(i => i.SortOrder)
            .ToList();
    }

    public IReadOnlyList<DateOnly> GetDatesWithTodos(int year, int month)
    {
        return Items
            .Where(i => i.PlannedDate is not null
                && i.PlannedDate.Value.Year == year
                && i.PlannedDate.Value.Month == month)
            .Select(i => i.PlannedDate!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }

    public int NotStartedCount => Items.Count(i => i.Status == TodoStatus.NotStarted);
    public int InProgressCount => Items.Count(i => i.Status == TodoStatus.InProgress);
    public int CompletedCount => Items.Count(i => i.Status == TodoStatus.Completed);
    public int OverdueCount => Items.Count(i => i.IsOverdue);
}
