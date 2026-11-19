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
    public Func<TaskItem,bool>? ConfirmTaskDeletion { get; set; }
    public ObservableCollection<TaskItem> Tasks { get; }
    public ObservableCollection<string> Lists { get; }
    public ObservableCollection<NavEntry> Navigation { get; } = [
        new("pending","待办",AppIcons.List), new("done","已完成",AppIcons.Completed), new("all","全部",AppIcons.AllTasks),
        new("yesterday","昨天",AppIcons.Today), new("week","近一周",AppIcons.Planned), new("month","近一月",AppIcons.Planned),
        new("day","今天",AppIcons.Today),
        new("planned","计划任务",AppIcons.Planned)
    ];
    public IEnumerable<NavEntry> StateNavigation => Navigation.Where(n=>n.Key is "pending" or "done" or "all");
    public IEnumerable<NavEntry> TimeNavigation => new[]{"day","yesterday","week","month"}.Select(key=>Navigation.First(n=>n.Key==key));
    public ObservableCollection<NavEntry> CustomLists { get; } = [];
    public ListCollectionView TasksView { get; }
    public ListCollectionView MiniTasksView { get; }
    public ObservableCollection<NavEntry> MiniScopes { get; } = [];
    [ObservableProperty] private string miniScopeKey = "day";
    public bool MiniHasTasks => !MiniTasksView.IsEmpty;
    public string MiniCountLabel => $"待办 {MiniTasksView.Cast<TaskItem>().Count(t=>!t.IsCompleted)} · 完成 {MiniTasksView.Cast<TaskItem>().Count(t=>t.IsCompleted)}";
    public string MiniEmptyHint => MiniScopeKey == "day" ? "在上方记下今天要做的事" : "在上方添加任务，或切换时间范围";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddMiniTaskCommand))] private string miniNewTitle = "";
    public event Action<TaskItem>? MiniTaskAdded;
    public string DataPath => store.FilePath;
    public string[] SortOptions { get; } = ["默认排序", "优先级排序", "截止日期排序"];
    public string[] PriorityOptions { get; } = ["高优先级", "中优先级", "低优先级"];
    public string[] DateModeOptions { get; } = ["按创建日期", "按完成日期"];
    [ObservableProperty] private int dateModeIndex;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDateFilter))] private DateTime? historyDate;
    public bool HasDateFilter => HistoryDate.HasValue;
    public string DateScopeLabel => DateModeIndex == 1 ? "按完成日期 · 仅已完成" : SortIndex == 1 ? "按优先级" : SortIndex == 2 ? "按截止日期" : "按创建日期";
    [ObservableProperty] private string currentView = "pending";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private string newListName = "";
    [ObservableProperty] private string newStepTitle = "";
    [ObservableProperty] private DateTime? newTaskDate;
    [ObservableProperty] private bool isAddingTask;
    [ObservableProperty] private string newTaskList = "";
    [ObservableProperty] private string newTaskNotes = "";
    [ObservableProperty] private int newTaskPriority = 1;
    [ObservableProperty] private string newTaskFeedback = "";
    public ObservableCollection<TaskStep> NewTaskSteps { get; } = [];
    [ObservableProperty] private string newTaskStepTitle = "";
    [RelayCommand] private void AddNewTaskStep()
    {
        if(string.IsNullOrWhiteSpace(NewTaskStepTitle))return;
        if(NewTaskStepTitle.Trim().Length>80){NewTaskFeedback="子任务最多 80 个字符";return;}
        NewTaskSteps.Add(new(){Title=NewTaskStepTitle.Trim()});NewTaskStepTitle="";
    }
    [RelayCommand] private void RemoveNewTaskStep(TaskStep? step) { if(step is not null)NewTaskSteps.Remove(step); }
    private bool hasNewTaskDraft;
    partial void OnNewTitleChanged(string value) => NewTaskFeedback = "";
    partial void OnIsAddingTaskChanged(bool value) => Refresh();
    public bool IsPlannedView => CurrentView == "planned";
    public string EmptyTitle => !string.IsNullOrWhiteSpace(Search) ? "没有匹配的任务" : HasDateFilter ? "这一天没有记录" : "还没有任务";
    public string EmptyHint => !string.IsNullOrWhiteSpace(Search) ? "搜索仅在当前视图内，可清除搜索或搜索全部任务" : HasDateFilter ? "试试查看全部日期" : "点击左侧“添加任务”，记下第一件事";
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
            Filter = o => o is TaskItem t && InView(t,MiniScopeKey ?? "day"),
            CustomSort = new TaskComparer(2,false)
        };
        SyncMiniScopes();
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
    { "pending" => !t.IsCompleted, "day" => TaskDateGroupDescription.GetDate(t,false)==DateTime.Today, "planned" => t.DueDate.HasValue,
      "yesterday" => TaskDateGroupDescription.GetDate(t,false)==DateTime.Today.AddDays(-1),
      "week" => CreatedWithin(t,7), "month" => CreatedWithin(t,30),
      "done" => t.IsCompleted, "all" => true, _ => "list:"+t.List == view };
    private static bool CreatedWithin(TaskItem task,int days) => TaskDateGroupDescription.GetDate(task,false) is { } date &&
        date>=DateTime.Today.AddDays(1-days)&&date<=DateTime.Today;
    private bool Matches(TaskItem t) => InView(t,CurrentView) && (DateModeIndex != 1 || t.IsCompleted) &&
        (!HistoryDate.HasValue || TaskDateGroupDescription.GetDate(t,DateModeIndex == 1) == HistoryDate.Value.Date) && (string.IsNullOrWhiteSpace(Search) ||
        t.Title.Contains(Search,StringComparison.OrdinalIgnoreCase) || (t.Notes ?? string.Empty).Contains(Search,StringComparison.OrdinalIgnoreCase));
    partial void OnCurrentViewChanged(string value) { DateModeIndex = value == "done" ? 1 : 0; HistoryDate = null; Refresh(); OnPropertyChanged(nameof(ViewTitle)); OnPropertyChanged(nameof(IsPlannedView)); }
    partial void OnMiniScopeKeyChanged(string value) => RefreshMini();
    private void SyncMiniScopes()
    {
        var key=MiniScopeKey;
        MiniScopes.Clear();
        foreach(var scope in new[]{"all","day","yesterday","week","month"}) MiniScopes.Add(Navigation.First(n=>n.Key==scope));
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
        EditNotes=value?.Notes??""; NewStepTitle="";
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
        foreach(var n in Navigation.Concat(CustomLists)) { n.IsActive=!IsAddingTask&&n.Key==CurrentView; n.Count=Tasks.Count(t=>InView(t,n.Key)&&(n.Key is "done" or "all" or "day" or "yesterday" or "week" or "month"||!t.IsCompleted)); }
        RefreshMini();
        foreach(var p in new[]{nameof(HasTasks),nameof(VisibleCount),nameof(CompletedCount),nameof(Progress),nameof(CountLabel),nameof(ProgressLabel),nameof(EmptyTitle),nameof(EmptyHint),nameof(HasSearch)}) OnPropertyChanged(p);
    }
    public void CheckDay()
    {
        if(day==DateTime.Today)return;
        day=DateTime.Today; OnPropertyChanged(nameof(DateLabel));
        dateGrouping.RefreshLabels();
        foreach(var task in Tasks)task.RefreshDateLabels();
        Refresh(); Status="新的一天，今日清单已更新";
    }
    [RelayCommand] private void Navigate(NavEntry? entry) { if(entry is null)return; if(SelectedTask is not null)CommitDraft(SelectedTask); IsAddingTask=false; CurrentView=entry.Key; }
    [RelayCommand] private void StartAddingTask()
    {
        SelectedTask=null;
        if(!hasNewTaskDraft)
        {
            NewTaskList=CurrentView.StartsWith("list:")?CurrentView[5..]:Lists[0];
            NewTaskDate=null;NewTaskPriority=1;NewTaskNotes="";
            hasNewTaskDraft=true;
        }
        if(!Lists.Contains(NewTaskList))NewTaskList=Lists[0];
        NewTaskFeedback="";IsAddingTask=true;
    }
    [RelayCommand] private void CancelAddingTask()
    {
        NewTitle="";NewTaskNotes="";NewTaskDate=null;NewTaskFeedback="";NewTaskSteps.Clear();NewTaskStepTitle="";
        hasNewTaskDraft=false;IsAddingTask=false;
    }
    [RelayCommand] private void CreateTask()
    {
        if(!IsAddingTask)return;
        if(string.IsNullOrWhiteSpace(NewTitle)){NewTaskFeedback="请先填写任务内容";return;}
        if(NewTitle.Trim().Length>100){NewTaskFeedback="任务内容最多 100 个字符";return;}
        if(!string.IsNullOrWhiteSpace(NewTaskStepTitle))AddNewTaskStep();
        if(!string.IsNullOrWhiteSpace(NewTaskStepTitle))return;
        var task=new TaskItem {CreatedAt=DateTime.Now,Title=NewTitle.Trim(),
            List=Lists.Contains(NewTaskList)?NewTaskList:Lists[0],Notes=NewTaskNotes,
            DueDate=NewTaskDate?.Date,Priority=Math.Clamp(NewTaskPriority,0,2),Steps=new(NewTaskSteps.Select(s=>new TaskStep{Title=s.Title,IsCompleted=s.IsCompleted}))};
        IsAddingTask=false;hasNewTaskDraft=false;
        if(!InView(task,CurrentView))CurrentView="day";
        AddTaskCore(task);
        NewTaskDate=null;NewTaskNotes="";NewTaskFeedback="";NewTaskSteps.Clear();NewTaskStepTitle="";
    }
    [RelayCommand] private void AddTask()
    {
        if(string.IsNullOrWhiteSpace(NewTitle)){Status="请先输入任务标题";return;}
        if(IsPlannedView && !NewTaskDate.HasValue){Status="请先选择截止日期，再添加计划任务";return;}
        if(SelectedTask is not null)CommitDraft(SelectedTask);
        var task=new TaskItem { CreatedAt=DateTime.Now, Title=NewTitle.Trim(), List=CurrentView.StartsWith("list:")?CurrentView[5..]:Lists[0],
            DueDate=IsPlannedView?NewTaskDate?.Date:null };
        AddTaskCore(task);
    }
    private bool CanAddMiniTask() => !string.IsNullOrWhiteSpace(MiniNewTitle)&&MiniNewTitle.Trim().Length<=100;
    [RelayCommand(CanExecute=nameof(CanAddMiniTask))] private void AddMiniTask()
    {
        if(!CanAddMiniTask())return;
        SaveMiniTask(new TaskEditorViewModel(Lists,new TaskItem{Title=MiniNewTitle.Trim(),List=Lists[0]}),null);
    }
    public void SaveMiniTask(TaskEditorViewModel draft,TaskItem? task)
    {
        if(!draft.Validate())return;
        var isNew=task is null;
        if(!isNew&&!Tasks.Contains(task!))return;
        if(task==SelectedTask&&task is not null)SelectedTask=null;
        task??=new TaskItem{CreatedAt=DateTime.Now};
        draft.ApplyTo(task);
        if(isNew){Attach(task);Tasks.Add(task);MiniNewTitle="";if(!InView(task,MiniScopeKey))MiniScopeKey="day";}
        Refresh();ScheduleSave();Status=isNew?"已添加任务":"任务已更新";
        MiniTaskAdded?.Invoke(task);
    }
    private void AddTaskCore(TaskItem task)
    {
        Attach(task); Tasks.Add(task); NewTitle="";Search="";HistoryDate=null;if(CurrentView=="done")CurrentView="all";DateModeIndex=0;
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
        Lists.Remove(entry.Title);CustomLists.Remove(entry);
        if(CurrentView==entry.Key)CurrentView="list:"+destination;
        Refresh();OnPropertyChanged(nameof(ViewTitle));
        if(selected is not null && TasksView.Contains(selected))SelectedTask=selected;
        ScheduleSave();
        Status=moved.Count==0?"已删除清单":$"已删除清单，{moved.Count} 项任务已移至“{destination}”";
    }
    [RelayCommand] private void DeleteTask(TaskItem? task)
    {
        task??=SelectedTask;
        if(task is null||!Tasks.Contains(task)||ConfirmTaskDeletion?.Invoke(task)!=true)return;
        if(task==SelectedTask){CommitDraft(task);SelectedTask=null;}
        task.PropertyChanged-=TaskChanged;Tasks.Remove(task);Refresh();ScheduleSave();Status="任务已删除";
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
    public bool RenameList(NavEntry entry, string name)
    {
        name=name.Trim();
        if(!CustomLists.Contains(entry) || name.Length is 0 or >24){Status="清单名称应为 1–24 个字符";return false;}
        if(name==entry.Title)return true;
        if(Lists.Any(x=>string.Equals(x,name,StringComparison.OrdinalIgnoreCase)&&x!=entry.Title)){Status="这个清单已经存在";return false;}
        var old=entry.Title; var selected=SelectedTask; SelectedTask=null;
        foreach(var task in Tasks.Where(t=>t.List==old))task.List=name;
        Lists[Lists.IndexOf(old)]=name;
        CustomLists[CustomLists.IndexOf(entry)]=new("list:"+name,name,AppIcons.List);
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
