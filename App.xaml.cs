using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using TaskDesk.Services;
using TaskDesk.ViewModels;

namespace TaskDesk;

public partial class App : Application
{
    private Mutex? instance;
    private Mutex? legacyInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var customDirectory = Argument(e.Args,"--data-dir");
        var dataDirectory = customDirectory ?? DataStore.DefaultDirectory;
        var preview=Argument(e.Args,"--preview");
        var mutexName=MutexName(dataDirectory);
        instance=new Mutex(true,mutexName,out var first);
        if(!first){MessageBox.Show("任务清单已在另一个窗口中打开。","Tally");Shutdown();return;}
        try
        {
            // The old and new versions must not write divergent copies during first migration.
            if(customDirectory is null)
            {
                legacyInstance = new Mutex(true,MutexName(DataStore.LegacyDirectory),out var legacyFree);
                if(!legacyFree){MessageBox.Show("请先关闭旧版拾序，再打开 Tally。","Tally");Shutdown();return;}
            }
            var vm=new MainViewModel(new DataStore(dataDirectory,customDirectory is null?DataStore.LegacyDirectory:null));
            var window=new MainWindow(vm);
            MainWindow=window;
            if(Argument(e.Args,"--theme") is { } theme)window.ApplyTheme(theme,false);
            if(e.Args.Contains("--mini"))window.Loaded+=(_,_)=>window.SetMiniMode(true);
            if(preview is not null)window.ConfigurePreview(preview);
            window.Show();
        }
        catch(Exception ex)
        {
            if(preview is not null)File.WriteAllText(preview+".error.txt",ex.ToString());
            else MessageBox.Show("无法启动 Tally。\n"+ex.Message,"Tally",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    public static string? Argument(string[] args,string name)
    {var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
    private static string MutexName(string directory)=>"Local\\TaskDesk-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant())))[..20];
    protected override void OnExit(ExitEventArgs e){legacyInstance?.Dispose();instance?.Dispose();base.OnExit(e);}
}
