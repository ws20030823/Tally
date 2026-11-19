using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskDesk.ViewModels;
using TaskDesk.Models;
using TaskDesk.Services;
using Wpf.Ui.Controls;
using DatePicker = System.Windows.Controls.DatePicker;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace TaskDesk;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }
    private bool preview;
    private readonly string miniSettingsPath;
    private readonly MiniWindowSettings miniSettings;
    private readonly string appearanceSettingsPath;
    private ResourceDictionary? palette;
    private bool applyingTheme,applyingOpacity;
    private DispatcherTimer? opacitySaveTimer;
    private WindowTransparency? transparency;
    private HwndSource? miniCaptionSource;
    public int CurrentOpacityPercent { get; private set; } = 100;
    public string CurrentTheme { get; private set; } = "warm";
    private Rect fullBounds;
    private TaskItem? fullSelection;
    private bool fullWasMaximized, fullWasTopmost;
    public bool IsMiniMode { get; private set; }
    public MainWindow(MainViewModel vm)
    {
        ViewModel=vm;InitializeComponent();DataContext=vm;
        miniSettingsPath=Path.Combine(Path.GetDirectoryName(vm.DataPath)!,"mini-window.json");
        miniSettings=MiniWindowSettings.Load(miniSettingsPath);
        appearanceSettingsPath=Path.Combine(Path.GetDirectoryName(vm.DataPath)!,"appearance.json");
        ThemePicker.ItemsSource=AppearanceSettings.Themes;
        var appearance=AppearanceSettings.Load(appearanceSettingsPath);
        ApplyTheme(appearance.Theme,false);
        ApplyOpacity(appearance.OpacityPercent,false);
        SourceInitialized+=(_,_)=>{transparency=new WindowTransparency(this);ApplyOpacity(CurrentOpacityPercent,false);};
        vm.ConfirmTaskDeletion=ConfirmTaskDeletion;
        vm.MiniTaskAdded+=RevealMiniTask;
        Closed+=(_,_)=>{DetachMiniCaptionHook();transparency?.Dispose();opacitySaveTimer?.Stop();vm.ConfirmTaskDeletion=null;vm.MiniTaskAdded-=RevealMiniTask;if(palette is not null)Application.Current?.Resources.MergedDictionaries.Remove(palette);};
        Loaded+=(_,_)=>
        {
            InitializeAccessibleControls();
        };
        StateChanged+=(_,_)=>UpdateTitleBarNames();
        MiniTitle.Loaded+=(_,_)=>UpdateTitleBarNames();
        Activated+=(_,_)=>vm.CheckDay();
    }

    private void ThemeSelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(!applyingTheme&&appearanceSettingsPath is not null&&ThemePicker.SelectedItem is ThemeOption choice)ApplyTheme(choice.Key);
    }
    private void ToggleThemeClick(object sender,RoutedEventArgs e) => ApplyTheme(CurrentTheme=="warm"?"blue":"warm");
    public void ApplyTheme(string? key,bool persist=true)
    {
        key=AppearanceSettings.Normalize(key);
        applyingTheme=true;
        try
        {
            var next=new ResourceDictionary{Source=new Uri($"/TaskDesk;component/Themes/Palettes/{(key=="blue"?"Blue":"Warm")}.xaml",UriKind.Relative)};
            // Template-local focus brushes resolve through application resources too.
            var dictionaries=Application.Current?.Resources.MergedDictionaries??Resources.MergedDictionaries;
            if(palette is null)dictionaries.Add(next);
            else dictionaries[dictionaries.IndexOf(palette)]=next;
            palette=next;CurrentTheme=key;
            ThemePicker.SelectedValue=key;
            MiniThemeButton.Content=key=="blue"?"雾蓝":"暖白";
            MiniThemeButton.ToolTip=$"当前主题：{AppearanceSettings.Themes.Single(t=>t.Key==key).Title}；点击切换";
            if(persist&&!preview)
            {
                try {SaveAppearance();}
                catch(Exception e) when(e is IOException or UnauthorizedAccessException)
                {ViewModel.Status="主题已切换，但偏好未能保存："+e.Message;}
            }
        }
        finally {applyingTheme=false;}
    }

    private void SaveAppearance() => new AppearanceSettings{Theme=CurrentTheme,OpacityPercent=CurrentOpacityPercent}.Save(appearanceSettingsPath);
    private void OpacitySliderChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(ReferenceEquals(e.OriginalSource,sender)&&!applyingOpacity&&appearanceSettingsPath is not null)ApplyOpacity((int)Math.Round(e.NewValue));
    }
    private void OpenOpacityPopup(object sender,RoutedEventArgs e)=>MiniOpacityPopup.IsOpen=!MiniOpacityPopup.IsOpen;
    public void ApplyOpacity(int percent,bool persist=true)
    {
        percent=AppearanceSettings.NormalizeOpacity(percent);
        try{transparency?.Apply(percent);}
        catch(Win32Exception e){ViewModel.Status="透明度调整失败："+e.Message;return;}
        applyingOpacity=true;
        try
        {
            CurrentOpacityPercent=percent;OpacitySlider.Value=MiniOpacitySlider.Value=percent;
            OpacityValueText.Text=$"{percent}%";MiniOpacityButton.Content=$"{percent}%";
        }
        finally{applyingOpacity=false;}
        if(!persist||preview)return;
        // Dragging updates the native window immediately; preference writes wait until dragging settles.
        opacitySaveTimer??=new DispatcherTimer(TimeSpan.FromMilliseconds(400),DispatcherPriority.Background,(_,_)=>{
            opacitySaveTimer!.Stop();
            try{SaveAppearance();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){ViewModel.Status="透明度已调整，但偏好未能保存："+e.Message;}
        },Dispatcher);
        opacitySaveTimer.Stop();opacitySaveTimer.Start();
    }

    private void OpenTaskComposer(object sender,RoutedEventArgs e) => ShowTaskComposer();
    private void OpenMiniTaskEditor(object sender,RoutedEventArgs e)=>ShowMiniTaskEditor(null);
    private void EditMiniTask(object sender,MouseButtonEventArgs e)
    {
        if(sender is FrameworkElement {DataContext:TaskItem task})ShowMiniTaskEditor(task);
        e.Handled=true;
    }
    private void ShowMiniTaskEditor(TaskItem? task)
    {
        if(task is not null&&task==ViewModel.SelectedTask)ViewModel.SaveCommand.Execute(null);
        var draft=new TaskEditorViewModel(ViewModel.Lists,task??new TaskItem{Title=ViewModel.MiniNewTitle,List=ViewModel.Lists[0]});
        var dialog=new Controls.TaskEditorDialog(draft,task is null){Owner=this,Topmost=Topmost};
        if(dialog.ShowDialog()==true)ViewModel.SaveMiniTask(draft,task);
    }
    private void RevealMiniTask(TaskItem task)
    {
        if(!IsMiniMode)return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            if(!IsMiniMode)return;
            MiniTaskList.ScrollIntoView(task);MiniNewTaskBox.Focus();
        }));
    }
    private void ShowTaskComposer()
    {
        if(IsMiniMode)SetMiniMode(false);
        ViewModel.StartAddingTaskCommand.Execute(null);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>NewTaskBox.Focus()));
    }
    private void SubmitNewTask(object sender,RoutedEventArgs e)
    {
        if(!string.IsNullOrEmpty(NewTaskDateFeedback.Text)){NewTaskDatePicker.Focus();return;}
        ViewModel.CreateTaskCommand.Execute(null);
        if(ViewModel.IsAddingTask)NewTaskBox.Focus();
    }
    private void CancelTaskComposer(object sender,RoutedEventArgs e)
    {ViewModel.CancelAddingTaskCommand.Execute(null);ClearDateFeedback(NewTaskDatePicker);}
    private void NewTaskTitleKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Return){e.Handled=true;SubmitNewTask(sender,e);}
    }
    private void ClearNewTaskDate(object sender,RoutedEventArgs e)
    {ViewModel.NewTaskDate=null;ClearDateFeedback(NewTaskDatePicker);}
    private void EnterMiniClick(object sender,RoutedEventArgs e) => SetMiniMode(true);
    private void ExitMiniClick(object sender,RoutedEventArgs e) => SetMiniMode(false);
    public void SetMiniMode(bool enabled)
    {
        if(enabled==IsMiniMode)return;
        MiniOpacityPopup.IsOpen=false;
        if(enabled)
        {
            // Commit the full editor once; the Mini list only changes completion state.
            fullSelection=ViewModel.SelectedTask;
            ViewModel.CloseDetailsCommand.Execute(null);
            fullWasMaximized=WindowState==WindowState.Maximized;
            fullWasTopmost=Topmost;
            fullBounds=WindowState==WindowState.Normal?new Rect(Left,Top,ActualWidth,ActualHeight):RestoreBounds;
            WindowState=WindowState.Normal;
            IsMiniMode=true;FullLayout.Visibility=Visibility.Collapsed;MiniLayout.Visibility=Visibility.Visible;
            MinWidth=280;MinHeight=240;
            MaxWidth=900;MaxHeight=1200;
            var bounds=miniSettings.VisibleBounds(new Rect(fullBounds.Right-340,fullBounds.Top+40,320,420));
            Width=bounds.Width;Height=bounds.Height;Left=bounds.Left;Top=bounds.Top;
            Topmost=miniSettings.Topmost;Title="Tally Mini";
            MiniScopePicker.Focus();
        }
        else
        {
            RememberMiniBounds();
            IsMiniMode=false;MiniLayout.Visibility=Visibility.Collapsed;FullLayout.Visibility=Visibility.Visible;
            MaxWidth=double.PositiveInfinity;MaxHeight=double.PositiveInfinity;
            MinWidth=980;MinHeight=660;
            Width=fullBounds.Width;Height=fullBounds.Height;Left=fullBounds.Left;Top=fullBounds.Top;
            Topmost=fullWasTopmost;Title="Tally";
            if(fullSelection is not null&&ViewModel.TasksView.Contains(fullSelection))ViewModel.SelectedTask=fullSelection;
            if(fullWasMaximized)WindowState=WindowState.Maximized;
        }
        DetachMiniCaptionHook();
        if(enabled)
        {
            miniCaptionSource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            miniCaptionSource?.AddHook(MiniCaptionHitTest);
        }
        UpdateTitleBarNames();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,UpdateTitleBarNames);
    }
    private void DetachMiniCaptionHook()
    {
        if(miniCaptionSource is {IsDisposed:false})miniCaptionSource.RemoveHook(MiniCaptionHitTest);
        miniCaptionSource=null;
    }
    private nint MiniCaptionHitTest(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        // The full title bar still owns the window hook after switching layouts.
        // Route this Mini control to the client without changing drag/resize zones.
        if(IsMiniMode&&message==0x0084)
        {
            var packed=lParam.ToInt64();
            var screenPoint=new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff));
            foreach(var control in new[]{MiniThemeButton,MiniOpacityButton})
            {
                if(!control.IsVisible)continue;
                var point=control.PointFromScreen(screenPoint);
                if(new Rect(control.RenderSize).Contains(point)){handled=true;return 1;}
            }
        }
        return 0;
    }
    private void RememberMiniBounds()
    {
        var bounds=WindowState==WindowState.Normal?new Rect(Left,Top,Width,Height):RestoreBounds;
        miniSettings.Width=bounds.Width;miniSettings.Height=bounds.Height;
        miniSettings.Left=bounds.Left;miniSettings.Top=bounds.Top;miniSettings.Topmost=Topmost;
        if(preview)return;
        try {miniSettings.Save(miniSettingsPath);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {ViewModel.Status="窗口位置未能保存，任务数据不受影响："+e.Message;}
    }

    private bool ConfirmTaskDeletion(TaskItem task)
    {
        var title=task==ViewModel.SelectedTask&&!string.IsNullOrWhiteSpace(ViewModel.EditTitle)?ViewModel.EditTitle.Trim():task.Title;
        return System.Windows.MessageBox.Show(this,
            $"确定删除任务“{title}”吗？\n任务及其子任务将一并删除，删除后无法撤销。",
            "删除任务",System.Windows.MessageBoxButton.OKCancel,MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel)==System.Windows.MessageBoxResult.OK;
    }

    private void DeleteListClick(object sender, RoutedEventArgs e)
    {
        e.Handled=true;
        if(sender is not FrameworkElement { DataContext: NavEntry entry } || !ViewModel.CustomLists.Contains(entry))return;
        if(ViewModel.Lists.Count<=1)
        {
            ViewModel.Status="至少保留一个清单；可以先新建其他清单再删除";
            return;
        }
        var destination=ViewModel.Lists.First(name=>name!=entry.Title);
        // The selected editor may have an unsaved change to its list; include that draft in the count.
        var count=ViewModel.Tasks.Count(task=>(ReferenceEquals(task,ViewModel.SelectedTask)?ViewModel.EditList:task.List)==entry.Title);
        if(count>0)
        {
            var result=System.Windows.MessageBox.Show(this,
                $"删除清单“{entry.Title}”？\n其中的 {count} 项任务会移至“{destination}”，任务内容会保留。",
                "Tally",System.Windows.MessageBoxButton.YesNo,MessageBoxImage.Question,System.Windows.MessageBoxResult.No);
            if(result!=System.Windows.MessageBoxResult.Yes)return;
        }
        ViewModel.DeleteListCommand.Execute(entry);
    }

    private static object? MenuTarget(object sender) => sender is System.Windows.Controls.MenuItem item &&
        System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(item) is System.Windows.Controls.ContextMenu menu ? menu.PlacementTarget?.GetValue(DataContextProperty) : null;
    private void RenameListClick(object sender,RoutedEventArgs e)
    {
        if(MenuTarget(sender) is NavEntry entry) ShowRename("重命名清单",entry.Title,24,name=>ViewModel.RenameList(entry,name));
    }
    private void RenameStepClick(object sender,RoutedEventArgs e)
    {
        if(MenuTarget(sender) is TaskStep step) ShowRename("重命名步骤",step.Title,80,name=>ViewModel.RenameStep(step,name));
    }
    private void ShowRename(string title,string original,int limit,Func<string,bool> apply)
    {
        var dialog=new Window {FontFamily=FontFamily,Foreground=Foreground,Title=title,Owner=this,Width=360,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Background=(Brush)FindResource("PaneFill")};
        var panel=new System.Windows.Controls.StackPanel {Margin=new Thickness(20)};
        var input=new System.Windows.Controls.TextBox {Text=original,MaxLength=limit,Padding=new Thickness(8),FontSize=14};
        var feedback=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8),Foreground=Brushes.Firebrick};
        var buttons=new System.Windows.Controls.StackPanel {Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button {Content="取消",IsCancel=true,Padding=new Thickness(14,6,14,6),Margin=new Thickness(0,0,8,0)};
        var save=new Button {Content="确定",IsDefault=true,Padding=new Thickness(14,6,14,6)};
        save.Click+=(_,_)=>{if(apply(input.Text))dialog.DialogResult=true;else feedback.Text=ViewModel.Status;};
        buttons.Children.Add(cancel);buttons.Children.Add(save);panel.Children.Add(input);panel.Children.Add(feedback);panel.Children.Add(buttons);dialog.Content=panel;
        dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};dialog.ShowDialog();
    }

    private void InitializeAccessibleControls()
    {
        UpdateTitleBarNames();
        foreach(var picker in new[]{DueDatePicker,NewTaskDatePicker})
        {
            picker.ApplyTemplate();
            if(picker.Template.FindName("PART_Button",picker) is Button calendar)
            {
                calendar.Content=new FontIcon { FontFamily=TaskDesk.Controls.AppIcons.Family, Glyph=TaskDesk.Controls.AppIcons.Planned, FontSize=16 };
                NameControl(calendar,picker==NewTaskDatePicker?"打开新任务截止日期日历":"打开截止日期日历");
            }
            if(picker.Template.FindName("PART_TextBox",picker) is DatePickerTextBox editor)
            {
                // DatePickerTextBox also inherits the library's rectangular focus adorner.
                editor.FocusVisualStyle=null;
                editor.PreviewTextInput+=(_,_)=>ClearDateFeedback(picker);
                editor.PreviewKeyDown+=(_,e)=>
                {
                    if(e.Key is Key.Back or Key.Delete)ClearDateFeedback(picker);
                };
                DataObject.AddPastingHandler(editor,(_,_)=>ClearDateFeedback(picker));
            }
        }
    }
    private void UpdateTitleBarNames()
    {
        foreach(var button in Descendants(WindowTitle).Concat(Descendants(MiniTitle)).OfType<Button>())
        {
            var id=AutomationProperties.GetAutomationId(button);
            if(id=="TitleBarMinimizeButton"||button.Name=="PART_MinimizeButton")NameControl(button,"最小化窗口");
            else if(id=="TitleBarMaximizeButton"||button.Name=="PART_MaximizeButton")NameControl(button,WindowState==WindowState.Maximized?"还原窗口":"最大化窗口");
            else if(id=="TitleBarCloseButton"||button.Name=="PART_CloseButton")NameControl(button,"关闭窗口");
        }
    }
    private static void NameControl(FrameworkElement control,string name)
    {AutomationProperties.SetName(control,name);control.ToolTip=name;}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    private TextBlock DateFeedback(DatePicker picker)=>picker==NewTaskDatePicker?NewTaskDateFeedback:DueDateFeedback;
    private void ClearDateFeedback(DatePicker picker)
    {
        // Selection events can arrive during InitializeComponent before the feedback elements exist.
        var feedback=DateFeedback(picker);
        if(feedback is not null)feedback.Text="";
        AutomationProperties.SetHelpText(picker,"");
        if(picker.Template?.FindName("PART_TextBox",picker) is DatePickerTextBox editor)
            AutomationProperties.SetHelpText(editor,"");
    }
    private void DateValidationError(object? sender,System.Windows.Controls.DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException=false;
        if(sender is not DatePicker picker)return;
        var previous=picker.SelectedDate is { } date?date.ToString("d",CultureInfo.CurrentCulture):"空白";
        var message=$"日期无效，已恢复为{previous}。请输入有效日期或清空。";
        DateFeedback(picker).Text=message;
        AutomationProperties.SetHelpText(picker,message);
        if(picker.Template.FindName("PART_TextBox",picker) is DatePickerTextBox editor)
            AutomationProperties.SetHelpText(editor,message);
    }
    private void ClearDueDateFeedback(object sender,RoutedEventArgs e)=>ClearDateFeedback(DueDatePicker);
    private void DateSelectionChanged(object? sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {if(sender is DatePicker picker)ClearDateFeedback(picker);}
    private void WindowKeyDown(object sender,KeyEventArgs e)
    {
        e.Handled=HandleShortcut(e.Key,Keyboard.Modifiers,e.IsRepeat)||e.Handled;
    }
    private bool HandleShortcut(Key key,ModifierKeys modifiers,bool repeat)
    {
        if(modifiers==ModifierKeys.Control&&key==Key.M)
        {
            if(!repeat)SetMiniMode(!IsMiniMode);
            return true;
        }
        // Holding a shortcut must not repeatedly select text while the user starts typing.
        if(modifiers==ModifierKeys.Control)
        {
            if(key is not (Key.N or Key.K or Key.S))return false;
            if(repeat)return true;
            if(key==Key.N)ShowTaskComposer();
            else if(key==Key.K){if(IsMiniMode)SetMiniMode(false);ViewModel.IsAddingTask=false;SearchBox.Focus();SearchBox.SelectAll();}
            else ViewModel.SaveCommand.Execute(null);
            return true;
        }
        if(key==Key.Escape){if(IsMiniMode)SetMiniMode(false);else if(ViewModel.IsAddingTask)ViewModel.IsAddingTask=false;else ViewModel.CloseDetailsCommand.Execute(null);return true;}
        return false;
    }
    private void WindowClosing(object? sender,CancelEventArgs e)
    {
        if(!preview&&!ViewModel.Flush())
        {
            var result=System.Windows.MessageBox.Show(this,"本次修改尚未保存。是否仍要退出？选择“否”可返回重试。","Tally",System.Windows.MessageBoxButton.YesNo,MessageBoxImage.Warning,System.Windows.MessageBoxResult.No);
            if(result!=System.Windows.MessageBoxResult.Yes){e.Cancel=true;return;}
        }
        MiniOpacityPopup.IsOpen=false;
        if(!preview&&opacitySaveTimer?.IsEnabled==true)
        {
            opacitySaveTimer.Stop();
            try{SaveAppearance();}catch(Exception error)when(error is IOException or UnauthorizedAccessException)
            {ViewModel.Status="透明度偏好未能保存："+error.Message;}
        }
        if(IsMiniMode)RememberMiniBounds();
        ViewModel.Dispose();
    }
    public void ConfigurePreview(string output)
    {
        preview=true;
        Loaded+=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>
        {
            try
            {
                UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)AppRoot.ActualWidth,(int)AppRoot.ActualHeight,96,96,PixelFormats.Pbgra32);
                bitmap.Render(AppRoot);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=File.Create(output);encoder.Save(stream);
            }
            catch(Exception ex){File.WriteAllText(output+".error.txt",ex.ToString());}
            Close();
        });
    }
}

public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is true?Visibility.Collapsed:Visibility.Visible;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
