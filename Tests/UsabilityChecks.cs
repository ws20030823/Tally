using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class UsabilityChecks
{
    public static void Run(string root)
    {
        var store=new DataStore(Path.Combine(root,"usability-"+Guid.NewGuid().ToString("N")));
        var today=DateTime.Today;
        var urgent=new TaskItem {Title="旧日期的紧急事项",List="默认清单",Priority=0,CreatedAt=today.AddDays(-20),DueDate=today.AddDays(-2),TodayDate=today.AddDays(-1),Notes="旧备注",Steps=[new(){Title="已做步骤",IsCompleted=true}]};
        var later=new TaskItem {Title="新日期的普通事项",List="默认清单",Priority=1,CreatedAt=today,DueDate=today.AddDays(5)};
        store.Save(new(){Tasks=[urgent,later]});
        using(var vm=new MainViewModel(store))
        {
            Check(vm.YesterdayCount==1&&vm.ShowYesterdayBanner,"yesterday remains discoverable without automatic carryover");
            vm.ToggleYesterdayCommand.Execute(null);
            Check(vm.VisibleCount==1&&vm.TasksView.Cast<TaskItem>().Single().Id==urgent.Id,"yesterday lookup uses planned day rather than creation day");
            vm.SelectedTask=vm.Tasks.Single(t=>t.Id==urgent.Id);vm.MoveToTodayCommand.Execute(null);
            Check(vm.YesterdayCount==0&&vm.VisibleCount==0,"moving yesterday task updates review and count");
            vm.ToggleYesterdayCommand.Execute(null);Check(vm.VisibleCount==1,"moved task appears today");
            vm.NavigateCommand.Execute(vm.Navigation.Single(n=>n.Key=="all"));vm.SortIndex=2;
            Check(vm.TasksView.Cast<TaskItem>().First().Id==urgent.Id,"earliest deadline sorts first across creation dates");
            vm.SortIndex=1;Check(vm.TasksView.Cast<TaskItem>().First().Id==urgent.Id,"highest priority sorts first across creation dates");
            vm.Search="没有匹配";Check(vm.EmptyTitle=="没有匹配的任务"&&vm.HasSearch,"search empty state explains filter");
            vm.ClearSearchCommand.Execute(null);vm.HistoryDate=today.AddYears(-3);
            Check(vm.EmptyTitle=="这一天没有记录","history empty state explains date");vm.ClearHistoryDateCommand.Execute(null);
            vm.SelectedTask=vm.Tasks.Single(t=>t.Id==urgent.Id);vm.DeleteTaskCommand.Execute(null);
            Check(!vm.RenameList(vm.CustomLists[0]," "),"reject blank list name");
            Check(vm.RenameList(vm.CustomLists[0],"项目"),"rename list");vm.UndoDeleteCommand.Execute(null);
            Check(vm.SelectedTask!.Id==urgent.Id&&vm.SelectedTask.List=="项目","undo follows renamed list");
            var task=vm.SelectedTask;var step=task.Steps.Single();
            Check(vm.RenameStep(step,"修改后的步骤")&&step.IsCompleted,"step rename preserves completion");
            Check(task.DueDate==urgent.DueDate&&task.CreatedAt==urgent.CreatedAt,"moving and renaming preserve dates");
            Check(task.PriorityLabel=="高优先级"&&task.DueLabel=="逾期 2 天","priority and overdue labels");
            task.IsCompleted=true;Check(!task.IsOverdue&&!task.IsDueToday&&!task.DueLabel.StartsWith("逾期"),"completed tasks suppress deadline warning");
            vm.NavigateCommand.Execute(vm.Navigation.Single(n=>n.Key=="planned"));
            var count=vm.Tasks.Count;vm.NewTitle="需要明确日期";vm.AddTaskCommand.Execute(null);
            Check(vm.Tasks.Count==count,"planned add requires explicit date");vm.NewTaskDate=today.AddDays(4);vm.AddTaskCommand.Execute(null);
            Check(vm.SelectedTask!.DueDate==today.AddDays(4),"planned add uses chosen date");
            Check(vm.Flush(),"save usability changes");
        }
        var reloaded=store.Load().Tasks.Single(t=>t.Id==urgent.Id);
        Check(reloaded.List=="项目"&&reloaded.Notes=="旧备注"&&reloaded.Steps.Single().Title=="修改后的步骤"&&reloaded.Steps.Single().IsCompleted,"rename survives reload without losing content");

        var legacy=new DataStore(Path.Combine(root,"legacy-ui-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(legacy.DirectoryPath);
        File.WriteAllText(legacy.FilePath,"""
        {"Version":1,"Lists":["旧清单"],"Tasks":[{"Id":"ff23af6c-04bf-4c54-bcd9-41464c9b38f6","Title":"旧版任务","List":"旧清单","Priority":0,"DueDate":"2024-01-02T00:00:00","IsCompleted":false,"Notes":"旧备注","Steps":[{"Title":"旧步骤","IsCompleted":true}]}]}
        """);
        VerifyRoundTrip(legacy,"legacy missing timestamps");

        // Only read the user's file. All load/save operations happen on an isolated copy.
        var original=Path.Combine(DataStore.DefaultDirectory,"tasks.json");
        if(File.Exists(original))
        {
            var bytes=File.ReadAllBytes(original);
            var copy=new DataStore(Path.Combine(root,"existing-file-copy-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(copy.DirectoryPath);File.WriteAllBytes(copy.FilePath,bytes);
            VerifyRoundTrip(copy,"existing user file copy");
            Check(SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))),"original user file unchanged");
        }
    }
    private static void VerifyRoundTrip(DataStore store,string label)
    {
        var before=store.Load();var snapshot=JsonSerializer.Serialize(before);
        using(var vm=new MainViewModel(store))
        {
            vm.NavigateCommand.Execute(vm.Navigation.Single(n=>n.Key=="all"));
            Check(vm.VisibleCount==before.Tasks.Count,label+": all existing tasks visible");
            Check(vm.Flush(),label+": save copied data");
        }
        Check(JsonSerializer.Serialize(store.Load())==snapshot,label+": all persisted fields survive roundtrip");
    }
    private static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("OK: "+label);}
}
