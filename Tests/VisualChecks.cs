using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class VisualChecks
{
    public static void Run(string root)
    {
        var app=new TestApp { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var store=new DataStore(Path.Combine(root,"visual-"+Guid.NewGuid().ToString("N")));
        var visualData=TestData.Create();
        for(var i=0;i<visualData.Tasks.Count;i++)visualData.Tasks[i].CreatedAt=DateTime.Today.AddDays(-i).AddHours(9);
        store.Save(visualData);
        using var vm=new MainViewModel(store);
        var window=new TaskDesk.MainWindow(vm);
        window.Show();window.UpdateLayout();
        var list=(Wpf.Ui.Controls.ListView)window.FindName("TaskList");
        list.ScrollIntoView(vm.SelectedTask);window.UpdateLayout();
        var item=(FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(vm.SelectedTask!);
        Check(item is not null,"selected task is rendered");
        var itemImage=Render(item!);
        Check(Alpha(itemImage,3,3)<8,"selected row corner has no rectangular underlay");
        Check(Alpha(itemImage,itemImage.PixelWidth/2,itemImage.PixelHeight/2)>15,"row glass is visible");
        var newTask=(Wpf.Ui.Controls.TextBox)window.FindName("NewTaskBox");
        var search=(Wpf.Ui.Controls.TextBox)window.FindName("SearchBox");
        vm.NewTitle="准备下一次设计评审";
        FocusManager.SetFocusedElement(window,newTask);Keyboard.Focus(newTask);window.UpdateLayout();
        Check(newTask.IsFocused,"quick-add editor is actually focused");
        var inner=ContentBorder(newTask);
        Check(inner.Background is SolidColorBrush b&&b.Color.A==0,"focused quick-add editor remains transparent");
        var underline=AccentBorder(newTask);
        Check(underline.BorderBrush is SolidColorBrush line&&line.Color.A==0,"no blue quick-add underline");
        Save(Render((FrameworkElement)window.FindName("AppRoot")),Path.Combine(root,"focused-new-task.png"));
        vm.NewTitle="";
        FocusManager.SetFocusedElement(window,search);Keyboard.Focus(search);window.UpdateLayout();
        Check(search.IsFocused,"search editor is actually focused");
        var searchBorder=ContentBorder(search);
        Check(searchBorder.Background is SolidColorBrush focusedSearch&&focusedSearch.Color==(Color)window.FindResource("InputFocusedColor"),"focused search uses the selected theme focus surface");
        var searchLine=AccentBorder(search);
        Check(searchLine.BorderBrush is SolidColorBrush brush&&brush.Color.A==0,"no blue search underline");
        Save(Render((FrameworkElement)window.FindName("AppRoot")),Path.Combine(root,"focused-search.png"));
        var date=(DatePicker)window.FindName("DueDatePicker");
        var dateEditor=(System.Windows.Controls.Primitives.DatePickerTextBox)date.Template.FindName("PART_TextBox",date);
        FocusManager.SetFocusedElement(window,dateEditor);Keyboard.Focus(dateEditor);window.UpdateLayout();
        Check(dateEditor.IsFocused,"date editor is actually focused");
        Check(ContentBorder(dateEditor).Background is SolidColorBrush focusedDate&&focusedDate.Color==(Color)window.FindResource("InputFocusedColor"),"date editor uses the selected theme focus surface");
        var picker=(ComboBox)window.FindName("TaskListPicker");
        FocusManager.SetFocusedElement(window,picker);Keyboard.Focus(picker);window.UpdateLayout();
        Check(ContentBorder(picker).Background is SolidColorBrush,"list picker retains a themed surface when focused");
        FocusManager.SetFocusedElement(window,null);Keyboard.ClearFocus();window.UpdateLayout();
        Save(Render((FrameworkElement)window.FindName("AppRoot")),Path.Combine(root,"glass-default.png"));
        window.Close();app.Shutdown();
        Console.WriteLine("PASS: rendered rounded corners and genuine focused input states; library templates retained.");
    }
    private static RenderTargetBitmap Render(FrameworkElement element)
    {var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);return bitmap;}
    private static byte Alpha(BitmapSource image,int x,int y)
    {var pixel=new byte[4];image.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel[3];}
    private static void Save(BitmapSource image,string path)
    {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);}
    private static void Check(bool passed,string label)
    {if(!passed)throw new InvalidOperationException("FAIL: "+label);Console.WriteLine("OK: "+label);}
    private static Border ContentBorder(Control control)
    {
        return (Border?)control.Template.FindName("ContentBorder",control)??throw new Exception("ContentBorder missing");
    }
    private static Border AccentBorder(Control control)=>(Border?)control.Template.FindName("AccentBorder",control)??throw new Exception("AccentBorder missing");
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
}
