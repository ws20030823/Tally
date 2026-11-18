using System.IO;
using System.Text.Json;
using System.Windows;

namespace TaskDesk.Services;

// UI preferences are independent of tasks.json and never change its schema.
public sealed class MiniWindowSettings
{
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 420;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; }

    public static MiniWindowSettings Load(string path)
    {
        try
        {
            var settings=File.Exists(path)?JsonSerializer.Deserialize<MiniWindowSettings>(File.ReadAllText(path)):null;
            settings ??= new();
            settings.Width=double.IsFinite(settings.Width)?Math.Clamp(settings.Width,280,900):320;
            settings.Height=double.IsFinite(settings.Height)?Math.Clamp(settings.Height,240,1200):420;
            if(settings.Left is { } left&&!double.IsFinite(left))settings.Left=null;
            if(settings.Top is { } top&&!double.IsFinite(top))settings.Top=null;
            return settings;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public Rect VisibleBounds(Rect fallback)
    {
        var width=Math.Min(Width,SystemParameters.VirtualScreenWidth);
        var height=Math.Min(Height,SystemParameters.VirtualScreenHeight);
        return new Rect(
            Math.Clamp(Left??fallback.Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-width),
            Math.Clamp(Top??fallback.Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-height),width,height);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,true);
    }
}
