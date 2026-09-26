using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TodoList.ViewModels;

/// <summary>按状态分组的列表分组，支持标题点击折叠。</summary>
public sealed class StatusGroup : INotifyPropertyChanged
{
    private readonly List<TodoItemVm> _all = new();
    private bool _isExpanded = true;

    public StatusGroup(string title, IEnumerable<TodoItemVm> items, bool isExpanded = true)
    {
        Title = title;
        _all.AddRange(items);
        _isExpanded = isExpanded;
        ApplyVisibility();
    }

    public string Title { get; }

    public ObservableCollection<TodoItemVm> Items { get; } = new();

    public int Count => _all.Count;

    public string HeaderText => $"{Title}  {Count}";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            ApplyVisibility();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChevronGlyph));
        }
    }

    public string ChevronGlyph => _isExpanded ? "▾" : "▸";

    public void Toggle() => IsExpanded = !IsExpanded;

    private void ApplyVisibility()
    {
        Items.Clear();
        if (!_isExpanded) return;
        foreach (var item in _all)
            Items.Add(item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
