using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskDesk.Models;

public partial class TaskStep : ObservableObject
{
    [ObservableProperty] private string title = "";
    [ObservableProperty] private bool isCompleted;
}

public partial class TaskItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Missing legacy timestamps stay unknown; loading a file must never invent dates.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CreatedTimeLabel)), NotifyPropertyChangedFor(nameof(Metadata))] private DateTime? createdAt;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CompletedTimeLabel)), NotifyPropertyChangedFor(nameof(Metadata))] private DateTime? completedAt;
    [ObservableProperty] private string title = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Metadata))] private string list = "工作项目";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Metadata)), NotifyPropertyChangedFor(nameof(IsOverdue))] private DateTime? dueDate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Metadata))] private int priority = 1;
    [ObservableProperty] private bool isImportant;
    [ObservableProperty] private DateTime? todayDate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOverdue)), NotifyPropertyChangedFor(nameof(Metadata))] private bool isCompleted;
    private string notes = "";
    // Optional notes from older or externally edited files may be null.
    [AllowNull]
    public string Notes
    {
        get => notes;
        set => SetProperty(ref notes, value ?? "");
    }
    public ObservableCollection<TaskStep> Steps { get; set; } = [];

    [JsonIgnore] public bool IsOverdue => !IsCompleted && DueDate?.Date < DateTime.Today;
    [JsonIgnore] public string PriorityLabel => Priority switch { 0 => "高优先级", 1 => "中优先级", _ => "低优先级" };
    [JsonIgnore] public string DueLabel => DueDate is not { } date ? "" : IsOverdue ? $"逾期 {(DateTime.Today-date.Date).Days} 天" :
        date.Date == DateTime.Today ? "今天截止" : date.Date == DateTime.Today.AddDays(1) ? "明天截止" : $"{ShortDate(date)}截止";
    [JsonIgnore] public bool IsDueToday => !IsCompleted && DueDate?.Date == DateTime.Today;
    [JsonIgnore] public bool HasDueDate => DueDate.HasValue;
    [JsonIgnore] public string ListSummary => Steps.Count > 0 ? $"{List} · 步骤 {StepsSummary}" : List;
    [JsonIgnore] public string StepsSummary => $"{Steps.Count(s => s.IsCompleted)} / {Steps.Count}";
    [JsonIgnore] public string CreatedTimeLabel => LocalTime(CreatedAt) is { } date ? $"创建于 {date:yyyy-MM-dd HH:mm}" : "创建时间未记录";
    [JsonIgnore] public string CompletedTimeLabel => LocalTime(CompletedAt) is { } date ? $"完成于 {date:yyyy-MM-dd HH:mm}" : "完成时间未记录";
    public static DateTime? LocalTime(DateTime? value) => value is { Year: > 1 } date ? date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date : null;
    [JsonIgnore] public string Metadata
    {
        get
        {
            var parts = new List<string> { List };
            if (LocalTime(CreatedAt) is { } created) parts.Add($"创建于 {ShortDate(created)}");
            if (IsCompleted) parts.Add(LocalTime(CompletedAt) is { } completed ? $"完成于 {ShortDate(completed)}" : "完成日期未记录");
            if (DueDate is { } date)
                parts.Add(IsOverdue ? $"已逾期 · {date:M月d日}" : date.Date == DateTime.Today ? "今天截止" : $"{date:M月d日}截止");
            if (Priority == 0) parts.Add("高优先级");
            if (Steps.Count > 0) parts.Add($"步骤 {StepsSummary}");
            return string.Join("   ·   ", parts);
        }
    }
    private static string ShortDate(DateTime date) => date.ToString(date.Year == DateTime.Today.Year ? "M月d日" : "yyyy年M月d日");

    private readonly HashSet<TaskStep> attached = [];
    public void AttachSteps()
    {
        Steps.CollectionChanged -= StepsChanged;
        Steps.CollectionChanged += StepsChanged;
        SyncSteps();
    }
    private void StepsChanged(object? sender, NotifyCollectionChangedEventArgs e) { SyncSteps(); NotifySteps(); }
    private void SyncSteps()
    {
        foreach (var removed in attached.Where(x => !Steps.Contains(x)).ToArray())
        { removed.PropertyChanged -= StepChanged; attached.Remove(removed); }
        foreach (var step in Steps.Where(x => !attached.Contains(x)))
        { step.PropertyChanged += StepChanged; attached.Add(step); }
    }
    private void StepChanged(object? sender, PropertyChangedEventArgs e) => NotifySteps();
    private void NotifySteps() { OnPropertyChanged(nameof(StepsSummary)); OnPropertyChanged(nameof(Metadata)); OnPropertyChanged(nameof(ListSummary)); }
    partial void OnPriorityChanged(int value) => OnPropertyChanged(nameof(PriorityLabel));
    partial void OnListChanged(string value) => OnPropertyChanged(nameof(ListSummary));
    partial void OnDueDateChanged(DateTime? value) => RefreshDateLabels();
    partial void OnIsCompletedChanged(bool value) => RefreshDateLabels();
    public void RefreshDateLabels() { foreach(var name in new[]{nameof(Metadata),nameof(IsOverdue),nameof(DueLabel),nameof(IsDueToday),nameof(HasDueDate)}) OnPropertyChanged(name); }
}

public class AppData
{
    // Defaults support a fresh workspace; persisted files must explicitly contain these fields.
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public List<string> Lists { get; set; } = ["默认清单"];
    [JsonRequired] public List<TaskItem> Tasks { get; set; } = [];
}
