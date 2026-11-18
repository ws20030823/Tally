using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

// In-process WPF integration checks: these do not claim to inject physical mouse/keyboard input.
internal static class UiAuditChecks
{
    private static string root="";
    private static readonly List<object> results=[];
    private static readonly List<object> metrics=[];
    private static int failed;
    private static string[] casePrefixes=[];
    public static int Run(string directory,string? cases=null)
    {
        casePrefixes=cases?.Split(',',StringSplitOptions.RemoveEmptyEntries)??[];
        root=Path.Combine(Path.GetFullPath(directory),"controls-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(root);
        var app=new TestApp();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Case("UI01 empty state and named editors",0,(w,vm)=>{
            Require(Texts(w).Contains("还没有任务")&&!Named<TextBox>(w,"任务标题").IsVisible,"empty and details placeholders");
            foreach(var name in new[]{"新任务标题","搜索任务","新清单名称"})Require(Named<TextBox>(w,name).IsVisible,"visible "+name);
            Capture(w,"empty");
        });
        Case("UI02 quick add button and selection binding",0,(w,vm)=>{
            Edit((TextBox)w.FindName("NewTaskBox"),"界面新增 😀");Click(All<Button>(w).Single(b=>ReferenceEquals(b.Command,vm.AddTaskCommand)));Pump(50);
            Require(vm.Tasks.Count==1&&vm.SelectedTask?.Title=="界面新增 😀","add from control");
            Require(((ListView)w.FindName("TaskList")).SelectedItem==vm.SelectedTask,"selected item binding");
            Require(Named<TextBox>(w,"任务标题").Text=="界面新增 😀","detail title binding");
        });
        Case("UI03 details text checkbox and save button",2,(w,vm)=>{
            Edit(Named<TextBox>(w,"任务标题"),"界面修改");Edit(Named<TextBox>(w,"任务备注"),"多行备注\n第二行");
            var priority=Named<ComboBox>(w,"优先级");priority.SetCurrentValue(Selector.SelectedIndexProperty,0);
            Click(ContentButton(w,"立即保存 / 重试"));Pump(30);
            Require(vm.SelectedTask?.Title=="界面修改"&&vm.SelectedTask.Notes.Contains("第二行")&&vm.SelectedTask.Priority==0,"details saved from bindings");
            Require(vm.SaveState=="所有修改已保存",vm.SaveState);
        });
        Case("UI04 blank editor must not report all saved",1,(w,vm)=>{
            Edit(Named<TextBox>(w,"任务标题")," ");Pump(750);Capture(w,"blank-title-autosave");
            Require(!(vm.SaveState=="所有修改已保存"&&string.IsNullOrWhiteSpace(Named<TextBox>(w,"任务标题").Text)),"blank title remains in editor while SaveState says all changes saved; model="+vm.SelectedTask?.Title+", status="+vm.Status);
        });
        Case("UI05 search bound control and empty recovery",3,(w,vm)=>{
            var search=(TextBox)w.FindName("SearchBox");Edit(search,"完全不匹配");Pump(20);
            Require(vm.VisibleCount==0&&Texts(w).Contains("没有匹配的任务"),"no results placeholder");
            Edit(search,"UI任务 1");Pump(20);Require(vm.VisibleCount==1,"search match");
            Edit(search,"");Pump(20);Require(vm.VisibleCount==3,"clear search");
        });
        Case("UI06 completion checkbox and counter refresh",3,(w,vm)=>{
            var task=vm.SelectedTask!;var box=All<CheckBox>(w).Single(c=>AutomationProperties.GetName(c)=="完成或恢复任务："+task.Title);
            Click(box);Pump(80);Require(task.IsCompleted&&task.CompletedAt.HasValue,"complete through checkbox");
            Require(vm.CompletedCount==1&&vm.Progress>0,"counter and progress");
            ((ListView)w.FindName("TaskList")).ScrollIntoView(task);Pump(100);w.UpdateLayout();
            box=All<CheckBox>(w).Single(c=>AutomationProperties.GetName(c)=="完成或恢复任务："+task.Title);
            Click(box);Pump(80);Require(!task.IsCompleted,"restore through fresh checkbox after view refresh");
        });
        Case("UI07 navigation buttons and custom list",2,(w,vm)=>{
            Edit(Named<TextBox>(w,"新清单名称"),"UI自定义");Click(Named<Button>(w,"创建清单"));Pump(30);
            Require(vm.CurrentView=="list:UI自定义"&&vm.VisibleCount==0,"new list selected");
            Click(Named<Button>(w,"全部任务"));Pump(30);Require(vm.VisibleCount==2,"navigate all via button");
            Click(Named<Button>(w,"已完成"));Pump(30);Require(vm.DateModeIndex==1&&vm.VisibleCount==0,"done default mode");
        });
        Case("UI08 step editor button and checkbox",1,(w,vm)=>{
            Edit(Named<TextBox>(w,"新步骤"),"新增步骤");Click(Named<Button>(w,"添加步骤"));Pump(30);
            Require(vm.SelectedTask!.Steps.Single().Title=="新增步骤","step button");
            var box=All<CheckBox>(w).Single(c=>c.Content is TextBlock tb&&tb.Text=="新增步骤");Click(box);Pump(30);
            Require(vm.SelectedTask.StepsSummary=="1 / 1","step progress binding");
        });
        Case("UI09 valid date picker and clear",1,(w,vm)=>{
            var picker=(DatePicker)w.FindName("DueDatePicker");picker.SetCurrentValue(DatePicker.SelectedDateProperty,DateTime.Today.AddDays(7));Pump(20);
            Require(vm.EditDate==DateTime.Today.AddDays(7),"selected date binding");Click(Named<Button>(w,"清除截止日期"));Pump(20);Require(vm.EditDate==null&&picker.SelectedDate==null,"clear date button");
        });
        Case("UI10 invalid date has accessible validation feedback",1,(w,vm)=>{
            var picker=(DatePicker)w.FindName("DueDatePicker");var original=picker.SelectedDate;bool invalid=false;
            picker.DateValidationError+=(_,e)=>{invalid=true;e.ThrowException=false;};picker.SetCurrentValue(DatePicker.TextProperty,"2026/99/99");
            typeof(DatePicker).GetMethod("SetSelectedDate",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(picker,null);Pump(30);Capture(w,"invalid-date");
            Require(invalid,"invalid date event raised");
            var hasFeedback=Validation.GetHasError(picker)||!string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(picker))||vm.Status.Contains("日期");
            Require(hasFeedback,"invalid date rejected silently: no validation error, help text or status feedback; text="+picker.Text+", original="+original);
        });
        Case("UI11 independent scroll and return to selected",100,(w,vm)=>{
            var list=(ListView)w.FindName("TaskList");var last=vm.TasksView.Cast<TaskItem>().Last();list.ScrollIntoView(last);Pump(250);w.UpdateLayout();Pump(100);
            var rows=All<ListViewItem>(list).Select(r=>(r.DataContext as TaskItem)?.Title).ToArray();
            var scroll=All<ScrollViewer>(list).First();metrics.Add(new{kind="scrollLast",last=last.Title,rows,offset=scroll.VerticalOffset,extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight});
            Capture(w,"scroll-last-before-selection");
            // Grouped virtualization can defer ScrollIntoView. Also exercise the actual scroll viewer end action.
            scroll.ScrollToEnd();Pump(200);w.UpdateLayout();Pump(100);
            Require(All<ListViewItem>(list).Any(r=>r.DataContext==last),"scroll viewer end realizes final task");
            list.SetCurrentValue(Selector.SelectedItemProperty,last);Pump(40);Require(vm.SelectedTask==last,"selection after scroll");
            var details=Named<TextBox>(w,"任务备注");details.BringIntoView();Pump(30);Require(vm.SelectedTask==last,"detail scrolling retains selection");Capture(w,"scroll-last");
        });
        Case("UI12 grouping expand collapse preserves tasks",8,(w,vm)=>{
            var expander=All<Expander>(w).First(e=>e.DataContext is CollectionViewGroup);var group=(CollectionViewGroup)expander.DataContext;
            expander.SetCurrentValue(Expander.IsExpandedProperty,false);Pump(30);
            Require(!((TaskDateGroup)group.Name).IsExpanded&&vm.Tasks.Count==8,"collapse binding");
            expander.SetCurrentValue(Expander.IsExpandedProperty,true);Pump(30);Require(((TaskDateGroup)group.Name).IsExpanded,"expand binding");
        });
        Case("UI13 dropdowns expose accessible selected values",2,(w,vm)=>{
            foreach(var name in new[]{"排序方式","日期分组依据","优先级","所属清单"}) {
                var combo=Named<ComboBox>(w,name);var peer=UIElementAutomationPeer.CreatePeerForElement(combo)!;
                metrics.Add(new{kind="combo",name,peerName=peer.GetName(),selected=combo.SelectedItem?.ToString(),items=combo.Items.Count});
                Require(peer.GetName()==name&&combo.Items.Count>0,"accessible combo "+name);
            }
        });
        Case("UI14 editor constraints and enter bindings",1,(w,vm)=>{
            foreach(var (name,max) in new[]{("新任务标题",100),("任务标题",100),("任务备注",4000),("新步骤",80),("新清单名称",24)})Require(Named<TextBox>(w,name).MaxLength==max,"MaxLength "+name);
            foreach(var name in new[]{"新任务标题","新步骤","新清单名称"})Require(Named<TextBox>(w,name).InputBindings.OfType<KeyBinding>().Any(k=>k.Key==Key.Return&&k.Command!=null),"enter command configured "+name);
        });
        Case("UI15 keyboard tab stop audit",3,(w,vm)=>{
            var controls=All<Control>(w).Where(c=>c.IsVisible&&c.IsEnabled&&c.Focusable&&c.IsTabStop).Select(c=>new{type=c.GetType().Name,name=AutomationProperties.GetName(c),content=(c as ContentControl)?.Content?.ToString(),tabIndex=c.TabIndex}).ToArray();
            metrics.Add(new{kind="tabStops",controls});Require(controls.Any(c=>c.name=="搜索任务")&&controls.Any(c=>c.name=="新任务标题"),"main editors reachable in tab configuration");
            var unnamed=All<Button>(w).Where(b=>b.IsVisible&&b.IsEnabled&&UIElementAutomationPeer.CreatePeerForElement(b)?.GetName()=="").Select(b=>new{type=b.GetType().Name,id=b.Name,automationId=AutomationProperties.GetAutomationId(b),tooltip=b.ToolTip?.ToString(),content=b.Content?.ToString()}).ToArray();
            metrics.Add(new{kind="unnamedButtons",buttons=unnamed});Require(unnamed.Length==0,"unnamed visible buttons="+unnamed.Length);
        });
        foreach(var size in new[]{(1280,850),(980,660),(1600,900)})Case($"UI16 layout {size}",20,(w,vm)=>{
            w.Width=size.Item1;w.Height=size.Item2;Pump(80);w.UpdateLayout();Capture(w,$"layout-{size.Item1}-{size.Item2}");
            var list=(ListView)w.FindName("TaskList");var item=list.ItemContainerGenerator.ContainerFromItem(vm.SelectedTask!) as FrameworkElement;
            Require(list.ActualWidth>100&&list.ActualHeight>50,"usable task viewport");
            if(size.Item2==660)Require(list.ActualHeight>=230,"minimum window retains at least 230 DIP list height");
            Require(item==null||item.ActualWidth<=list.ActualWidth+2,"row clipped horizontally");
            var details=Named<TextBox>(w,"任务标题");Require(details.ActualWidth>100,"usable detail editor");
            metrics.Add(new{kind="layout",width=w.Width,height=w.Height,listWidth=list.ActualWidth,listHeight=list.ActualHeight,rowWidth=item?.ActualWidth,detailWidth=details.ActualWidth});
        });
        Case("UI17 long content layout",1,(w,vm)=>{
            Edit(Named<TextBox>(w,"任务标题"),new string('长',100));Edit(Named<TextBox>(w,"任务备注"),string.Join("\n",Enumerable.Repeat(new string('备',39),100)));
            Click(ContentButton(w,"立即保存 / 重试"));Pump(100);w.Width=980;w.Height=660;Pump(80);w.UpdateLayout();Capture(w,"long-content-minimum");
            var list=(ListView)w.FindName("TaskList");var item=(FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(vm.SelectedTask!);
            Require(item.ActualWidth<=list.ActualWidth+2,"long title expands row beyond viewport");Require(vm.SelectedTask!.Title.Length==100,"long title retained");
        });
        Case("UI18 Escape routed event commits and closes details",1,(w,vm)=>{
            var title=Named<TextBox>(w,"任务标题");Edit(title,"Esc前编辑");var task=vm.SelectedTask!;
            var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            title.RaiseEvent(e);Pump(30);Require(vm.SelectedTask==null&&task.Title=="Esc前编辑","Escape routed handler closes and commits");Require(!title.IsVisible,"details collapse after Escape");
        });
        Case("UI19 dropdown open selection and close",3,(w,vm)=>{
            var sort=Named<ComboBox>(w,"排序方式");sort.SetCurrentValue(ComboBox.IsDropDownOpenProperty,true);Pump(50);Require(sort.IsDropDownOpen,"sort popup open");
            sort.SetCurrentValue(Selector.SelectedIndexProperty,2);sort.SetCurrentValue(ComboBox.IsDropDownOpenProperty,false);Pump(30);Require(vm.SortIndex==2,"sort selection binding");
            var priority=Named<ComboBox>(w,"优先级");priority.SetCurrentValue(Selector.SelectedIndexProperty,0);Pump(30);Require(vm.EditPriority==0,"priority selection binding");
        });
        Case("UI20 calendar popup opens and renders",1,(w,vm)=>{
            var picker=(DatePicker)w.FindName("DueDatePicker");picker.SetCurrentValue(DatePicker.IsDropDownOpenProperty,true);Pump(100);
            Require(picker.IsDropDownOpen,"calendar open");var popup=(Popup)picker.Template.FindName("PART_Popup",picker);var child=(FrameworkElement)popup.Child;
            Require(child.ActualWidth>100&&child.ActualHeight>100,"calendar has usable size");
            // Render the popup presentation root so child offsets/shadows do not crop its calendar.
            var popupRoot=PresentationSource.FromVisual(child)?.RootVisual as FrameworkElement;
            CaptureElement(popupRoot??child,"calendar-popup");
            picker.SetCurrentValue(DatePicker.IsDropDownOpenProperty,false);Pump(20);Require(!picker.IsDropDownOpen,"calendar close");
        });
        Case("UI21 shortcut focus and held-key repeat",1,(w,vm)=>{
            var method=typeof(TaskDesk.MainWindow).GetMethod("HandleShortcut",BindingFlags.Instance|BindingFlags.NonPublic)!;
            bool Shortcut(Key key,bool repeat=false)=>(bool)method.Invoke(w,[key,ModifierKeys.Control,repeat])!;
            var add=(TextBox)w.FindName("NewTaskBox");var search=(TextBox)w.FindName("SearchBox");
            Require(Shortcut(Key.N),"new shortcut handled");Pump(30);
            Require(add.IsKeyboardFocused&&add.FocusVisualStyle==null,"new task has glass focus without rectangular adorner");
            var glass=(Border)w.FindName("QuickAddSurface");Require(glass.CornerRadius.TopLeft>=12&&glass.Background!=null&&add.IsKeyboardFocusWithin,"focused quick-add retains rounded glass surface");
            Require(Shortcut(Key.K),"search shortcut handled");Pump(30);
            Require(search.IsKeyboardFocused&&search.FocusVisualStyle==null,"search has glass focus without rectangular adorner");
            Edit(search,"UI任务");search.Select(2,0);
            for(var i=0;i<20;i++)Require(Shortcut(Key.K,true),"repeat consumed");
            Require(search.SelectionLength==0&&search.SelectionStart==2,"held shortcut does not repeatedly select text");
            Capture(w,"shortcut-search-glass");
        });
        Case("UI22 title restoration and validation clearing",2,(w,vm)=>{
            var title=Named<TextBox>(w,"任务标题");var original=vm.SelectedTask!.Title;
            Edit(title,"   ");Edit(Named<TextBox>(w,"任务备注"),"其他字段继续保存");Click(ContentButton(w,"立即保存 / 重试"));Pump(30);
            Require(title.Text==original&&vm.HasTitleFeedback,"blank title restored with inline explanation");
            Require(AutomationProperties.GetHelpText(title).Contains("恢复原标题"),"title explanation accessible");
            Require(vm.SelectedTask.Notes=="其他字段继续保存","other valid fields saved");
            Edit(title,"修正后的标题");Require(!vm.HasTitleFeedback,"title correction clears feedback");
            Click(ContentButton(w,"立即保存 / 重试"));Require(vm.SelectedTask.Title=="修正后的标题","corrected title saved");
            Edit(title,"");vm.CloseDetailsCommand.Execute(null);Require(vm.SelectedTask==null,"close uses same restoration policy");
        });
        Case("UI23 both date editors recover with accessible feedback",1,(w,vm)=>{
            foreach(var name in new[]{"DueDatePicker","HistoryDatePicker"})
            {
                var picker=(DatePicker)w.FindName(name);var selected=picker.SelectedDate;
                picker.SetCurrentValue(DatePicker.TextProperty,"2026/99/99");
                typeof(DatePicker).GetMethod("SetSelectedDate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(picker,null);Pump(30);
                Require(picker.SelectedDate==selected&&AutomationProperties.GetHelpText(picker).Contains("已恢复"),"invalid date explained: "+name);
                var feedback=(TextBlock)w.FindName(name=="DueDatePicker"?"DueDateFeedback":"HistoryDateFeedback");
                Require(feedback.IsVisible&&feedback.Text.Contains("日期无效"),"inline date feedback: "+name);
                picker.SetCurrentValue(DatePicker.SelectedDateProperty,DateTime.Today.AddDays(3));Pump(20);
                Require(feedback.Text==""&&AutomationProperties.GetHelpText(picker)=="","valid correction clears feedback: "+name);
                picker.SetCurrentValue(DatePicker.SelectedDateProperty,null);Pump(20);
                Require(feedback.Text==""&&picker.SelectedDate==null,"blank date allowed: "+name);
            }
        });
        Case("UI24 priority and deadline badges at minimum size",3,(w,vm)=>{
            vm.CloseDetailsCommand.Execute(null);
            var tasks=vm.Tasks.ToArray();tasks[0].Priority=0;tasks[0].DueDate=DateTime.Today.AddDays(-2);
            tasks[1].Priority=1;tasks[1].DueDate=DateTime.Today;tasks[2].Priority=2;tasks[2].DueDate=DateTime.Today.AddDays(1);
            vm.SelectedTask=tasks[1];vm.SelectedTask=tasks[0];Pump(80);
            w.Width=980;w.Height=660;Pump(50);w.UpdateLayout();Capture(w,"priority-deadline-minimum");
            Require(Texts(w).Contains("高优先级")&&Texts(w).Contains("逾期 2 天"),"high priority and overdue text visible");
            var red=All<TextBlock>(w).First(t=>t.IsVisible&&t.Text=="逾期 2 天");
            Require(red.Foreground is SolidColorBrush brush&&brush.Color.R>brush.Color.G,"overdue text is red");
            var expanded=((ListView)w.FindName("TaskList")).ActualWidth;
            vm.CloseDetailsCommand.Execute(null);Pump(50);w.UpdateLayout();
            Require(((ListView)w.FindName("TaskList")).ActualWidth>=expanded+300,"closing details returns space to task list");
            Capture(w,"priority-deadline-expanded");
        });
        Case("UI25 empty search recovery buttons",2,(w,vm)=>{
            Edit((TextBox)w.FindName("SearchBox"),"找不到的任务");Pump(30);
            Click(ContentButton(w,"清除搜索"));Pump(30);Require(vm.VisibleCount==2&&!vm.HasSearch,"clear search from empty state");
            vm.CurrentView="important";vm.Search="UI任务";Pump(30);
            Click(ContentButton(w,"搜索全部任务"));Pump(30);Require(vm.CurrentView=="all"&&vm.VisibleCount==2&&vm.Search=="UI任务","global search retains query and clears view scope");
        });
        Case("UI26 planned date explicitly selected",0,(w,vm)=>{
            vm.CurrentView="planned";Pump(30);
            var picker=(DatePicker)w.FindName("NewTaskDatePicker");Require(picker.IsVisible,"planned add date picker visible");
            picker.SetCurrentValue(DatePicker.SelectedDateProperty,DateTime.Today.AddDays(6));
            Edit((TextBox)w.FindName("NewTaskBox"),"带明确日期的任务");Click(All<Button>(w).Single(b=>ReferenceEquals(b.Command,vm.AddTaskCommand)));Pump(30);
            Require(vm.SelectedTask?.DueDate==DateTime.Today.AddDays(6),"new date picker binding used by add command");
        });
        Case("UI27 yesterday review and selective carryover",1,(w,vm)=>{
            var task=vm.SelectedTask!;vm.CloseDetailsCommand.Execute(null);task.TodayDate=DateTime.Today.AddDays(-1);Pump(60);
            Require(Texts(w).Contains("昨天还有 1 项未完成"),"yesterday prompt visible");
            Click(ContentButton(w,"查看"));Pump(30);vm.SelectedTask=task;Pump(30);
            Require(vm.ReviewingYesterday&&vm.VisibleCount==1,"review shows yesterday task");
            Click(ContentButton(w,"加入今天"));Pump(30);
            Require(task.TodayDate==DateTime.Today&&task.DueDate==DateTime.Today,"carryover preserves original deadline");
            Click(ContentButton(w,"返回今天"));Pump(30);Require(vm.VisibleCount==1,"return to today's task");
        });
        app.Shutdown();File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(Path.Combine(root,"metrics.json"),JsonSerializer.Serialize(metrics,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"UI AUDIT cases={results.Count}, failed={failed}, evidence={root}");return failed==0?0:2;
    }
    private static void Case(string name,int count,Action<TaskDesk.MainWindow,MainViewModel> check)
    {
        if(casePrefixes.Length>0&&!casePrefixes.Any(prefix=>name.StartsWith(prefix+" ",StringComparison.Ordinal)))return;
        TaskDesk.MainWindow? w=null;MainViewModel? vm=null;var sw=Stopwatch.StartNew();
        try {
            var store=new DataStore(Path.Combine(root,Guid.NewGuid().ToString("N")));store.Save(new(){Tasks=Enumerable.Range(0,count).Select(i=>new TaskItem{Title="UI任务 "+i,List="默认清单",CreatedAt=DateTime.Now.AddDays(-i%3),TodayDate=DateTime.Today,DueDate=DateTime.Today,Notes="初始备注"}).ToList()});
            vm=new MainViewModel(store);w=new TaskDesk.MainWindow(vm);w.Show();Pump(50);w.UpdateLayout();
            check(w,vm);results.Add(new{name,passed=true,ms=sw.Elapsed.TotalMilliseconds});Console.WriteLine("PASS "+name);
        } catch(Exception ex){failed++;results.Add(new{name,passed=false,ms=sw.Elapsed.TotalMilliseconds,error=ex.ToString()});Console.WriteLine("FAIL "+name+": "+ex.Message);}
        finally {if(w!=null){w.ConfigurePreview(Path.Combine(root,"unused.png"));w.Close();}vm?.Dispose();}
    }
    private static IEnumerable<T> All<T>(DependencyObject o) where T:DependencyObject {if(o is T t)yield return t;for(var i=0;i<VisualTreeHelper.GetChildrenCount(o);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(o,i)))yield return item;}
    private static T Named<T>(DependencyObject w,string name) where T:DependencyObject=>All<T>(w).First(x=>AutomationProperties.GetName(x)==name);
    private static Button ContentButton(DependencyObject w,string content)=>All<Button>(w).First(b=>b.Content?.ToString()==content);
    private static IEnumerable<string> Texts(DependencyObject w)=>All<TextBlock>(w).Where(t=>t.IsVisible).Select(t=>t.Text);
    private static void Edit(TextBox box,string value){box.SetCurrentValue(TextBox.TextProperty,value);box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();}
    private static void Click(ButtonBase button)=>typeof(ButtonBase).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(button,null);
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Pump(int ms){var f=new DispatcherFrame();var t=new DispatcherTimer(TimeSpan.FromMilliseconds(ms),DispatcherPriority.Background,(_,_)=>f.Continue=false,Dispatcher.CurrentDispatcher);Dispatcher.PushFrame(f);t.Stop();}
    private static void Capture(TaskDesk.MainWindow w,string name){w.UpdateLayout();CaptureElement((FrameworkElement)w.FindName("AppRoot"),name);}
    private static void CaptureElement(FrameworkElement el,string name){var bmp=new RenderTargetBitmap((int)el.ActualWidth,(int)el.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(el);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(root,name+".png"));enc.Save(f);}
}
