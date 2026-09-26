using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TodoList.ViewModels;

/// <summary>按状态分组的列表分组，支持标题点击折叠。</summary>
public partial class StatusGroup : ObservableObject
{
    private readonly List<TodoItemVm> _all = new();

    public StatusGroup(string title, IEnumerable<TodoItemVm> items, bool isExpanded = true)
    {
        Title = title;
        _all.AddRange(items);
        IsExpanded = isExpanded;
    }

    public string Title { get; }

    public ObservableCollection<TodoItemVm> Items { get; } = new();

    public int Count => _all.Count;

    public string HeaderText => $"{Title}  {Count}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronGlyph))]
    public partial bool IsExpanded { get; set; }

    public string ChevronGlyph => IsExpanded ? "▾" : "▸";

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value) => ApplyVisibility();

    private void ApplyVisibility()
    {
        Items.Clear();
        if (!IsExpanded) return;
        foreach (var item in _all)
            Items.Add(item);
    }
}
