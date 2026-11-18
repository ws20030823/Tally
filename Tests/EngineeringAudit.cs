using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class EngineeringAudit
{
    private static string root = "";
    private static readonly List<object> results = [];
    private static int failures;
    public static int Run(string directory)
    {
        root = Path.Combine(Path.GetFullPath(directory), "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(root);
        Case("F01 unicode and draft switching", () => {
            using var vm = Vm(); Add(vm,"中文 😀 <>&\" "); var a=vm.SelectedTask!;
            vm.EditNotes="多行\n第二行 😀"; Add(vm,"任务B"); vm.SelectedTask=a;
            Require(vm.EditNotes=="多行\n第二行 😀", "switch preserves notes");
            Require(vm.Flush(),vm.Status); Require(a.Title=="中文 😀 <>&\"",a.Title);
        });
        Case("F02 auto-save survives selection clearing", () => {
            var store=Store(); using var vm=new MainViewModel(store); Add(vm,"自动保存"); vm.EditNotes="备注";
            var elapsed=Stopwatch.StartNew(); Pump(1500);
            var disk=store.Load(); Require(disk.Tasks.Count==1,$"after {elapsed.ElapsedMilliseconds}ms disk tasks={disk.Tasks.Count}, state={vm.SaveState}");
            Require(disk.Tasks[0].Notes=="备注","notes persisted");
        });
        Case("F03 empty draft validation feedback", () => {
            using var vm=Vm(); Add(vm,"原题"); vm.EditTitle=" "; Require(vm.Flush(),vm.Status);
            Require(vm.SelectedTask!.Title=="原题","original retained");
            vm.SaveCommand.Execute(null); Require(vm.Status.Contains("不能为空"),vm.Status);
        });
        Case("F04 delete undo edit and repeated delete", () => {
            using var vm=Vm(); Add(vm,"A"); var a=vm.SelectedTask!; Add(vm,"B"); var b=vm.SelectedTask!;
            vm.DeleteTaskCommand.Execute(null); vm.SelectedTask=a; vm.DeleteTaskCommand.Execute(null); vm.UndoDeleteCommand.Execute(null);
            Require(vm.Tasks.Count==1&&vm.Tasks[0]==a&&!vm.CanUndo,"only most recent deletion restored");
        });
        Case("F05 list duplicate whitespace and case", () => {
            using var vm=Vm(); vm.NewListName=" Work "; vm.AddListCommand.Execute(null); vm.NewListName="work"; vm.AddListCommand.Execute(null);
            Require(vm.Lists.Count==2,"case insensitive uniqueness"); Add(vm,"Work task"); Require(vm.SelectedTask!.List=="Work","list default");
        });
        Case("F06 restore completion and step persistence", () => {
            var s=Store(); using(var vm=new MainViewModel(s)) { Add(vm,"步骤任务"); vm.NewStepTitle="子步骤"; vm.AddStepCommand.Execute(null);
                vm.SelectedTask!.Steps[0].IsCompleted=true; vm.SelectedTask.IsCompleted=true; Require(vm.SelectedTask.CompletedAt!=null,"timestamp");
                vm.SelectedTask.IsCompleted=false; Require(vm.SelectedTask.CompletedAt==null,"clear timestamp"); Require(vm.Flush(),vm.Status); }
            Require(s.Load().Tasks.Single().Steps.Single().IsCompleted,"step persisted");
        });
        Case("D01 corrupt primary uses valid backup", () => {
            var s=Store(); s.Save(Data(2)); s.Save(Data(3)); File.WriteAllText(s.FilePath,"{broken");
            Require(s.Load().Tasks.Count==2,"backup loaded"); Require(Directory.GetFiles(s.DirectoryPath,"*.damaged-*").Length==1,"preserved original");
        });
        Case("D02 both corrupt fail without overwriting", () => {
            var s=Store(); s.Save(Data(1)); s.Save(Data(2)); File.WriteAllText(s.FilePath,"broken"); File.WriteAllText(s.FilePath+".bak","also broken");
            try { s.Load(); throw new Exception("accepted corrupt files"); } catch(JsonException) {} catch(InvalidDataException) {}
            Require(File.ReadAllText(s.FilePath)=="broken","retained primary");
        });
        Case("D03 missing Tasks must recover backup", () => {
            var s=Store(); s.Save(Data(2)); s.Save(Data(3)); File.WriteAllText(s.FilePath,"{\"Version\":1,\"Lists\":[\"默认清单\"]}");
            Require(s.Load().Tasks.Count==2,"missing Tasks silently accepted as empty instead of recovering 2 backup tasks");
        });
        Case("D04 null notes should be rejected or normalized", () => {
            var s=Store(); var data=Data(1); data.Tasks[0].Notes=null!; s.Save(data);
            using var vm=new MainViewModel(s); vm.CurrentView="all"; vm.Search="not found"; Require(vm.VisibleCount==0,"safe search");
        });
        Case("D05 duplicate IDs rejected", () => {
            var data=Data(2); data.Tasks[1].Id=data.Tasks[0].Id; try { DataStore.Validate(data); throw new Exception("accepted duplicate"); } catch(InvalidDataException) {}
        });
        Case("D06 file write lock retains memory and retries", () => {
            var s=Store(); using var vm=new MainViewModel(s); Add(vm,"锁定测试");
            using(var held=new FileStream(s.FilePath,FileMode.Open,FileAccess.Read,FileShare.None)) Require(!vm.Flush(),"locked save should fail");
            Require(vm.Tasks.Count==1&&vm.Flush(),"retry"); Require(s.Load().Tasks.Count==1,"disk retry");
        });
        Case("D07 unsupported version preserved", () => {
            var s=Store(); Directory.CreateDirectory(s.DirectoryPath); File.WriteAllText(s.FilePath,"{\"Version\":999}");
            try { s.Load(); throw new Exception("accepted future version"); } catch(InvalidDataException) {}
            Require(File.ReadAllText(s.FilePath).Contains("999"),"unchanged");
        });
        Case("D08 invalid task and step validation", () => {
            foreach(var kind in new[]{"title","list","priority","steps"}) {var d=Data(1); var t=d.Tasks[0];
                if(kind=="title")t.Title=" ";if(kind=="list")t.List="不存在";if(kind=="priority")t.Priority=8;if(kind=="steps")t.Steps.Add(new(){Title=""});
                try{DataStore.Validate(d);throw new Exception("accepted "+kind);}catch(InvalidDataException){} }
        });
        Case("I01 equivalent directory mutex identity", () => {
            var method=typeof(TaskDesk.App).GetMethod("MutexName",BindingFlags.Static|BindingFlags.NonPublic)!;
            var dir=Path.Combine(root,"mutex"); var a=method.Invoke(null,[dir]); var b=method.Invoke(null,[dir+Path.DirectorySeparatorChar]);
            Require(Equals(a,b),$"same directory has different mutex names: {a} / {b}");
        });
        Case("D09 abandoned temp does not overwrite primary", () => {
            var s=Store();s.Save(Data(2));File.WriteAllText(s.FilePath+".tmp","{truncated");Require(s.Load().Tasks.Count==2,"primary remains authoritative");
            s.Save(Data(3));Require(s.Load().Tasks.Count==3,"next save replaces temp safely");
        });
        Case("D10 legacy damaged primary migrates backup unchanged", () => {
            var legacy=Store();legacy.Save(Data(2));legacy.Save(Data(3));File.WriteAllText(legacy.FilePath,"broken");
            var backup=File.ReadAllBytes(legacy.FilePath+".bak");var dest=new DataStore(Store().DirectoryPath,legacy.DirectoryPath);
            Require(dest.Load().Tasks.Count==2,"migrate backup");Require(File.ReadAllText(legacy.FilePath)=="broken"&&File.ReadAllBytes(legacy.FilePath+".bak").SequenceEqual(backup),"legacy untouched");
        });
        Case("S01 repeated edits save reload integrity", () => {
            var s=Store();using(var vm=new MainViewModel(s))for(var i=0;i<200;i++) {Add(vm,"连续任务 "+i);vm.EditNotes="批次 "+i;
                if(i%10==0)Require(vm.Flush(),vm.Status);if(i%5==0){vm.DeleteTaskCommand.Execute(null);vm.UndoDeleteCommand.Execute(null);} }
            // Dispose intentionally does not imply save: explicitly save the final edit batch in a second run.
            using(var vm=new MainViewModel(s)) {Require(vm.Tasks.Count==191,"last explicit flush at index 190");Add(vm,"最后写入");Require(vm.Flush(),vm.Status);}
            Require(s.Load().Tasks.Count==192,"final reload count");
        });
        Case("S02 disposed view models collectible", () => {
            WeakReference Create(){var vm=Vm();Add(vm,"内存回收");vm.Dispose();return new WeakReference(vm);}
            var refs=Enumerable.Range(0,30).Select(_=>Create()).ToArray();Pump(100);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            Require(refs.All(w=>!w.IsAlive),$"alive={refs.Count(w=>w.IsAlive)} / 30");
        });
        foreach(var count in new[]{100,1000,10000}) Case("P"+count+" performance",()=>Performance(count));
        Case("U01 window layout and list realization", Visual);
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"AUDIT: cases={results.Count}, failed={failures}, evidence={root}");
        return failures==0?0:2;
    }
    private static void Performance(int count)
    {
        var s=Store();var data=Data(count); var sw=Stopwatch.StartNew();s.Save(data);var write=sw.Elapsed.TotalMilliseconds;
        sw.Restart();s.Load();var read=sw.Elapsed.TotalMilliseconds;sw.Restart();
        using var vm=new MainViewModel(s);var startup=sw.Elapsed.TotalMilliseconds;vm.CurrentView="all";
        var searches=new List<double>();var sorts=new List<double>();var flushes=new List<double>();
        for(var repeat=0;repeat<5;repeat++) {
            foreach(var q in new[]{"x","任务","999","备注","missing",""}){sw.Restart();vm.Search=q;searches.Add(sw.Elapsed.TotalMilliseconds);}
            foreach(var mode in new[]{1,2,0}){sw.Restart();vm.SortIndex=mode;sorts.Add(sw.Elapsed.TotalMilliseconds);}
            sw.Restart();Require(vm.Flush(),vm.Status);flushes.Add(sw.Elapsed.TotalMilliseconds);
        }
        var metric=new{count,bytes=new FileInfo(s.FilePath).Length,writeMs=write,readMs=read,vmStartupMs=startup,searchMs=searches,sortMs=sorts,flushMs=flushes,managedMB=GC.GetTotalMemory(false)/1048576d};
        File.WriteAllText(Path.Combine(root,$"performance-{count}.json"),JsonSerializer.Serialize(metric,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("METRIC "+JsonSerializer.Serialize(metric));
    }
    private static void Visual()
    {
        var app=new TestApp{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();
        var s=Store();s.Save(Data(1000));using var vm=new MainViewModel(s);vm.CurrentView="all";
        var errors=new StringWriter();var listener=new TextWriterTraceListener(errors);PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Warning;
        var window=new TaskDesk.MainWindow(vm);var sw=Stopwatch.StartNew();window.Show();window.UpdateLayout();
        var list=(System.Windows.Controls.ListView)window.FindName("TaskList");
        Console.WriteLine($"UI count=1000 showLayoutMs={sw.Elapsed.TotalMilliseconds:F1} realized={Count<ListViewItem>(list)} groupingVirtualization={VirtualizingPanel.GetIsVirtualizingWhenGrouping(list)} canContentScroll={ScrollViewer.GetCanContentScroll(list)} tier={RenderCapability.Tier>>16}");
        SaveWindow(window,"window-1280.png");window.Width=980;window.Height=660;window.UpdateLayout();Pump(150);window.UpdateLayout();SaveWindow(window,"window-980.png");
        Console.WriteLine($"UI minimum listWidth={list.ActualWidth:F1}, windowWidth={window.ActualWidth:F1}");
        var row=(FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(vm.TasksView.Cast<TaskItem>().First());
        Console.WriteLine($"UI minimum firstRowWidth={row?.ActualWidth:F1}, desiredWidth={row?.DesiredSize.Width:F1}, selected={vm.SelectedTask?.Title}");
        var clipped=row!=null&&row.ActualWidth>list.ActualWidth+2;
        File.WriteAllText(Path.Combine(root,"binding-warnings.log"),errors.ToString());
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);window.ConfigurePreview(Path.Combine(root,"unused.png"));window.Close();app.Shutdown();
        Require(!clipped,"task row wider than viewport at minimum supported window size; right-side star clipped");
    }
    private static int Count<T>(DependencyObject o) {var n=o is T?1:0;for(var i=0;i<VisualTreeHelper.GetChildrenCount(o);i++)n+=Count<T>(VisualTreeHelper.GetChild(o,i));return n;}
    private static void SaveWindow(TaskDesk.MainWindow w,string name) {var el=(FrameworkElement)w.FindName("AppRoot");var bmp=new RenderTargetBitmap((int)el.ActualWidth,(int)el.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(el);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(root,name));encoder.Save(f);}
    private static void Pump(int ms) {var frame=new DispatcherFrame();var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(ms),DispatcherPriority.Background,(_,_)=>frame.Continue=false,Dispatcher.CurrentDispatcher);Dispatcher.PushFrame(frame);timer.Stop();}
    private static DataStore Store()=>new(Path.Combine(root,Guid.NewGuid().ToString("N")));
    private static MainViewModel Vm()=>new(Store());
    private static void Add(MainViewModel vm,string title){vm.NewTitle=title;vm.AddTaskCommand.Execute(null);}
    private static AppData Data(int count)=>new(){Tasks=Enumerable.Range(0,count).Select(i=>new TaskItem{Title="任务 "+i,List="默认清单",CreatedAt=DateTime.Today.AddDays(-i%30),TodayDate=DateTime.Today,Notes="备注 "+new string('测',100),DueDate=DateTime.Today.AddDays(i%10),Priority=i%3,Steps=[new(){Title="步骤一"},new(){Title="步骤二"}]}).ToList()};
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Case(string name,Action body){var sw=Stopwatch.StartNew();try{body();results.Add(new{name,passed=true,ms=sw.Elapsed.TotalMilliseconds});Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;results.Add(new{name,passed=false,ms=sw.Elapsed.TotalMilliseconds,error=ex.ToString()});Console.WriteLine("FAIL "+name+": "+ex.Message);}}
}
