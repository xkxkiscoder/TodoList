using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoList.Models;

namespace TodoList.Views;

public sealed partial class EditTodoDialog : ContentDialog
{
    public string TitleText { get; private set; } = string.Empty;
    public string NotesText { get; private set; } = string.Empty;
    public TodoStatus StatusValue { get; private set; } = TodoStatus.NotStarted;
    public TodoPriority PriorityValue { get; private set; } = TodoPriority.Medium;
    public DateOnly? PlannedDateValue { get; private set; }

    public bool IsConfirmed { get; private set; }

    public EditTodoDialog()
    {
        InitializeComponent();
    }

    public void SetValues(string title, string notes, TodoStatus status, TodoPriority priority, DateOnly? plannedDate)
    {
        TitleBox.Text = title;
        NotesBox.Text = notes;
        SelectStatus(status);
        SelectPriority(priority);
        if (plannedDate is null)
        {
            DatePick.PlaceholderText = "未排期";
            DatePick.Date = null;
        }
        else
        {
            var dt = plannedDate.Value.ToDateTime(TimeOnly.MinValue);
            DatePick.Date = new DateTimeOffset(dt);
        }
    }

    private void SelectStatus(TodoStatus status)
    {
        var index = status switch
        {
            TodoStatus.InProgress => 1,
            TodoStatus.Completed => 2,
            _ => 0
        };
        StatusCombo.SelectedIndex = index;
    }

    private void SelectPriority(TodoPriority priority)
    {
        var index = priority switch
        {
            TodoPriority.Low => 0,
            TodoPriority.High => 2,
            TodoPriority.Critical => 3,
            _ => 1
        };
        PriorityCombo.SelectedIndex = index;
    }

    private static TodoStatus ReadStatus(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
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

    private static TodoPriority ReadPriority(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
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

    private void ClearDate_Click(object sender, RoutedEventArgs e)
    {
        DatePick.Date = null;
        DatePick.PlaceholderText = "未排期";
        PlannedDateValue = null;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var title = TitleBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(title))
        {
            args.Cancel = true;
            TitleBox.Focus(FocusState.Programmatic);
            return;
        }

        TitleText = title;
        NotesText = NotesBox.Text?.Trim() ?? string.Empty;
        StatusValue = ReadStatus(StatusCombo);
        PriorityValue = ReadPriority(PriorityCombo);
        PlannedDateValue = DatePick.Date is null
            ? null
            : DateOnly.FromDateTime(DatePick.Date.Value.LocalDateTime.Date);
        IsConfirmed = true;
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        IsConfirmed = false;
    }
}
