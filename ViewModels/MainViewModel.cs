using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.Controls;

namespace TaskDesk.ViewModels;

public partial class NavEntry(string key, string title, string icon) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    [ObservableProperty] private int count;
    [ObservableProperty] private bool isActive;
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DataStore store;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer clock;
    private readonly TaskDateGroupDescription dateGrouping;
    private DateTime day = DateTime.Today;
    private bool loading, refreshPending, disposed;
    private TaskItem? deleted;
    private int deletedIndex;
    public ObservableCollection<TaskItem> Tasks { get; }
    public ObservableCollection<string> Lists { get; }
    public ObservableCollection<NavEntry> Navigation { get; } = [
        new("day","今天",AppIcons.Today), new("important","重要任务",AppIcons.Important),
        new("planned","计划任务",AppIcons.Planned), new("all","全部任务",AppIcons.AllTasks), new("done","已完成",AppIcons.Completed)
    ];
    public ObservableCollection<NavEntry> CustomLists { get; } = [];
    public ListCollectionView TasksView { get; }
    public ListCollectionView MiniTasksView { get; }
    public ObservableCollection<NavEntry> MiniScopes { get; } = [];
    [ObservableProperty] private string miniScopeKey = "day";
    public bool MiniHasTasks => !MiniTasksView.IsEmpty;
    public string MiniCountLabel => $"{MiniTasksView.Count} 项待办";
    public string MiniEmptyHint => MiniScopeKey == "day" ? "今天没有待办，可切换到全部任务" : "这个清单的待办已全部完成";
    public string DataPath => store.FilePath;
    public string[] SortOptions { get; } = ["默认排序", "优先级排序", "截止日期排序"];
    public string[] PriorityOptions { get; } = ["高优先级", "中优先级", "低优先级"];
    public string[] DateModeOptions { get; } = ["按创建日期", "按完成日期"];
    [ObservableProperty] private int dateModeIndex;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDateFilter))] private DateTime? historyDate;
    public bool HasDateFilter => HistoryDate.HasValue;
    public string DateScopeLabel => DateModeIndex == 1 ? "按完成日期 · 仅已完成" : SortIndex == 1 ? "按优先级" : SortIndex == 2 ? "按截止日期" : "按创建日期";
    [ObservableProperty] private string currentView = "day";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private string newListName = "";
    [ObservableProperty] private string newStepTitle = "";
    [ObservableProperty] private DateTime? newTaskDate;
    [ObservableProperty] private bool reviewingYesterday;
    public bool IsPlannedView => CurrentView == "planned";
    public int YesterdayCount => Tasks.Count(t => !t.IsCompleted && t.TodayDate?.Date == DateTime.Today.AddDays(-1));
    public bool ShowYesterdayBanner => CurrentView == "day" && (YesterdayCount > 0 || ReviewingYesterday);
    public string YesterdayLabel => ReviewingYesterday ? "正在查看昨天未完成的任务" : $"昨天还有 {YesterdayCount} 项未完成";
    public string YesterdayAction => ReviewingYesterday ? "返回今天" : "查看";
    public string EmptyTitle => !string.IsNullOrWhiteSpace(Search) ? "没有匹配的任务" : HasDateFilter ? "这一天没有记录" : ReviewingYesterday ? "昨天的任务已处理完" : "还没有任务";
    public string EmptyHint => !string.IsNullOrWhiteSpace(Search) ? "搜索仅在当前视图内，可清除搜索或搜索全部任务" : HasDateFilter ? "试试查看全部日期" : ReviewingYesterday ? "可以返回今天，继续安排今天的事情" : "在上方记下第一件事";
    public bool HasSearch => !string.IsNullOrWhiteSpace(Search);
    [ObservableProperty] private TaskItem? selectedTask;
    [ObservableProperty] private string status = "准备就绪";
    [ObservableProperty] private string saveState = "本地保存";
    [ObservableProperty] private string editTitle = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTitleFeedback))] private string titleFeedback = "";
    public bool HasTitleFeedback => !string.IsNullOrEmpty(TitleFeedback);
    partial void OnEditTitleChanged(string value) { if(HasTitleFeedback)Status="有未保存修改"; TitleFeedback = ""; }
    [ObservableProperty] private string editList = "";
    [ObservableProperty] private DateTime? editDate;
    [ObservableProperty] private int editPriority = 1;
    [ObservableProperty] private string editNotes = "";
    [ObservableProperty] private bool editDay;
    [ObservableProperty] private bool canUndo;
    public bool HasSelection => SelectedTask is not null;
    public bool HasTasks => TasksView.Cast<TaskItem>().Any();
    public int VisibleCount => TasksView.Cast<TaskItem>().Count();
    public int CompletedCount => TasksView.Cast<TaskItem>().Count(t => t.IsCompleted);
    public double Progress => VisibleCount == 0 ? 0 : 100.0 * CompletedCount / VisibleCount;
    public string CountLabel => $"{VisibleCount - CompletedCount} 项待办  ·  {CompletedCount} 项完成";
    public string ProgressLabel => $"已完成 {CompletedCount} / {VisibleCount}";
    public string ViewTitle => Navigation.Concat(CustomLists).FirstOrDefault(n => n.Key == CurrentView)?.Title ?? "全部任务";
    public string DateLabel => DateTime.Today.ToString("M月d日 · dddd", CultureInfo.GetCultureInfo("zh-CN"));

    public MainViewModel(DataStore store)
    {
        this.store = store;
        dispatcher = Dispatcher.CurrentDispatcher;
        var data = store.Load();
        Lists = new(data.Lists); Tasks = new(data.Tasks);
        foreach (var list in Lists) CustomLists.Add(new("list:"+list,list,AppIcons.List));
        foreach (var task in Tasks) Attach(task);
        TasksView = new ListCollectionView(Tasks) { Filter = o => Matches((TaskItem)o) };
        MiniTasksView = new ListCollectionView(Tasks) {
            Filter = o => o is TaskItem t && !t.IsCompleted && InView(t,MiniScopeKey ?? "day"),
            CustomSort = new TaskComparer(2,false)
        };
        SyncMiniScopes();
        CustomLists.CollectionChanged += (_,_) => SyncMiniScopes();
        dateGrouping = new TaskDateGroupDescription(() => DateModeIndex == 1);
        TasksView.GroupDescriptions.Add(dateGrouping);
        saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) => { saveTimer!.Stop(); Flush(); }, dispatcher);
        saveTimer.Stop();
        clock = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => CheckDay(), dispatcher);
        PropertyChanged += (_, e) => { if (!loading && e.PropertyName?.StartsWith("Edit", StringComparison.Ordinal) == true) ScheduleSave(); };
        Refresh();
        SelectedTask = TasksView.Cast<TaskItem>().FirstOrDefault();
        Status = store.LoadWarning ?? "已加载任务";
    }

    private bool InView(TaskItem t, string view) => view switch
    { "day" => t.TodayDate?.Date == DateTime.Today, "important" => t.IsImportant, "planned" => t.DueDate.HasValue,
      "done" => t.IsCompleted, "all" => true, _ => "list:"+t.List == view };
    private bool Matches(TaskItem t) => (CurrentView == "day" && ReviewingYesterday ? !t.IsCompleted && t.TodayDate?.Date == DateTime.Today.AddDays(-1) : InView(t,CurrentView)) && (DateModeIndex != 1 || t.IsCompleted) &&
        (!HistoryDate.HasValue || TaskDateGroupDescription.GetDate(t,DateModeIndex == 1) == HistoryDate.Value.Date) && (string.IsNullOrWhiteSpace(Search) ||
        t.Title.Contains(Search,StringComparison.OrdinalIgnoreCase) || (t.Notes ?? string.Empty).Contains(Search,StringComparison.OrdinalIgnoreCase));
    partial void OnCurrentViewChanged(string value) { ReviewingYesterday=false; DateModeIndex = value == "done" ? 1 : 0; HistoryDate = null; Refresh(); OnPropertyChanged(nameof(ViewTitle)); OnPropertyChanged(nameof(IsPlannedView)); }
    partial void OnReviewingYesterdayChanged(bool value) => Refresh();
    partial void OnMiniScopeKeyChanged(string value) => RefreshMini();
    private void SyncMiniScopes()
    {
        var key=MiniScopeKey;
        MiniScopes.Clear();
        foreach(var entry in Navigation.Where(n=>n.Key is "day" or "all" or "important").Concat(CustomLists)) MiniScopes.Add(entry);
        MiniScopeKey=MiniScopes.Any(n=>n.Key==key)?key:"day";
        RefreshMini();
    }
    private void RefreshMini()
    {
        if(MiniTasksView is null)return;
        MiniTasksView.Refresh();
        foreach(var name in new[]{nameof(MiniHasTasks),nameof(MiniCountLabel),nameof(MiniEmptyHint)})OnPropertyChanged(name);
    }
    partial void OnDateModeIndexChanged(int value) { Refresh(); OnPropertyChanged(nameof(DateScopeLabel)); }
    partial void OnHistoryDateChanged(DateTime? value) => Refresh();
    partial void OnSearchChanged(string value) => Refresh();
    partial void OnSortIndexChanged(int value) { Refresh(); OnPropertyChanged(nameof(DateScopeLabel)); }
    partial void OnSelectedTaskChanging(TaskItem? oldValue, TaskItem? newValue) { if (!loading && oldValue is not null) CommitDraft(oldValue); }
    partial void OnSelectedTaskChanged(TaskItem? value)
    {
        loading = true;
        EditTitle=value?.Title??""; EditList=value?.List??Lists[0]; EditDate=value?.DueDate; EditPriority=value?.Priority??1;
        EditNotes=value?.Notes??""; EditDay=value?.TodayDate?.Date==DateTime.Today; NewStepTitle="";
        TitleFeedback=""; loading=false; OnPropertyChanged(nameof(HasSelection));
    }
    private void CommitDraft(TaskItem task)
    {
        if (!string.IsNullOrWhiteSpace(EditTitle)) task.Title=EditTitle.Trim();
        else
        {
            EditTitle=task.Title;
            TitleFeedback="标题不能为空，已恢复原标题；其他修改正常保存。";
            Status=TitleFeedback;
        }
        if (Lists.Contains(EditList)) task.List=EditList;
        task.DueDate=EditDate?.Date; task.Priority=Math.Clamp(EditPriority,0,2); task.Notes=EditNotes;
        task.TodayDate=EditDay?DateTime.Today:null;
    }
    private void Attach(TaskItem t) { t.AttachSteps(); t.PropertyChanged += TaskChanged; }
    private void TaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed) return;
        // Only a real state change after loading records completion; old JSON without a timestamp stays unknown.
        if (sender is TaskItem task && e.PropertyName == nameof(TaskItem.IsCompleted)) task.CompletedAt = task.IsCompleted ? DateTime.Now : null;
        ScheduleSave();
        if (!refreshPending)
        {
            refreshPending=true;
            dispatcher.BeginInvoke(DispatcherPriority.Background, () => { refreshPending=false; if(!disposed) Refresh(); });
        }
    }
    private void ScheduleSave() { if(disposed)return; SaveState="有未保存修改…"; saveTimer.Stop(); saveTimer.Start(); }
    public bool Flush()
    {
        saveTimer.Stop();
        if (SelectedTask is not null) CommitDraft(SelectedTask);
        // Changes made by CommitDraft are part of this write, not another pending write.
        saveTimer.Stop();
        try { store.Save(new(){Lists=Lists.ToList(),Tasks=Tasks.ToList()}); SaveState="所有修改已保存"; return true; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { SaveState="保存失败 · 请重试"; Status="保存失败，编辑仍保留在内存中："+e.Message; return false; }
    }
    private void Refresh()
    {
        var selected=SelectedTask;
        using (TasksView.DeferRefresh()) {
            var groupByDate = SortIndex == 0 || DateModeIndex == 1;
            if(groupByDate && TasksView.GroupDescriptions.Count == 0) TasksView.GroupDescriptions.Add(dateGrouping);
            if(!groupByDate && TasksView.GroupDescriptions.Count > 0) TasksView.GroupDescriptions.Clear();
            TasksView.CustomSort=new TaskComparer(SortIndex,DateModeIndex == 1);
        }
        // Filtered-out tasks close naturally; do not choose a different task on behalf of the user.
        if(selected is not null && !TasksView.Contains(selected)) SelectedTask=null;
        foreach(var n in Navigation.Concat(CustomLists)) { n.IsActive=n.Key==CurrentView; n.Count=Tasks.Count(t=>InView(t,n.Key)&&(n.Key=="done"||!t.IsCompleted)); }
        RefreshMini();
        foreach(var p in new[]{nameof(HasTasks),nameof(VisibleCount),nameof(CompletedCount),nameof(Progress),nameof(CountLabel),nameof(ProgressLabel),nameof(EmptyTitle),nameof(EmptyHint),nameof(HasSearch),nameof(YesterdayCount),nameof(ShowYesterdayBanner),nameof(YesterdayLabel),nameof(YesterdayAction)}) OnPropertyChanged(p);
    }
    public void CheckDay()
    {
        if(day==DateTime.Today)return;
        day=DateTime.Today; OnPropertyChanged(nameof(DateLabel));
        dateGrouping.RefreshLabels();
        foreach(var task in Tasks)task.RefreshDateLabels();
        if(SelectedTask is not null){loading=true;EditDay=SelectedTask.TodayDate?.Date==day;loading=false;}
        Refresh(); Status="新的一天，今日清单已更新";
    }
    [RelayCommand] private void Navigate(NavEntry? entry) { if(entry is null)return; if(SelectedTask is not null)CommitDraft(SelectedTask); CurrentView=entry.Key; }
    [RelayCommand] private void AddTask()
    {
        if(string.IsNullOrWhiteSpace(NewTitle)){Status="请先输入任务标题";return;}
        if(IsPlannedView && !NewTaskDate.HasValue){Status="请先选择截止日期，再添加计划任务";return;}
        if(SelectedTask is not null)CommitDraft(SelectedTask);
        var task=new TaskItem { CreatedAt=DateTime.Now, Title=NewTitle.Trim(), List=CurrentView.StartsWith("list:")?CurrentView[5..]:Lists[0],
            TodayDate=CurrentView=="day"?DateTime.Today:null,DueDate=IsPlannedView?NewTaskDate?.Date:null,
            IsImportant=CurrentView=="important" };
        Attach(task); Tasks.Add(task); NewTitle="";Search="";HistoryDate=null;ReviewingYesterday=false;if(CurrentView=="done")CurrentView="all";DateModeIndex=0;
        Refresh(); SelectedTask=task; ScheduleSave();Status="已添加任务";
    }
    [RelayCommand] private void AddList()
    {
        var name=NewListName.Trim();
        if(name.Length==0){Status="请输入清单名称";return;}
        if(Lists.Contains(name,StringComparer.OrdinalIgnoreCase)){Status="这个清单已经存在";return;}
        Lists.Add(name);var entry=new NavEntry("list:"+name,name,AppIcons.List);CustomLists.Add(entry);
        NewListName="";Navigate(entry);ScheduleSave();Status="已创建清单";
    }
    [RelayCommand] private void DeleteList(NavEntry? entry)
    {
        if(entry is null || !CustomLists.Contains(entry) || !Lists.Contains(entry.Title))return;
        if(Lists.Count<=1){Status="至少保留一个清单；可以先新建其他清单再删除";return;}
        var destination=Lists.First(name=>name!=entry.Title);
        var selected=SelectedTask;
        // Commit the editor before moving tasks, then reload it from the surviving list.
        SelectedTask=null;
        var moved=Tasks.Where(task=>task.List==entry.Title).ToList();
        foreach(var task in moved)task.List=destination;
        // A task waiting for undo must also point to a list that still exists.
        if(deleted?.List==entry.Title)deleted.List=destination;
        Lists.Remove(entry.Title);CustomLists.Remove(entry);
        if(CurrentView==entry.Key)CurrentView="list:"+destination;
        Refresh();OnPropertyChanged(nameof(ViewTitle));
        if(selected is not null && TasksView.Contains(selected))SelectedTask=selected;
        ScheduleSave();
        Status=moved.Count==0?"已删除清单":$"已删除清单，{moved.Count} 项任务已移至“{destination}”";
    }
    [RelayCommand] private void ToggleImportant(TaskItem? task) { if(task is not null)task.IsImportant=!task.IsImportant; }
    [RelayCommand] private void DeleteTask()
    {
        if(SelectedTask is not { } task)return;
        CommitDraft(task); deleted=task;deletedIndex=Tasks.IndexOf(task);SelectedTask=null;
        task.PropertyChanged-=TaskChanged;Tasks.Remove(task);CanUndo=true;Refresh();ScheduleSave();Status="任务已删除，可撤销最近一次删除";
    }
    [RelayCommand] private void UndoDelete()
    {
        if(deleted is null)return;
        var task=deleted;Tasks.Insert(Math.Min(deletedIndex,Tasks.Count),task);Attach(task);deleted=null;CanUndo=false;
        if(!InView(task,CurrentView))CurrentView="all";Search="";HistoryDate=null;if(!task.IsCompleted)DateModeIndex=0;Refresh();SelectedTask=task;ScheduleSave();Status="已恢复任务";
    }
    [RelayCommand] private void AddStep()
    {
        if(SelectedTask is null||string.IsNullOrWhiteSpace(NewStepTitle))return;
        SelectedTask.Steps.Add(new(){Title=NewStepTitle.Trim()});NewStepTitle="";Status="已添加步骤";
    }
    [RelayCommand] private void DeleteStep(TaskStep? step)
    {
        if(step is null || SelectedTask is not { } task || !task.Steps.Remove(step))return;
        ScheduleSave();Status="已删除步骤";
    }
    [RelayCommand] private void Save()
    {
        if(Flush()){Refresh();Status=HasTitleFeedback?TitleFeedback:"修改已保存";}
    }
    [RelayCommand] private void ClearDate() => EditDate=null;
    [RelayCommand] private void ClearHistoryDate() => HistoryDate=null;
    [RelayCommand] private void ClearSearch() => Search="";
    [RelayCommand] private void SearchAll() { CurrentView="all"; HistoryDate=null; DateModeIndex=0; }
    [RelayCommand] private void ToggleYesterday() { Search=""; HistoryDate=null; DateModeIndex=0; ReviewingYesterday=!ReviewingYesterday; }
    [RelayCommand] private void MoveToToday()
    {
        if(SelectedTask is not { } task)return;
        EditDay=true; CommitDraft(task); ScheduleSave(); Refresh(); Status="已加入今天，截止日期保持原样";
    }
    public bool RenameList(NavEntry entry, string name)
    {
        name=name.Trim();
        if(!CustomLists.Contains(entry) || name.Length is 0 or >24){Status="清单名称应为 1–24 个字符";return false;}
        if(name==entry.Title)return true;
        if(Lists.Any(x=>string.Equals(x,name,StringComparison.OrdinalIgnoreCase)&&x!=entry.Title)){Status="这个清单已经存在";return false;}
        var old=entry.Title; var miniWasSelected=MiniScopeKey==entry.Key; var selected=SelectedTask; SelectedTask=null;
        foreach(var task in Tasks.Where(t=>t.List==old))task.List=name;
        if(deleted?.List==old)deleted.List=name;
        Lists[Lists.IndexOf(old)]=name;
        CustomLists[CustomLists.IndexOf(entry)]=new("list:"+name,name,AppIcons.List);
        if(miniWasSelected)MiniScopeKey="list:"+name;
        if(CurrentView==entry.Key)CurrentView="list:"+name;
        Refresh(); if(selected is not null&&TasksView.Contains(selected))SelectedTask=selected;
        ScheduleSave(); Status="已重命名清单"; return true;
    }
    public bool RenameStep(TaskStep step,string name)
    {
        name=name.Trim();
        if(SelectedTask is null || !SelectedTask.Steps.Contains(step) || name.Length is 0 or >80){Status="步骤名称应为 1–80 个字符";return false;}
        step.Title=name; ScheduleSave(); Status="已重命名步骤"; return true;
    }
    [RelayCommand] private void OpenDataFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.DirectoryPath){UseShellExecute=true}); }
        catch(Exception e) when(e is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {Status="无法打开数据文件夹："+e.Message;}
    }
    [RelayCommand] private void CloseDetails() { if(SelectedTask is not null)CommitDraft(SelectedTask);SelectedTask=null; }
    public void Dispose() { disposed=true;saveTimer.Stop();clock.Stop();foreach(var t in Tasks)t.PropertyChanged-=TaskChanged; }

    private sealed class TaskComparer(int mode, bool completion) : IComparer
    {
        public int Compare(object? x,object? y)
        {
            if(x is not TaskItem a||y is not TaskItem b)return 0;
            // Days are newest first; the existing sort applies inside each day.
            var result=mode==0||completion ? Nullable.Compare(TaskDateGroupDescription.GetDate(b,completion),TaskDateGroupDescription.GetDate(a,completion)) : 0;
            if(result==0)result=a.IsCompleted.CompareTo(b.IsCompleted);
            if(result==0&&mode==1)result=a.Priority.CompareTo(b.Priority);
            if(result==0&&mode==2)result=(a.DueDate??DateTime.MaxValue).CompareTo(b.DueDate??DateTime.MaxValue);
            return result!=0?result:Nullable.Compare(completion?b.CompletedAt:b.CreatedAt,completion?a.CompletedAt:a.CreatedAt);
        }
    }
}
