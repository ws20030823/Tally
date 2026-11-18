using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using TaskDesk.Models;

namespace TaskDesk.Services;

public sealed class DataStore(string directory, string? legacyDirectory = null)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string FilePath => Path.Combine(DirectoryPath, "tasks.json");
    public string? LoadWarning { get; private set; }
    public string? LegacyDirectoryPath { get; } = legacyDirectory is null ? null : Path.GetFullPath(legacyDirectory);
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "拾序");
    public static string LegacyDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskDesk");
    private static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public AppData Load()
    {
        if (!File.Exists(FilePath))
        {
            // A missing primary with an existing backup is recovery, not a fresh workspace.
            if (File.Exists(FilePath + ".bak"))
            {
                var recovered = Read(FilePath + ".bak");
                Save(recovered);
                LoadWarning = "主文件缺失，已从备份恢复。";
                return recovered;
            }
            AppData data;
            var legacy = LegacyDirectoryPath is null ? null : Path.Combine(LegacyDirectoryPath, "tasks.json");
            if (legacy is not null && (File.Exists(legacy) || File.Exists(legacy + ".bak")))
            {
                // Never rewrite or delete old-version files during migration.
                try { data = File.Exists(legacy) ? Read(legacy) : Read(legacy + ".bak"); }
                catch (Exception e) when (e is JsonException or InvalidDataException)
                {
                    if (!File.Exists(legacy + ".bak")) throw new InvalidDataException("旧版数据无法读取，迁移未执行；原文件已保留。", e);
                    data = Read(legacy + ".bak");
                }
                LoadWarning = $"已迁移旧版数据，共 {data.Tasks.Count} 项任务。";
            }
            else data = new AppData();
            // Initialize immediately: even a first empty launch has a real on-disk source of truth.
            Save(data);
            return data;
        }
        try { return Read(FilePath); }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            if (!File.Exists(FilePath + ".bak")) throw new InvalidDataException("数据文件无法读取。原文件已保留，请先备份或修复后再启动。", e);
            var recovered = Read(FilePath + ".bak");
            File.Copy(FilePath, FilePath + $".damaged-{DateTime.Now:yyyyMMddHHmmssfff}");
            // Move the invalid primary aside so the next save cannot replace the valid backup with it.
            File.Move(FilePath, FilePath + ".recovered", true);
            LoadWarning = "数据异常，已从备份恢复；损坏文件已保留。";
            return recovered;
        }
    }

    private static AppData Read(string path)
    {
        var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("空数据文件");
        Validate(data);
        return data;
    }
    public static void Validate(AppData data)
    {
        if (data.Version != 1 || data.Lists is null || data.Tasks is null || data.Lists.Count == 0 ||
            data.Lists.Any(string.IsNullOrWhiteSpace) || data.Lists.Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Lists.Count ||
            data.Tasks.Any(t => t is null || string.IsNullOrWhiteSpace(t.Title) || !data.Lists.Contains(t.List) || t.Priority is < 0 or > 2 ||
                t.Steps is null || t.Steps.Any(s => s is null || string.IsNullOrWhiteSpace(s.Title))) ||
            data.Tasks.Select(t => t.Id).Distinct().Count() != data.Tasks.Count)
            throw new InvalidDataException("数据格式不正确或包含无效任务");
    }
    public void Save(AppData data)
    {
        Validate(data);
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, data, Options);
            stream.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
    }

}
