using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskDesk.Models;

namespace TaskDesk.ViewModels;

public partial class TaskDateGroup(DateTime? date, bool completion) : ObservableObject
{
    public DateTime? Date { get; } = date;
    [ObservableProperty] private bool isExpanded = true;
    public string Label => Date is not { } value ? completion ? "完成日期未记录" : "创建日期未记录" :
        value == DateTime.Today ? "今天" : value == DateTime.Today.AddDays(-1) ? "昨天" :
        value < DateTime.Today ? $"更早 · {value:yyyy年M月d日}" : value.ToString("yyyy年M月d日");
    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

public sealed class TaskDateGroupDescription(Func<bool> useCompletion) : GroupDescription
{
    private readonly Dictionary<(DateTime?, bool),TaskDateGroup> groups = [];
    public static DateTime? GetDate(TaskItem item, bool completion) => TaskItem.LocalTime(completion ? item.CompletedAt : item.CreatedAt)?.Date;
    public override object GroupNameFromItem(object item, int level, CultureInfo culture)
    {
        var completion = useCompletion();
        var key = (GetDate((TaskItem)item,completion),completion);
        if (!groups.TryGetValue(key,out var group)) groups[key] = group = new TaskDateGroup(key.Item1,completion);
        return group;
    }
    public void RefreshLabels() { foreach(var group in groups.Values) group.RefreshLabel(); }
}
