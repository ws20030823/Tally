using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class ScrollChecks
{
    public static int Run(string directory)
    {
        var root=Path.Combine(Path.GetFullPath(directory),Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var app=new TestApp{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();
        var store=new DataStore(root);
        store.Save(new(){Tasks=Enumerable.Range(0,1000).Select(i=>new TaskItem
        {Title="滚动测试 "+i,List="默认清单",CreatedAt=DateTime.Today.AddDays(-i%30),TodayDate=DateTime.Today}).ToList()});
        using var vm=new MainViewModel(store);
        var w=new TaskDesk.MainWindow(vm);w.Show();Pump(150);w.UpdateLayout();
        var list=(ListView)w.FindName("TaskList");var scroll=All<ScrollViewer>(list).First();
        Require(VirtualizingPanel.GetScrollUnit(list)==ScrollUnit.Pixel,"pixel scrolling enabled");
        Require(VirtualizingPanel.GetIsVirtualizingWhenGrouping(list)&&VirtualizingPanel.GetVirtualizationMode(list)==VirtualizationMode.Recycling,"group recycling remains enabled");
        var offsets=new List<double>();
        scroll.ScrollChanged+=(_,e)=>{if(e.VerticalChange!=0)offsets.Add(scroll.VerticalOffset);};
        var initial=scroll.VerticalOffset;
        Wheel(list,-120);Pump(30);var mid=scroll.VerticalOffset;Pump(450);var settled=scroll.VerticalOffset;
        Console.WriteLine($"SCROLL initial={initial}, mid={mid}, settled={settled}, max={scroll.ScrollableHeight}, viewport={scroll.ViewportHeight}");
        Require(settled>initial&&offsets.Any(o=>o>initial&&o<settled-.5),"wheel moves through intermediate pixel offsets");
        Wheel(list,-600);Pump(15);Wheel(list,-600);Pump(500);var accumulated=scroll.VerticalOffset;
        Require(accumulated>settled,"rapid wheel input accumulates");
        Wheel(list,120);Pump(450);Require(scroll.VerticalOffset<accumulated,"reverse wheel responds");
        var count=All<ListViewItem>(list).Count();Require(count<150,"1000 tasks remain virtualized, realized="+count);
        for(var i=0;i<12;i++){scroll.ScrollToEnd();Pump(100);if(Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<.5)break;}
        var end=scroll.VerticalOffset;
        Console.WriteLine($"END offset={end}, max={scroll.ScrollableHeight}");
        var boundary=Wheel(list,-120);Pump(100);
        Require(Math.Abs(scroll.VerticalOffset-end)<1&&!boundary.Handled,"end boundary does not trap wheel");
        scroll.ScrollToTop();Pump(150);Wheel(list,120);Pump(80);Require(scroll.VerticalOffset==0,"top clamp");
        var nav=All<Button>(w).First(b=>AutomationProperties.GetName(b)=="今天");
        Require(nav.Effect==null,"navigation has no whole-content rectangular shadow");
        Require(nav.Background is ImageBrush && nav.FocusVisualStyle is null,"navigation selection uses the library material and rounded keyboard focus");
        var bmp=new RenderTargetBitmap((int)nav.ActualWidth,(int)nav.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(nav);
        var corner=new byte[4];bmp.CopyPixels(new Int32Rect(2,2,1,1),corner,4,0);Require(corner[3]<8,"navigation corners have no rectangular background");
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(root,"navigation.png")))encoder.Save(f);
        File.WriteAllText(Path.Combine(root,"metrics.json"),JsonSerializer.Serialize(new{initial,mid,settled,accumulated,offsets,realized=count,extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight}));
        Wheel(list,-120);w.ConfigurePreview(Path.Combine(root,"unused.png"));w.Close();Pump(100);
        app.Shutdown();Console.WriteLine("PASS: pixel wheel interpolation, accumulated input, reversal, boundaries, virtualization and navigation rendering. Evidence="+root);return 0;
    }
    private static MouseWheelEventArgs Wheel(UIElement target,int delta)
    {var e=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=Mouse.PreviewMouseWheelEvent};target.RaiseEvent(e);return e;}
    private static IEnumerable<T> All<T>(DependencyObject o) where T:DependencyObject
    {if(o is T t)yield return t;for(var i=0;i<VisualTreeHelper.GetChildrenCount(o);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(o,i)))yield return x;}
    private static void Require(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("OK: "+message);}
    private static void Pump(int ms)
    {var frame=new DispatcherFrame();var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(ms),DispatcherPriority.Background,(_,_)=>frame.Continue=false,Dispatcher.CurrentDispatcher);Dispatcher.PushFrame(frame);timer.Stop();}
}
