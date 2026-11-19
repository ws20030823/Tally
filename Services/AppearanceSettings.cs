using System.IO;
using System.Text.Json;

namespace TaskDesk.Services;

public sealed record ThemeOption(string Key,string Title);

public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "warm";
    public int OpacityPercent { get; set; } = 100;
    public static int NormalizeOpacity(int value) => Math.Clamp(value,50,100);
    public static IReadOnlyList<ThemeOption> Themes { get; } = [new("warm","暖白与墨绿"),new("blue","银灰与雾蓝")];
    public static string Normalize(string? theme) => theme=="blue"?"blue":"warm";
    public static AppearanceSettings Load(string path)
    {
        try
        {
            var settings=File.Exists(path)?JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path)):null;
            return new(){Theme=Normalize(settings?.Theme),OpacityPercent=NormalizeOpacity(settings?.OpacityPercent??100)};
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) {return new();}
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(new AppearanceSettings{Theme=Normalize(Theme),OpacityPercent=NormalizeOpacity(OpacityPercent)},new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,true);
    }
}
