using System.Windows.Markup;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace TaskDesk.Controls;


// Subset of iconfont.cn's Ant Design official collection 9402, embedded for offline use.
public static class AppIcons
{
    public static FontFamily Family { get; } = new(new Uri("pack://application:,,,/TaskDesk;component/"), "./Assets/Icons/TaskDeskIcons.ttf#TaskDesk Iconfont Ant");
    public const string Today = "\uE900";
    public const string Planned = "\uE902";
    public const string AllTasks = "\uE903";
    public const string Completed = "\uE904";
    public const string List = "\uE905";
    public const string Search = "\uE906";
    public const string Add = "\uE907";
    public const string Close = "\uE908";
    public const string Delete = "\uE909";
}

// Each target needs its own visual instance; the embedded font asset is shared.
[MarkupExtensionReturnType(typeof(FontIcon))]
public sealed class IconfontExtension(string name) : MarkupExtension
{
    public string Name { get; set; } = name;
    public double Size { get; set; } = 18;
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var glyph = Name switch
        {
            "Today" => AppIcons.Today,
            "Planned" => AppIcons.Planned,
            "AllTasks" => AppIcons.AllTasks,
            "Completed" => AppIcons.Completed,
            "List" => AppIcons.List,
            "Search" => AppIcons.Search,
            "Add" => AppIcons.Add,
            "Close" => AppIcons.Close,
            "Delete" => AppIcons.Delete,
            _ => throw new ArgumentException($"Unknown iconfont icon: {Name}")
        };
        return new FontIcon { FontFamily = AppIcons.Family, Glyph = glyph, FontSize = Size };
    }
}
