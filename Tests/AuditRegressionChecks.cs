using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskDesk.Models;
using TaskDesk.Services;
using TaskDesk.ViewModels;

internal static class AuditRegressionChecks
{
    public static void Run(string root)
    {
        var malformed = new[]
        {
            "{}", "{\"Version\":1,\"Lists\":[\"默认清单\"]}",
            "{\"Version\":1,\"Lists\":[\"默认清单\"],\"Tasks\":null}",
            "{\"Lists\":[\"默认清单\"],\"Tasks\":[]}", "{\"Version\":1,\"Tasks\":[]}",
            "{\"Version\":1,\"Lists\":[\"默认清单\"],\"Tasks\":{}}",
            "{\"Version\":1,\"Lists\":null,\"Tasks\":[]}",
            "{\"Version\":999,\"Lists\":[\"默认清单\"],\"Tasks\":[]}"
        };
        foreach (var json in malformed)
        {
            var store = Store(root);
            store.Save(Data()); store.Save(Data());
            var backup = File.ReadAllBytes(store.FilePath + ".bak");
            File.WriteAllText(store.FilePath, json);
            var recovered = store.Load();
            Check(recovered.Tasks.Count == 1 && store.LoadWarning is not null,
                "invalid structure uses valid backup: " + json);
            Check(Directory.GetFiles(store.DirectoryPath, "*.damaged-*").Any(p => File.ReadAllText(p) == json),
                "invalid primary preserved");
            store.Save(recovered);
            Check(File.ReadAllBytes(store.FilePath + ".bak").SequenceEqual(backup) && store.Load().Tasks.Count == 1,
                "recovery save retains valid backup");

            var noBackup = Store(root);
            Directory.CreateDirectory(noBackup.DirectoryPath);
            File.WriteAllText(noBackup.FilePath, json);
            try { noBackup.Load(); throw new Exception("accepted invalid structure"); }
            catch (InvalidDataException) { }
            Check(File.ReadAllText(noBackup.FilePath) == json && !File.Exists(noBackup.FilePath + ".bak"),
                "invalid file without backup fails without rewriting");
        }
        var empty = Store(root);
        empty.Save(Data());
        File.WriteAllText(empty.FilePath, "{\"Version\":1,\"Lists\":[\"默认清单\"],\"Tasks\":[]}");
        Check(empty.Load().Tasks.Count == 0 && empty.LoadWarning is null, "explicit empty task array remains valid");

        foreach (var kind in new[] { "missing", "null", "empty", "unicode" })
        {
            var store = Store(root);
            var json = JsonSerializer.SerializeToNode(Data())!;
            var task = json["Tasks"]![0]!.AsObject();
            if (kind == "missing") task.Remove("Notes");
            else task["Notes"] = kind == "null" ? null : kind == "unicode" ? "多行\n中文 😀" : "";
            // Older files legitimately lack task timestamps.
            task.Remove("CreatedAt"); task.Remove("CompletedAt");
            Directory.CreateDirectory(store.DirectoryPath);
            File.WriteAllText(store.FilePath, json.ToJsonString());
            using var vm = new MainViewModel(store);
            vm.CurrentView = "all";
            Check(vm.Tasks.Single().Notes == (kind == "unicode" ? "多行\n中文 😀" : ""), "normalize optional notes: " + kind);
            Check(vm.Tasks.Single().CreatedAt is null, "legacy timestamp remains unknown");
            vm.Search = kind == "unicode" ? "😀" : "not found";
            Check(vm.VisibleCount == (kind == "unicode" ? 1 : 0), "search optional notes: " + kind);
            vm.Tasks.Single().Notes = null;
            Check(vm.Flush() && store.Load().Tasks.Single().Notes == "", "null assignment saves and reloads as empty notes");
        }

        var directory = Path.Combine(root, "mutex-" + Guid.NewGuid().ToString("N"));
        var equivalents = new[] { directory, directory + "\\", directory + "/", directory.ToUpperInvariant(),
            Path.Combine(directory, "."), Path.Combine(directory, "child", "..") };
        Check(equivalents.All(p => MutexName(p) == MutexName(directory)), "equivalent paths share mutex identity");
        var volume = Path.GetPathRoot(directory)!;
        Check(MutexName(volume) == MutexName(volume + "."), "volume root semantics preserved");
        using (var held = new Mutex(true, MutexName(directory), out var created))
        {
            Check(created, "parent owns isolated instance mutex");
            try
            {
                foreach (var path in equivalents)
                    Check(ChildProbe(path) == 10, "second process detects held mutex: " + path);
                Check(ChildProbe(directory + "-other") == 0, "independent data directory can run concurrently");
            }
            finally { held.ReleaseMutex(); }
        }
        Check(ChildProbe(directory) == 0, "second process can start after lock released");
    }

    public static int ProbeMutex(string directory)
    {
        using var mutex = new Mutex(true, MutexName(directory), out var created);
        if (created) mutex.ReleaseMutex();
        return created ? 0 : 10;
    }

    private static int ChildProbe(string directory)
    {
        var executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(AuditRegressionChecks).Assembly.Location);
        start.ArgumentList.Add("--mutex-probe"); start.ArgumentList.Add(directory);
        using var child = Process.Start(start) ?? throw new Exception("mutex probe failed to launch");
        if (!child.WaitForExit(10000)) { child.Kill(); child.WaitForExit(); throw new Exception("mutex probe timed out"); }
        return child.ExitCode;
    }

    private static string MutexName(string directory) => (string)typeof(TaskDesk.App)
        .GetMethod("MutexName", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [directory])!;
    private static AppData Data() => new() { Tasks = [new TaskItem { Title = "回归任务", List = "默认清单" }] };
    private static DataStore Store(string root) => new(Path.Combine(root, "regression-" + Guid.NewGuid().ToString("N")));
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("OK: " + name); }
}
