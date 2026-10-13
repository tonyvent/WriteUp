using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WriteUp.Services;

namespace WriteUp;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--speech-runtime-test"))
        {
            StartupUri = null; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            if (!WindowsAppLaunch.HasIdentity)
            {
                if (await WindowsAppLaunch.ActivateRegisteredAsync("--speech-runtime-test")) { Shutdown(); return; }
            }
            try
            {
                if (!WindowsAppLaunch.HasIdentity) throw new InvalidOperationException(WindowsAppLaunch.RegistrationError);
                var ready = Microsoft.Windows.AI.Speech.SpeechRecognitionModelFactory.Default.GetReadyState();
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "speech-runtime-test.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { success = true, identity = Windows.ApplicationModel.Package.Current.Id.FullName, readiness = ready.ToString() }));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "speech-runtime-test.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { success = false, error = ex.ToString() }));
                Shutdown(1);
            }
            return;
        }
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
