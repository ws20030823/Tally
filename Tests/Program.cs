using System.IO;
using System.Windows.Threading;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "--theme-checks") return ThemeChecks.Run(args.Skip(1).FirstOrDefault() ?? "Tests/Artifacts/Themes");
            if (args.FirstOrDefault() == "--mini-checks") return MiniChecks.Run(args.Skip(1).FirstOrDefault() ?? "Tests/Artifacts/Mini");
            if (args.FirstOrDefault() == "--scroll-checks") return ScrollChecks.Run(args.Skip(1).FirstOrDefault() ?? "Tests/Artifacts/ScrollChecks");
            if (args.FirstOrDefault() == "--ui-audit") return UiAuditChecks.Run(args.Skip(1).FirstOrDefault() ?? "Tests/Artifacts/UIAudit",args.Skip(2).FirstOrDefault());
            if (args.FirstOrDefault() == "--mutex-probe") return AuditRegressionChecks.ProbeMutex(args[1]);
            if (args.FirstOrDefault() == "--audit") return EngineeringAudit.Run(args.Skip(1).FirstOrDefault() ?? "Tests/Artifacts/EngineeringAudit");
            var root=Path.GetFullPath(args.FirstOrDefault()??"test-data");
            Directory.CreateDirectory(root);
            var folder=Path.Combine(root,Guid.NewGuid().ToString("N"));
            var store=new DataStore(folder);
            PersistenceChecks.Run(root);
            AuditRegressionChecks.Run(root);
            DateHistoryChecks.Run(root);
            UsabilityChecks.Run(root);
            store.Save(TestData.Create());
            using(var vm=new MainViewModel(store))
            {
                Check(vm.VisibleCount==5,"initial today filter");
                vm.NewTitle="   ";vm.AddTaskCommand.Execute(null);Check(vm.Tasks.Count==6,"reject empty title");
                vm.NewTitle="新增测试";vm.AddTaskCommand.Execute(null);var task=vm.SelectedTask!;
                Check(task.TodayDate==DateTime.Today&&task.DueDate is null,"today does not invent a deadline");
                vm.EditTitle="已修改";vm.EditNotes="备注搜索专用词";vm.EditPriority=0;vm.EditDate=DateTime.Today.AddDays(3);
                vm.NewStepTitle="完成验证";vm.AddStepCommand.Execute(null);task.Steps[0].IsCompleted=true;
                Check(vm.Flush(),"persist edits");Check(task.Title=="已修改"&&task.StepsSummary=="1 / 1","edit and steps");
                vm.Search="专用词";Check(vm.VisibleCount==1,"search notes");
                vm.Search="";vm.ToggleImportantCommand.Execute(task);Check(task.IsImportant,"star");
                vm.DeleteTaskCommand.Execute(null);Check(!vm.Tasks.Contains(task)&&vm.CanUndo,"delete");
                vm.UndoDeleteCommand.Execute(null);Check(vm.Tasks.Contains(task)&&vm.SelectedTask==task&&!vm.CanUndo,"undo restores selection and task");
                vm.NewListName="测试清单";vm.AddListCommand.Execute(null);vm.NewTitle="清单内任务";vm.AddTaskCommand.Execute(null);
                Check(vm.SelectedTask!.List=="测试清单"&&vm.VisibleCount==1,"create in custom list");
                vm.NewListName="测试清单";vm.AddListCommand.Execute(null);Check(vm.Lists.Count==4,"duplicate list");
                vm.NavigateCommand.Execute(vm.Navigation.First(n=>n.Key=="done"));vm.NewTitle="从完成视图创建";vm.AddTaskCommand.Execute(null);
                Check(vm.CurrentView=="all"&&!vm.SelectedTask!.IsCompleted,"new task visible from done view");
                vm.SelectedTask!.IsCompleted=true;Check(vm.Flush(),"persist lists and task status: "+vm.Status);
                vm.NavigateCommand.Execute(vm.Navigation.First(n=>n.Key=="done"));
                Check(vm.TasksView.Cast<TaskItem>().All(t=>t.IsCompleted),"completed view");
                vm.NavigateCommand.Execute(vm.Navigation.First(n=>n.Key=="planned"));vm.SortIndex=2;
                var pending=vm.TasksView.Cast<TaskItem>().Where(t=>!t.IsCompleted).ToArray();
                Check(pending.GroupBy(t=>TaskDateGroupDescription.GetDate(t,false)).All(g=>g.Zip(g.Skip(1)).All(pair=>pair.First.DueDate<=pair.Second.DueDate)),"due date sort inside each date group");
                vm.Tasks.Add(new(){Title="昨天的今日任务",List=vm.Lists[0],TodayDate=DateTime.Today.AddDays(-1)});
                vm.NavigateCommand.Execute(vm.Navigation.First(n=>n.Key=="day"));
                Check(vm.TasksView.Cast<TaskItem>().All(t=>t.TodayDate==DateTime.Today),"today date excludes prior day");
                Check(vm.Flush(),"persist before restart: "+vm.Status);
            }
            using(var restarted=new MainViewModel(store))
            {
                Check(restarted.Tasks.Any(t=>t.Title=="已修改"&&t.Steps.Single().IsCompleted),"restart preserves data");
                Check(restarted.Lists.Contains("测试清单"),"restart preserves lists");
                restarted.EditTitle="";restarted.Flush();Check(restarted.SelectedTask?.Title.Length>0,"empty draft preserves original");
            }
            var valid=store.Load();store.Save(valid);store.Save(valid);
            File.WriteAllText(store.FilePath,"{ broken");
            var recoveryStore=new DataStore(folder);Check(recoveryStore.Load().Tasks.Count==valid.Tasks.Count,"recover valid backup");
            Check(recoveryStore.LoadWarning is not null&&Directory.GetFiles(folder,"*.damaged-*").Length==1,"preserve damaged file");
            recoveryStore.Save(valid);Check(new DataStore(folder).Load().Tasks.Count==valid.Tasks.Count,"save after recovery");
            // Saving to a directory occupied by a file must fail without destroying pending task edits.
            var occupied=Path.Combine(root,Guid.NewGuid().ToString("N"));
            var failedStore=new DataStore(occupied);
            using(var cannotSave=new MainViewModel(failedStore))
            {Directory.CreateDirectory(failedStore.FilePath+".tmp");cannotSave.NewTitle="仍在内存中";cannotSave.AddTaskCommand.Execute(null);Check(!cannotSave.Flush()&&cannotSave.Tasks.Any(t=>t.Title=="仍在内存中"),"save failure retains edits");}
            Console.WriteLine("PASS: task CRUD, undo, notes search, sorting, steps, views, lists, restart, date rollover filter, backup recovery, save failure.");
            VisualChecks.Run(root);
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("OK: "+name);}
}
