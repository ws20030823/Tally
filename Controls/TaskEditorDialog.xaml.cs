using System.Windows;
using System.Windows.Controls;
using TaskDesk.ViewModels;

namespace TaskDesk.Controls;

public partial class TaskEditorDialog : Wpf.Ui.Controls.FluentWindow
{
    public TaskEditorViewModel Draft { get; }
    public TaskEditorDialog(TaskEditorViewModel draft,bool isNew)
    {
        InitializeComponent();Draft=draft;DataContext=draft;
        Title=EditorTitle.Title=isNew?"添加任务":"编辑任务";
        SaveButton.Content=isNew?"添加任务":"保存修改";
        Loaded+=(_,_)=>{TitleInput.Focus();TitleInput.CaretIndex=TitleInput.Text.Length;};
    }
    private void InvalidDate(object? sender,DatePickerDateValidationErrorEventArgs e){e.ThrowException=false;DateFeedback.Text="请输入有效日期，例如 2026/11/19";}
    private void DateChanged(object? sender,SelectionChangedEventArgs e)=>DateFeedback.Text="";
    private void ClearEditorDate(object sender,RoutedEventArgs e)
    {
        DuePicker.Text="";DuePicker.SelectedDate=null;Draft.DueDate=null;DateFeedback.Text="";
    }
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        // Moving focus commits typed dates before validating and saving the draft.
        SaveButton.Focus();
        if(!string.IsNullOrWhiteSpace(DateFeedback.Text))return;
        if(Draft.Validate())DialogResult=true;
    }
}
