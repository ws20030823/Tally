using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Threading;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class DateHistoryChecks
{
    public static void Run(string root)
    {
        var store=new DataStore(Path.Combine(root,"date-history-"+Guid.NewGuid().ToString("N")));
        var today=DateTime.Today;
        var recent=new TaskItem{Title="今天记录",List="默认清单",CreatedAt=today.AddHours(8)};
        var previous=new TaskItem{Title="昨天记录",List="默认清单",CreatedAt=today.AddDays(-1).AddHours(9)};
        var older=new TaskItem{Title="更早记录",List="默认清单",CreatedAt=today.AddDays(-5).AddHours(10),IsCompleted=true,CompletedAt=today.AddDays(-2).AddHours(16)};
        var unknown=JsonSerializer.Deserialize<TaskItem>("{\"Title\":\"旧任务\",\"List\":\"默认清单\",\"IsCompleted\":true}")!;
        Check(unknown.CreatedAt is null&&unknown.CompletedAt is null,"legacy missing dates remain unknown on deserialization");
        store.Save(new(){Tasks=[recent,previous,older,unknown]});
        using(var vm=new MainViewModel(store))
        {
            vm.NavigateCommand.Execute(vm.Navigation.Single(n=>n.Key=="all"));
            var groups=vm.TasksView.Groups!.Cast<CollectionViewGroup>().ToArray();
            Check(groups.Select(g=>((TaskDateGroup)g.Name).Label).SequenceEqual(new[]{"今天","昨天",$"更早 · {today.AddDays(-5):yyyy年M月d日}","创建日期未记录"}),"creation groups are newest first, unknown last");
            ((TaskDateGroup)groups[2].Name).IsExpanded=false;
            vm.SortIndex=1;
            Check(vm.TasksView.GroupDescriptions.Count==0,"priority sorting spans creation dates");
            vm.SortIndex=0;
            Check(!((TaskDateGroup)vm.TasksView.Groups!.Cast<CollectionViewGroup>().ElementAt(2).Name).IsExpanded,"collapsed day survives refresh");
            vm.HistoryDate=today.AddDays(-1);
            Check(vm.VisibleCount==1&&vm.TasksView.Cast<TaskItem>().Single().Title=="昨天记录","filter exact creation date");
            vm.Search="不匹配";Check(vm.VisibleCount==0,"date filter composes with text search");vm.Search="";
            vm.ClearHistoryDateCommand.Execute(null);Check(vm.VisibleCount==4,"clear date filter");
            var task=vm.Tasks.Single(t=>t.Title=="昨天记录");
            var creation=task.CreatedAt;var before=DateTime.Now;task.IsCompleted=true;
            Check(task.CompletedAt>=before&&task.CompletedAt<=DateTime.Now&&task.CreatedAt==creation,"completing records actual time without changing creation");
            var completion=task.CompletedAt;vm.SelectedTask=task;vm.EditNotes="普通编辑";Check(vm.Flush(),"save dated tasks");
            Check(task.CompletedAt==completion,"ordinary editing does not change completion time");
            vm.NavigateCommand.Execute(vm.Navigation.Single(n=>n.Key=="done"));
            Check(vm.DateModeIndex==1&&vm.HistoryDate is null,"completed view defaults to completion date");
            var completedGroups=vm.TasksView.Groups!.Cast<CollectionViewGroup>().Select(g=>((TaskDateGroup)g.Name).Label).ToArray();
            Check(completedGroups.SequenceEqual(new[]{"今天",$"更早 · {today.AddDays(-2):yyyy年M月d日}","完成日期未记录"}),"completion groups use actual completion day and retain unknown history");
            vm.HistoryDate=today;Check(vm.VisibleCount==1&&vm.TasksView.Cast<TaskItem>().Single()==task,"filter exact completion date");
            vm.DateModeIndex=0;vm.HistoryDate=today.AddDays(-1);Check(vm.VisibleCount==1,"completed tasks can be found by creation date too");
            task.IsCompleted=false;Check(task.CompletedAt is null,"restoring task clears its current completion timestamp");
            vm.ClearHistoryDateCommand.Execute(null);task.IsCompleted=true;Check(task.CompletedAt>=completion,"completing again records a new completion time");
            vm.NewTitle="新记录";vm.AddTaskCommand.Execute(null);
            Check(vm.SelectedTask!.CreatedAt.HasValue&&vm.SelectedTask.CreatedAt.Value.Date==today&&vm.DateModeIndex==0&&!vm.HasDateFilter,"new task records creation time and remains visible");
            Check(vm.Flush(),"persist new creation and completion timestamps");
        }
        var loaded=new DataStore(store.DirectoryPath).Load();
        Check(loaded.Tasks.Single(t=>t.Title=="昨天记录").CompletedAt.HasValue&&loaded.Tasks.Single(t=>t.Title=="今天记录").CreatedAt==recent.CreatedAt,"both timestamps survive reload");
        Check(loaded.Tasks.Single(t=>t.Title=="旧任务").CompletedAt is null,"saving does not manufacture old completion dates");
        Console.WriteLine("PASS: date grouping, exact-day lookup, completion tracking, backward compatibility and persistence.");
    }
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("OK: "+name);}
}
