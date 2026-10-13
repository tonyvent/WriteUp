using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WriteUp.Services;

namespace WriteUp;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Test hosts instantiate App too; only the actual desktop EXE hands off.
        if (Assembly.GetEntryAssembly()?.GetName().Name != "WriteUp" || WindowsAppLaunch.HasIdentity ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
        { base.OnStartup(e); return; }
        StartupUri = null;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        var starting = new Window
        {
            Title = "WriteUp", Width = 360, Height = 110, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock { Text = "Starting WriteUp…", Margin = new Thickness(24), FontSize = 16 }
        };
        starting.Show();
        bool activated = await WindowsAppLaunch.ActivateRegisteredAsync();
        starting.Close();
        if (activated) { Shutdown(); return; }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow(); MainWindow.Show();
    }
}
