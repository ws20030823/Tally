using System.Windows;

// WPF queues startup even when tests drive a DispatcherFrame without Application.Run.
// Never let a UI test start the production app or open the user's task directory.
internal sealed class TestApp : Application
{
    public void InitializeComponent()
    {
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary {Theme=Wpf.Ui.Appearance.ApplicationTheme.Light});
        Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/TaskDesk;component/Themes/Palettes/Warm.xaml",UriKind.Relative)});
        Resources.MergedDictionaries.Add((ResourceDictionary)LoadComponent(new Uri("/TaskDesk;component/Themes/Glass.xaml",UriKind.Relative)));
    }
}
