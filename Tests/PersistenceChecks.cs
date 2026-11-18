using System.IO;
using System.Windows.Threading;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class PersistenceChecks
{
    public static void Run(string root)
    {
        var directory=Path.Combine(root,"persistence-"+Guid.NewGuid().ToString("N"));
        var fresh=new DataStore(directory);
        Check(fresh.Load().Tasks.Count==0&&File.Exists(fresh.FilePath),"first startup creates empty file, without demo tasks");
        Check(new DataStore(directory).Load().Lists.SequenceEqual(new[]{"默认清单"}),"default list loaded from file");
        using(var vm=new MainViewModel(fresh))
        {
            vm.NewTitle="自动写入文件";vm.AddTaskCommand.Execute(null);
            vm.EditNotes="重启后保留";vm.NewStepTitle="子任务保留";vm.AddStepCommand.Execute(null);
            // Pump the dispatcher so the production 450ms auto-save runs, without calling Flush.
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(1200),DispatcherPriority.Background,(_,_)=>frame.Continue=false,Dispatcher.CurrentDispatcher);
            Dispatcher.PushFrame(frame);timer.Stop();
            var disk=new DataStore(directory).Load();
            Check(disk.Tasks.Count==1,"auto-save task count; "+vm.SaveState+"; "+vm.Status);
            Check(disk.Tasks.Single().Title=="自动写入文件"&&disk.Tasks.Single().Notes=="重启后保留"&&disk.Tasks.Single().Steps.Single().Title=="子任务保留","auto-save writes task, notes and steps to disk");
            vm.NewListName="用户创建的清单";vm.AddListCommand.Execute(null);Check(vm.Flush(),"save custom list");
        }
        using(var reopened=new MainViewModel(new DataStore(directory)))
        {Check(reopened.Tasks.Count==1&&reopened.Lists.Contains("用户创建的清单"),"restart reads tasks and lists from file");}
        var legacy=Path.Combine(root,"legacy-"+Guid.NewGuid().ToString("N"));
        var legacyStore=new DataStore(legacy);legacyStore.Save(TestData.Create());
        var original=File.ReadAllBytes(legacyStore.FilePath);
        var destination=Path.Combine(root,"documents-"+Guid.NewGuid().ToString("N"));
        var migrated=new DataStore(destination,legacy);
        Check(migrated.Load().Tasks.Select(t=>t.Id).SequenceEqual(legacyStore.Load().Tasks.Select(t=>t.Id)),"migration preserves task identities");
        Check(File.ReadAllBytes(legacyStore.FilePath).SequenceEqual(original),"migration leaves old file unchanged");
        var local=migrated.Load();local.Tasks.Clear();migrated.Save(local);
        Check(new DataStore(destination,legacy).Load().Tasks.Count==0,"existing empty document file wins over old data");
        var recovery=Path.Combine(root,"missing-"+Guid.NewGuid().ToString("N"));
        var recoveryStore=new DataStore(recovery);recoveryStore.Save(TestData.Create());File.Move(recoveryStore.FilePath,recoveryStore.FilePath+".bak");
        Check(new DataStore(recovery).Load().Tasks.Count==6&&File.Exists(recoveryStore.FilePath),"missing primary restored from backup instead of resetting");
        Check(DataStore.DefaultDirectory==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"拾序"),"default data location is Documents folder");
        Console.WriteLine("PASS: disk auto-save, empty initialization, restart, one-time legacy migration and missing-file recovery.");
    }
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("OK: "+name);}
}
