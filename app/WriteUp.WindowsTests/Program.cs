using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WriteUp;
using WriteUp.Models;
using WriteUp.Services;

internal static class Program
{
    private static int _exit;
    private static readonly string Temp = Path.Combine(Path.GetTempPath(), "writeup-windows-" + Guid.NewGuid());
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--target"))
        {
            var targetApp = new Application();
            var panel = new StackPanel();
            panel.Children.Add(new Button { Content = "Capture test target", Height = 100 });
            panel.Children.Add(new TextBox { Name = "Entry", Height = 100 });
            targetApp.Run(new Window { Title = "WriteUp capture test target", Content = panel, Left = 30, Top = 30, Width = 450, Height = 300 });
            return 0;
        }
        Directory.CreateDirectory(Temp);
        SettingsStore.Save(new AppSettings { ShowGuidedTour = false, NarrationEnabled = false });
        var app = new WriteUp.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunChecks(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); _exit = 1; }
            finally
            {
                foreach (Window window in app.Windows.Cast<Window>().ToList())
                {
                    if (window is MainWindow) SetField(window, "_closeConfirmed", true);
                    window.Close();
                }
                app.Shutdown();
            }
        }, DispatcherPriority.ApplicationIdle);
        app.Run();
        try { Directory.Delete(Temp, true); } catch { }
        return _exit;
    }
    private static void SetField(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var element2 in Elements(child)) yield return element2;
        }
    }
    private static bool IsInput(FrameworkElement e) => e is ButtonBase or TextBoxBase or ComboBox;
    private static bool InsideControl(FrameworkElement e, Window window)
    {
        for (var parent = VisualTreeHelper.GetParent(e); parent != null && parent != window; parent = VisualTreeHelper.GetParent(parent))
            if (parent is FrameworkElement fe && (IsInput(fe) || fe is FlowDocumentScrollViewer)) return true;
        return false;
    }
    private static Rect VisibleBounds(FrameworkElement e, Window w)
    {
        var rect = e.TransformToAncestor(w).TransformBounds(new Rect(e.RenderSize));
        rect.Intersect(new Rect(0, 0, w.ActualWidth, w.ActualHeight));
        for (var parent = VisualTreeHelper.GetParent(e); parent != null && parent != w; parent = VisualTreeHelper.GetParent(parent))
            if (parent is FrameworkElement fe && (fe.ClipToBounds || fe is ScrollContentPresenter))
                rect.Intersect(fe.TransformToAncestor(w).TransformBounds(new Rect(fe.RenderSize)));
        return rect;
    }
    private static string Label(FrameworkElement e) => e.Name.Length > 0 ? e.Name : e is TextBlock t ? t.Text : e is ContentControl c ? c.Content?.ToString() ?? e.GetType().Name : e.GetType().Name;
    private static void CheckLayout(Window window, string label)
    {
        window.UpdateLayout();
        var elements = Elements(window).Where(e => e.IsVisible && e.ActualWidth > 1 && e.ActualHeight > 1
            && (IsInput(e) || e is TextBlock) && !InsideControl(e, window)).ToList();
        for (int i = 0; i < elements.Count; i++)
        for (int j = i + 1; j < elements.Count; j++)
        {
            if (!IsInput(elements[i]) && !IsInput(elements[j])) continue;
            Rect a = VisibleBounds(elements[i], window), b = VisibleBounds(elements[j], window);
            a.Intersect(b);
            if (!a.IsEmpty && a.Width > 2 && a.Height > 2)
                throw new Exception($"{label}: overlapping {Label(elements[i])} / {Label(elements[j])}: {a}");
        }
        Console.WriteLine("PASS layout " + label);
    }
    private static async Task Inspect(Window window, string label, int width, int height)
    {
        window.Width = width; window.Height = height; window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        CheckLayout(window, label);
        // Scroll each editor/pane to its end, including expanded settings feedback.
        foreach (var scroll in Elements(window).OfType<ScrollViewer>().ToList()) scroll.ScrollToEnd();
        window.UpdateLayout(); CheckLayout(window, label + " scrolled");
        window.Hide();
    }
    private static async Task RunChecks()
    {
        // Enumerating Windows' built-in dictation languages exercises the WinRT projection
        // without enabling online speech or recording a microphone in CI.
        var nativeLanguages = Windows.Media.SpeechRecognition.SpeechRecognizer.SupportedTopicLanguages;
        Console.WriteLine("PASS Windows dictation API loads: " + nativeLanguages.Count + " topic languages");
        var settings = new AppSettings { ShowGuidedTour = false, NarrationEnabled = false };
        SettingsStore.Save(settings);
        var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() ?? new MainWindow();
        var vm = (MainViewModel)main.DataContext;
        vm.Steps.Add(new Step { Caption = "A long instruction that should wrap without covering the editor buttons.",
            Context = "Long payroll application context", Notes = "A spoken explanation appears alongside this step." });
        await Inspect(main, "main minimum", 1000, 600);
        await Inspect(main, "main normal", 1260, 780);
        // Approximate reduced logical space at increased Windows scaling.
        var dialog = new SettingsWindow(settings);
        foreach (var expander in Elements(dialog).OfType<Expander>()) expander.IsExpanded = true;
        await Inspect(dialog, "settings minimum", 480, 480);
        foreach (var expander in Elements(dialog).OfType<Expander>()) expander.IsExpanded = true;
        dialog.Show(); dialog.UpdateLayout();
        foreach (var scroll in Elements(dialog).OfType<ScrollViewer>().ToList()) scroll.ScrollToEnd();
        dialog.UpdateLayout(); CheckLayout(dialog, "settings feedback expanded"); dialog.Hide();
        await Inspect(new StepEditorWindow(vm.Steps[0], vm.Steps.ToList()), "step editor minimum", 600, 500);
        var bitmap = new RenderTargetBitmap(640, 400, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0,0,640,400));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string image = Path.Combine(Temp, "fixture.png"); using (var stream = File.Create(image)) encoder.Save(stream);
        await Inspect(new AnnotationEditorWindow(new Step { ScreenshotPath = image, ZoomImagePath = image }), "annotation minimum", 760, 480);
        await Inspect(new CompactBar { DataContext = vm }, "compact", 620, 240);
        await CaptureCheck();
    }
    private static async Task CaptureCheck()
    {
        string assembly = Assembly.GetExecutingAssembly().Location;
        using var target = Process.Start(new ProcessStartInfo("dotnet") { ArgumentList = { assembly, "--target" }, UseShellExecute = false })!;
        try
        {
            for (int i = 0; i < 50 && target.MainWindowHandle == IntPtr.Zero; i++) { await Task.Delay(100); target.Refresh(); }
            if (target.MainWindowHandle == IntPtr.Zero) throw new Exception("Capture test target did not open.");
            var recorded = new List<Step>();
            using var recorder = new Recorder(Dispatcher.CurrentDispatcher, Temp, 800, new AppSettings { CaptureWindowChanges = false });
            recorder.StepAdded += recorded.Add;
            recorder.Start();
            SetForegroundWindow(target.MainWindowHandle);
            await Task.Delay(200);
            GetWindowRect(target.MainWindowHandle, out var bounds);
            SetCursorPos(bounds.Left + 100, bounds.Top + 75);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(1000);
            SetCursorPos(bounds.Left + 100, bounds.Top + 175);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(300);
            keybd_event(0x41, 0, 0, UIntPtr.Zero); keybd_event(0x41, 0, 2, UIntPtr.Zero);
            await Task.Delay(700);
            recorder.AddNarration(DateTime.Now, "Confirm the account before proceeding.");
            string? transcript = null; recorder.NarrationReady += (_, text) => transcript = text;
            await recorder.StopAsync();
            if (!recorded.Any(s => s.Kind == StepKind.Click && File.Exists(s.ScreenshotPath))) throw new Exception("Mouse capture did not produce a screenshot.");
            if (!recorded.Any(s => s.Kind == StepKind.Type)) throw new Exception("Keyboard capture did not produce a typed step.");
            if (transcript == null) throw new Exception("Narration was lost while stopping.");
            Console.WriteLine("PASS Windows input hooks, screenshot capture, idle typing flush and narration drain");
        }
        finally { if (!target.HasExited) target.Kill(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
