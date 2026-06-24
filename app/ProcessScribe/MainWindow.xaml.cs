using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using ProcessScribe.Models;
using ProcessScribe.Services;

namespace ProcessScribe;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x5253; // arbitrary unique id
    private const uint VK_R = 0x52;

    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _previewTimer;
    private AppSettings _settings;

    private Recorder? _recorder;
    private string? _sessionDir;
    private DateTime _startTime;
    private HwndSource? _source;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsStore.Load();
        ApplySettingsToUi();
        DataContext = _vm;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            var t = DateTime.Now - _startTime;
            _vm.Elapsed = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        };

        // Debounced live preview refresh.
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RefreshPreview(); };

        _vm.Steps.CollectionChanged += OnStepsChanged;
        _vm.Meta.PropertyChanged += (_, _) => SchedulePreview();

        Loaded += (_, _) => RefreshPreview();
    }

    // ---- live preview -------------------------------------------------------
    private void OnStepsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Step s in e.NewItems)
                s.PropertyChanged += OnStepEdited;
        if (e.OldItems != null)
            foreach (Step s in e.OldItems)
                s.PropertyChanged -= OnStepEdited;
        SchedulePreview();
    }

    private void OnStepEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SchedulePreview();

    private void SchedulePreview()
    {
        // Skip live churn while recording (screenshots are heavy); we refresh on stop.
        if (!_vm.AutoPreview || _vm.IsRecording) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RefreshPreview()
    {
        try { PreviewViewer.Document = FlowReport.Build(_vm.Meta, _vm.Steps.ToList()); }
        catch { /* preview is best-effort */ }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPreview();

    private void AutoPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.AutoPreview) RefreshPreview();
    }

    private void ApplySettingsToUi()
    {
        _vm.Meta.Author = _settings.DefaultAuthor;
        _vm.Meta.Company = _settings.DefaultCompany;
        _vm.Meta.Department = _settings.DefaultDepartment;
        _vm.Meta.LogoPath = _settings.DefaultLogoPath;
        _vm.AlwaysOnTop = _settings.AlwaysOnTop;
        _vm.OutputDir = string.IsNullOrWhiteSpace(_settings.OutputDir)
            ? SettingsStore.DefaultSessionsDir
            : _settings.OutputDir;
    }

    // ---- global hotkey wiring ----------------------------------------------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var helper = new WindowInteropHelper(this);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
        NativeMethods.RegisterHotKey(
            helper.Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, VK_R);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleRecording();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---- recording ----------------------------------------------------------
    private void RecordBtn_Click(object sender, RoutedEventArgs e) => ToggleRecording();

    private void ToggleRecording()
    {
        if (_vm.IsRecording) StopRecording();
        else StartRecording();
    }

    private void StartRecording()
    {
        try
        {
            string root = string.IsNullOrWhiteSpace(_vm.OutputDir)
                ? SettingsStore.DefaultSessionsDir : _vm.OutputDir;
            _sessionDir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(_sessionDir);

            _recorder = new Recorder(Dispatcher, _sessionDir, _settings.MaxImageWidth);
            _recorder.StepAdded += OnStepAdded;
            _recorder.Start();

            _startTime = DateTime.Now;
            _vm.Elapsed = "00:00";
            _vm.IsRecording = true;
            _timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not start recording:\n" + ex.Message,
                "ProcessScribe", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopRecording()
    {
        _timer.Stop();
        if (_recorder != null)
        {
            _recorder.StepAdded -= OnStepAdded;
            _recorder.Stop();
            _recorder.Dispose();
            _recorder = null;
        }
        _vm.IsRecording = false;
        PersistSettings();
        RefreshPreview();
    }

    private void OnStepAdded(Step step)
    {
        // Already marshalled to the UI thread by the recorder.
        _vm.Steps.Add(step);
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        _recorder?.AddNote("");
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Step step)
            _vm.Steps.Remove(step);
    }

    // ---- metadata / settings ------------------------------------------------
    private void BrowseLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a logo image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            _vm.Meta.LogoPath = dlg.FileName;
    }

    private void ChangeOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose where sessions are saved" };
        if (dlg.ShowDialog(this) == true)
        {
            _vm.OutputDir = dlg.FolderName;
            PersistSettings();
        }
    }

    private void PersistSettings()
    {
        _settings.DefaultAuthor = _vm.Meta.Author;
        _settings.DefaultCompany = _vm.Meta.Company;
        _settings.DefaultDepartment = _vm.Meta.Department;
        _settings.DefaultLogoPath = _vm.Meta.LogoPath;
        _settings.AlwaysOnTop = _vm.AlwaysOnTop;
        _settings.OutputDir = _vm.OutputDir;
        SettingsStore.Save(_settings);
    }

    // ---- export -------------------------------------------------------------
    private string? AskSavePath(string title, string filter, string ext)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = SafeFileName(ReportWriter.TitleOf(_vm.Meta)) + "." + ext
        };
        string initial = FirstExistingDir(_settings.LastExportDir, _sessionDir, _vm.OutputDir);
        if (!string.IsNullOrEmpty(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "report";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, ' ');
        name = name.Trim();
        return name.Length == 0 ? "report" : name;
    }

    private static string FirstExistingDir(params string?[] dirs)
    {
        foreach (var d in dirs)
            if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d)) return d!;
        return "";
    }

    private void RememberExportDir(string path)
    {
        _settings.LastExportDir = Path.GetDirectoryName(path) ?? "";
        PersistSettings();
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        string? path = AskSavePath("Save PDF as", "PDF document (*.pdf)|*.pdf", "pdf");
        if (path == null) return;
        try
        {
            DocumentExporter.SavePdf(_vm.Meta, _vm.Steps.ToList(), path);
            RememberExportDir(path);
            OpenFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PDF export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportWord_Click(object sender, RoutedEventArgs e)
    {
        string? path = AskSavePath("Save Word document as", "Word-compatible RTF (*.rtf)|*.rtf", "rtf");
        if (path == null) return;
        try
        {
            DocumentExporter.SaveRtf(_vm.Meta, _vm.Steps.ToList(), path);
            RememberExportDir(path);
            OpenFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Word export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        string? path = AskSavePath("Save HTML as", "Web page (*.html)|*.html", "html");
        if (path == null) return;
        try
        {
            // Self-contained: images are embedded, so it's portable anywhere.
            File.WriteAllText(path, ReportWriter.Html(_vm.Meta, _vm.Steps.ToList()));
            RememberExportDir(path);
            OpenFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        string? path = AskSavePath("Save Markdown as", "Markdown (*.md)|*.md", "md");
        if (path == null) return;
        try
        {
            File.WriteAllText(path, ReportWriter.Markdown(_vm.Meta, _vm.Steps.ToList()));
            // Markdown references images relatively, so copy them next to the file.
            CopyScreenshotsBeside(path);
            RememberExportDir(path);
            OpenFolder(Path.GetDirectoryName(path)!);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Copies the referenced screenshots into a ./screenshots folder
    /// beside the saved Markdown so its relative image links resolve anywhere.</summary>
    private void CopyScreenshotsBeside(string mdPath)
    {
        string dir = Path.GetDirectoryName(mdPath)!;
        string shots = Path.Combine(dir, "screenshots");
        foreach (var s in _vm.Steps)
        {
            if (!s.HasScreenshot || !File.Exists(s.ScreenshotPath!)) continue;
            string dest = Path.Combine(shots, Path.GetFileName(s.ScreenshotPath!));
            if (string.Equals(Path.GetFullPath(s.ScreenshotPath!), Path.GetFullPath(dest),
                    StringComparison.OrdinalIgnoreCase))
                continue; // already in place (saved into the session folder)
            try
            {
                Directory.CreateDirectory(shots);
                File.Copy(s.ScreenshotPath!, dest, overwrite: true);
            }
            catch { /* skip a problem image, keep going */ }
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = FirstExistingDir(_settings.LastExportDir, _sessionDir, _vm.OutputDir,
            SettingsStore.DefaultSessionsDir);
        OpenFolder(dir);
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private static void OpenFolder(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ---- shutdown -----------------------------------------------------------
    protected override void OnClosed(EventArgs e)
    {
        try
        {
            if (_source != null)
            {
                var helper = new WindowInteropHelper(this);
                NativeMethods.UnregisterHotKey(helper.Handle, HotkeyId);
                _source.RemoveHook(WndProc);
            }
            _recorder?.Dispose();
            PersistSettings();
        }
        catch { /* ignore */ }
        base.OnClosed(e);
    }
}
