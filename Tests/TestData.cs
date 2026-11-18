using TaskDesk.Models;

internal static class TestData
{
    // Fixtures only. Production startup never creates demonstration tasks or categories.
    public static AppData Create()
    {
        var today=DateTime.Today;
        return new AppData {Lists=["工作项目","个人生活","学习成长"], Tasks=[
            new(){Title="界面设计",DueDate=today,TodayDate=today,Priority=0,IsImportant=true,Steps=[new(){Title="步骤一",IsCompleted=true},new(){Title="步骤二"}]},
            new(){Title="项目安排",DueDate=today,TodayDate=today},
            new(){Title="阅读",List="个人生活",DueDate=today,TodayDate=today,Priority=2},
            new(){Title="文档确认",DueDate=today.AddDays(-1),TodayDate=today,Priority=0,IsImportant=true},
            new(){Title="整理资料",List="个人生活",DueDate=today,TodayDate=today,IsCompleted=true},
            new(){Title="下次评审",DueDate=today.AddDays(1)}
        ]};
    }
}
