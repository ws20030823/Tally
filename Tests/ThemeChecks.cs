using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class ThemeChecks
{
    public static int Run(string directory)
    {
        var root=Path.Combine(Path.GetFullPath(directory),DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(root);
        var store=new DataStore(Path.Combine(root,"data"));var today=DateTime.Today;
        store.Save(new(){Lists=["工作","个人"],Tasks=[
            new(){Title="提交项目方案",List="工作",Priority=0,TodayDate=today,CreatedAt=today,DueDate=today.AddDays(-2),Notes="原备注"},
            new(){Title="核对交付清单",List="工作",Priority=1,TodayDate=today,CreatedAt=today,DueDate=today},
            new(){Title="整理会议记录",List="个人",Priority=2,TodayDate=today,CreatedAt=today,DueDate=today.AddDays(1)}]});
        var original=File.ReadAllBytes(store.FilePath);
        var app=new TestApp();app.InitializeComponent();
        using var vm=new MainViewModel(store);var w=new TaskDesk.MainWindow(vm);w.Show();Pump(50);w.UpdateLayout();
        var picker=(ComboBox)w.FindName("ThemePicker");
        Check(w.CurrentTheme=="warm"&&picker.Items.Count==2,"two theme choices, warm default");
        Check(ColorOf(((FrameworkElement)w.FindName("AppRoot")).GetValue(Panel.BackgroundProperty))==ColorValue("#F4F3EF"),"warm neutral canvas rendered");
        Check(w.FontFamily.Source.StartsWith("MiSans"),"MiSans shared font");
        Check(w.GlassBackdrop is null&&!All<System.Windows.Shapes.Ellipse>((FrameworkElement)w.FindName("WindowBackdrop")).Any(),"no multicolor blobs or material renderer");
        Check(Accent(w)==ColorValue("#285C4A"),"warm forest accent");
        Capture(w,Path.Combine(root,"A-warm-full.png"));
        picker.SetCurrentValue(Selector.SelectedValueProperty,"blue");Pump(40);
        Check(w.CurrentTheme=="blue"&&Accent(w)==ColorValue("#476B91"),"dropdown changes live palette");
        Check(ColorOf(((Panel)w.FindName("AppRoot")).Background)==ColorValue("#EDF1F5"),"blue canvas updates without restart");
        Check(ColorOf(((Border)w.FindName("SidebarSurface")).Background)==ColorValue("#E4EAF0"),"sidebar updates with palette");
        var search=(TextBox)w.FindName("SearchBox");Keyboard.Focus(search);Pump(30);
        var content=(Border)search.Template.FindName("ContentBorder",search);
        Check(ColorOf(content.Background)==ColorValue("#EBF2FA"),"focused input uses blue palette rather than stale warm color");
        var checkedBox=All<CheckBox>(w).Single(c=>c.Content?.ToString()?.Contains("加入")==true);
        Check(All<Border>(checkedBox).Any(b=>b.Background is SolidColorBrush brush&&brush.Color==Accent(w)),"checked checkbox updates to blue accent while staying checked");
        w.ApplyTheme("warm");Pump(20);
        Check(ColorOf(content.Background)==ColorValue("#EDF3ED"),"already focused input updates to warm without losing focus");
        w.ApplyTheme("blue");Pump(20);
        Check(ColorOf(content.Background)==ColorValue("#EBF2FA"),"already focused input updates back to blue");
        Keyboard.ClearFocus();Capture(w,Path.Combine(root,"B-blue-full.png"));
        var preferences=Path.Combine(store.DirectoryPath,"appearance.json");
        Check(AppearanceSettings.Load(preferences).Theme=="blue","selected theme persisted outside task file");
        for(var i=0;i<12;i++)w.ApplyTheme(i%2==0?"warm":"blue");Pump(30);
        Check(app.Resources.MergedDictionaries.Count==5,"repeated switches replace rather than accumulate palettes");
        Check(File.ReadAllBytes(store.FilePath).SequenceEqual(original),"theme switches do not write task data");
        var task=vm.SelectedTask;vm.EditNotes="切换主题时保留的草稿";w.ApplyTheme("warm");Pump(20);
        Check(vm.SelectedTask==task&&vm.EditNotes=="切换主题时保留的草稿","switch preserves selection and editor draft");
        Check(vm.Flush(),"editor still saves normally");
        w.ApplyTheme("blue");w.SetMiniMode(true);Pump(40);
        Check(w.CurrentTheme=="blue"&&((Button)w.FindName("MiniThemeButton")).Content?.ToString()=="雾蓝","Mini inherits full-window theme");
        Capture(w,Path.Combine(root,"B-blue-mini.png"));
        Click((ButtonBase)w.FindName("MiniThemeButton"));Pump(40);
        Check(w.CurrentTheme=="warm"&&picker.SelectedValue?.ToString()=="warm","Mini button also updates full-window selector");
        Capture(w,Path.Combine(root,"A-warm-mini.png"));
        w.Width=280;w.Height=240;Pump(30);Capture(w,Path.Combine(root,"A-warm-mini-minimum.png"));
        Check(((ListView)w.FindName("MiniTaskList")).ActualWidth>200,"Mini remains usable with theme button at minimum size");
        w.SetMiniMode(false);w.Width=980;w.Height=660;Pump(30);Capture(w,Path.Combine(root,"A-warm-full-minimum.png"));
        Check(picker.IsVisible&&picker.ActualWidth>130,"theme selector remains usable at minimum full-window size");
        w.ApplyTheme("blue");w.Close();
        using var restarted=new MainViewModel(store);var second=new TaskDesk.MainWindow(restarted);second.Show();Pump(40);
        Check(second.CurrentTheme=="blue"&&Accent(second)==ColorValue("#476B91"),"theme restored after restart");
        Check(store.Load().Version==1&&store.Load().Tasks.Count==3&&store.Load().Tasks.Any(t=>t.Notes=="切换主题时保留的草稿"),"task schema and content preserved");
        second.ApplyTheme("unknown");Check(second.CurrentTheme=="warm","unknown theme falls back safely");second.Close();
        File.WriteAllText(preferences,"{broken");Check(AppearanceSettings.Load(preferences).Theme=="warm","invalid appearance settings fall back safely");
        app.Shutdown();Console.WriteLine("PASS: both palettes, live controls, focus, Mini synchronization, preference persistence, draft preservation and data compatibility. Evidence: "+root);return 0;
    }
    private static Color Accent(Window w)=>ColorOf(w.FindResource("AccentBrush"));
    private static Color ColorOf(object? brush)=>brush is SolidColorBrush solid?solid.Color:throw new Exception("expected solid theme brush: "+brush?.GetType().Name);
    private static Color ColorValue(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("OK: "+message);}
    private static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {if(root is T item)yield return item;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
    private static void Click(ButtonBase button)=>typeof(ButtonBase).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(button,null);
    private static void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(ms),DispatcherPriority.Background,(_,_)=>frame.Continue=false,Dispatcher.CurrentDispatcher);Dispatcher.PushFrame(frame);timer.Stop();}
    private static void Capture(TaskDesk.MainWindow window,string path){window.UpdateLayout();var root=(FrameworkElement)window.FindName("AppRoot");var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
}
