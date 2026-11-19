using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Models;

namespace TaskDesk.ViewModels;

// Dialog edits an independent draft. Closing or cancelling never changes the task.
public partial class TaskEditorViewModel : ObservableObject
{
    public IReadOnlyList<string> Lists { get; }
    public string[] PriorityOptions { get; } = ["高优先级","中优先级","低优先级"];
    [ObservableProperty] private string title="";
    [ObservableProperty] private string notes="";
    [ObservableProperty] private string list="";
    [ObservableProperty] private int priority=1;
    [ObservableProperty] private DateTime? dueDate;
    [ObservableProperty] private string stepTitle="";
    [ObservableProperty] private string feedback="";
    public ObservableCollection<TaskStep> Steps { get; }=[];
    public TaskEditorViewModel(IEnumerable<string> lists,TaskItem? task=null)
    {
        Lists=lists.ToArray();List=task?.List??Lists[0];
        if(!Lists.Contains(List))List=Lists[0];
        Title=task?.Title??"";Notes=task?.Notes??"";Priority=task?.Priority??1;DueDate=task?.DueDate;
        foreach(var step in task?.Steps??[])Steps.Add(new(){Title=step.Title,IsCompleted=step.IsCompleted});
    }
    [RelayCommand] private void AddStep()
    {
        if(string.IsNullOrWhiteSpace(StepTitle))return;
        if(StepTitle.Trim().Length>80){Feedback="子任务最多 80 个字符";return;}
        Steps.Add(new(){Title=StepTitle.Trim()});StepTitle="";Feedback="";
    }
    [RelayCommand] private void RemoveStep(TaskStep? step){if(step is not null)Steps.Remove(step);}
    public bool Validate()
    {
        if(string.IsNullOrWhiteSpace(Title)||Title.Trim().Length>100){Feedback="任务内容应为 1–100 个字符";return false;}
        if(!string.IsNullOrWhiteSpace(StepTitle)){AddStep();if(!string.IsNullOrWhiteSpace(StepTitle))return false;}
        Feedback="";return true;
    }
    public void ApplyTo(TaskItem task)
    {
        task.Title=Title.Trim();task.Notes=Notes;task.List=Lists.Contains(List)?List:Lists[0];
        task.DueDate=DueDate?.Date;task.Priority=Math.Clamp(Priority,0,2);
        task.Steps.Clear();foreach(var step in Steps)task.Steps.Add(new(){Title=step.Title,IsCompleted=step.IsCompleted});
    }
}
