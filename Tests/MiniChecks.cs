using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class MiniChecks
{
    public static int Run(string directory)
    {
        var root=Path.Combine(Path.GetFullPath(directory),DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(root);
        var store=new DataStore(Path.Combine(root,"data"));
        var today=DateTime.Today;
        var urgent=new TaskItem{Title="提交项目方案",List="工作",TodayDate=today,CreatedAt=today.AddDays(-3),DueDate=today.AddDays(-2),Priority=0,Notes="原备注",Steps=[new(){Title="保留原步骤",IsCompleted=true}]};
        var data=new AppData{Lists=["工作","个人"],Tasks=[urgent,
            new(){Title="核对交付清单，并确认需要和同事沟通的细节",List="工作",TodayDate=today,CreatedAt=today,DueDate=today,Priority=1},
            new(){Title="整理会议记录",List="工作",TodayDate=today,CreatedAt=today,DueDate=today.AddDays(1),Priority=2},
            new(){Title="回复邮件",List="工作",TodayDate=today,CreatedAt=today,Priority=1},
            new(){Title="准备明天的资料",List="工作",TodayDate=today,CreatedAt=today,Priority=2},
            new(){Title=new string('长',80),List="工作",TodayDate=today,CreatedAt=today,Priority=2},
            new(){Title="已经完成",List="工作",TodayDate=today,CreatedAt=today,IsCompleted=true,CompletedAt=today},
            new(){Title="昨天的任务",List="工作",TodayDate=today.AddDays(-1),CreatedAt=today.AddDays(-1)},
            new(){Title="买咖啡",List="个人",CreatedAt=today,Priority=2}]};
        store.Save(data);
        using var vm=new MainViewModel(store);
        Check(vm.MiniTasksView.Count==6&&vm.MiniTasksView.Cast<TaskItem>().First().Id==urgent.Id,"Mini defaults to today's pending tasks, earliest deadline first");
        vm.Search="不存在";vm.HistoryDate=today.AddYears(-1);vm.DateModeIndex=1;
        Check(vm.VisibleCount==0&&vm.MiniTasksView.Count==6,"Mini ignores full-window search and history filters");
        vm.MiniScopeKey="all";Check(vm.MiniTasksView.Count==8,"Mini all excludes completed tasks");
        vm.MiniScopeKey="list:个人";Check(vm.MiniTasksView.Count==1,"Mini custom list filter");
        vm.MiniScopeKey="important";Check(!vm.MiniHasTasks,"Mini empty important list");
        vm.MiniScopeKey="day";vm.Search="";vm.HistoryDate=null;vm.DateModeIndex=0;
        vm.SelectedTask=vm.Tasks.Single(t=>t.Id==urgent.Id);vm.EditNotes="切换前尚未保存的备注";
        var app=new TestApp();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var window=new TaskDesk.MainWindow(vm);window.Show();Pump(80);window.UpdateLayout();
        var fullWidth=window.Width;var fullHeight=window.Height;
        Click(All<Button>(window).Single(b=>AutomationProperties.GetName(b)=="切换到 Mini 窗口"));Pump(60);window.UpdateLayout();
        Check(window.IsMiniMode&&window.Width==320&&window.Height==420,"Mini entry uses 320 x 420 window");
        Check(app.Windows.Count==1&&!((FrameworkElement)window.FindName("FullLayout")).IsVisible,"same window switches layout without extra process or editor");
        Check(All<Button>((FrameworkElement)window.FindName("MiniTitle")).Where(b=>b.IsVisible).All(b=>!string.IsNullOrEmpty(AutomationProperties.GetName(b))),"Mini titlebar buttons have accessible names");
        Check(vm.Tasks.Single(t=>t.Id==urgent.Id).Notes=="切换前尚未保存的备注","entering Mini commits full editor draft");
        var list=(ListView)window.FindName("MiniTaskList");
        Check(list.ActualWidth>240&&list.ActualHeight>200,"Mini has usable pending-task viewport");
        var title=All<TextBlock>(list).First(t=>t.Text==urgent.Title);Check(title.IsVisible,"task title rendered");
        Check(All<TextBlock>(list).Any(t=>t.Text=="逾期 2 天")&&All<TextBlock>(list).Any(t=>t.Text=="高优先级"),"Mini retains priority and deadline text");
        Capture(window,Path.Combine(root,"mini-320-420.png"));
        window.Width=280;window.Height=240;Pump(60);window.UpdateLayout();
        Check(list.ActualWidth>200&&list.ActualHeight>100,"minimum Mini size remains usable");
        Capture(window,Path.Combine(root,"mini-280-240.png"));
        window.Width=320;window.Height=420;Pump(60);
        var pin=All<CheckBox>(window).Single(c=>AutomationProperties.GetName(c)=="Mini 窗口置顶");Click(pin);Check(window.Topmost,"pin checkbox binds to window topmost");
        var complete=All<CheckBox>(list).Single(c=>AutomationProperties.GetName(c)=="Mini 完成任务："+urgent.Title);Click(complete);Pump(900);
        Check(vm.MiniTasksView.Count==5&&vm.Tasks.Count==9,"completing removes only pending row, not task data");
        var saved=store.Load().Tasks.Single(t=>t.Id==urgent.Id);
        Check(saved.IsCompleted&&saved.CompletedAt.HasValue&&saved.Notes=="切换前尚未保存的备注"&&saved.Steps.Single().IsCompleted,"Mini completion auto-saves timestamp and existing content");
        var scope=(ComboBox)window.FindName("MiniScopePicker");scope.SetCurrentValue(Selector.SelectedValueProperty,"list:个人");Pump(40);
        Check(vm.MiniTasksView.Count==1&&All<TextBlock>(list).Any(t=>t.Text=="买咖啡"),"Mini dropdown changes custom list");
        Check(vm.RenameList(vm.CustomLists.Single(n=>n.Title=="个人"),"生活"),"rename Mini selected list");Pump(40);
        Check(vm.MiniScopeKey=="list:生活"&&scope.SelectedValue?.ToString()=="list:生活"&&vm.MiniTasksView.Count==1,"Mini dropdown follows renamed list without losing scope");
        vm.RenameList(vm.CustomLists.Single(n=>n.Title=="生活"),"个人");Pump(40);
        vm.MiniScopeKey="important";Pump(40);Check(All<TextBlock>(window).Any(t=>t.IsVisible&&t.Text=="暂时没有待办"),"empty pending list explained");
        Capture(window,Path.Combine(root,"mini-empty.png"));
        window.Width=360;window.Height=460;window.Left=SystemParameters.WorkArea.Left+40;window.Top=SystemParameters.WorkArea.Top+40;
        Click(All<Button>(window).Single(b=>AutomationProperties.GetName(b)=="返回完整窗口"));Pump(60);
        Check(!window.IsMiniMode&&window.Width==fullWidth&&window.Height==fullHeight&&!window.Topmost,"expand restores full-window geometry and topmost");
        Check(vm.SelectedTask?.Id==urgent.Id,"expand restores previous visible task selection");
        var settingsPath=Path.Combine(store.DirectoryPath,"mini-window.json");var settings=MiniWindowSettings.Load(settingsPath);
        Check(settings.Width==360&&settings.Height==460&&settings.Topmost,"Mini geometry and pin saved separately from tasks");
        window.WindowState=WindowState.Maximized;Pump(40);window.SetMiniMode(true);Pump(40);
        Check(window.Width==360&&window.Height==460&&window.Topmost,"Mini re-entry restores preferences");
        var shortcut=typeof(TaskDesk.MainWindow).GetMethod("HandleShortcut",BindingFlags.Instance|BindingFlags.NonPublic)!;
        shortcut.Invoke(window,[Key.Escape,ModifierKeys.None,false]);Pump(40);
        Check(!window.IsMiniMode&&window.WindowState==WindowState.Maximized,"Escape restores previous maximized full window");
        shortcut.Invoke(window,[Key.M,ModifierKeys.Control,false]);Pump(40);
        Check(window.IsMiniMode,"Ctrl+M enters Mini");
        shortcut.Invoke(window,[Key.M,ModifierKeys.Control,true]);Check(window.IsMiniMode,"held Ctrl+M does not toggle repeatedly");
        window.Close();
        using var reopened=new MainViewModel(store);
        var second=new TaskDesk.MainWindow(reopened);second.Show();Pump(50);second.SetMiniMode(true);Pump(40);
        Check(second.Width==360&&second.Height==460&&second.Topmost,"restart restores Mini size and pin");
        Check(Math.Abs(second.Left-settings.Left!.Value)<2&&Math.Abs(second.Top-settings.Top!.Value)<2,"restart restores Mini position");
        second.Close();
        var after=store.Load();Check(after.Version==1&&after.Tasks.Count==9&&after.Lists.SequenceEqual(data.Lists),"task schema, all tasks and lists preserved");
        File.WriteAllText(settingsPath,"{broken");Check(MiniWindowSettings.Load(settingsPath).Width==320,"invalid UI settings fall back safely");
        var offscreen=new MiniWindowSettings{Left=100000,Top=-100000};var rect=offscreen.VisibleBounds(new Rect(0,0,320,420));
        Check(rect.Left>=SystemParameters.VirtualScreenLeft&&rect.Right<=SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth&&rect.Top>=SystemParameters.VirtualScreenTop,"offscreen preference returns to desktop bounds");
        app.Shutdown();Console.WriteLine("PASS: Mini filtering, completion autosave, layout, pin, mode switching, shortcuts, geometry persistence and task compatibility. Evidence: "+root);return 0;
    }
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("OK: "+name);}
    private static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {if(root is T item)yield return item;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    private static void Click(ButtonBase button)=>typeof(ButtonBase).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(button,null);
    private static void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(ms),DispatcherPriority.Background,(_,_)=>frame.Continue=false,Dispatcher.CurrentDispatcher);Dispatcher.PushFrame(frame);timer.Stop();}
    private static void Capture(TaskDesk.MainWindow window,string path){window.UpdateLayout();var root=(FrameworkElement)window.FindName("AppRoot");var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
}
