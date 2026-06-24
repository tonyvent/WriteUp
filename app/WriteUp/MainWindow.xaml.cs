using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WriteUp.Models;
using WriteUp.Services;

namespace WriteUp;

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

        // Sweep up any session folders a previous run/crash left behind.
        SessionCleanup.PurgeOrphans(_vm.OutputDir);
        SessionCleanup.PurgeOrphans(SettingsStore.DefaultSessionsDir);

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

    private bool _propagatingContext;

    private void OnStepEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Renaming one step's section label renames the whole contiguous visit to
        // that app, so the user edits a category once rather than step-by-step.
        if (!_propagatingContext && e.PropertyName == nameof(Step.Context) && sender is Step s)
            PropagateContext(s);
        SchedulePreview();
    }

    private void PropagateContext(Step edited)
    {
        int i = _vm.Steps.IndexOf(edited);
        if (i < 0 || string.IsNullOrWhiteSpace(edited.App)) return;  // notes/unknown: leave alone

        string app = edited.App;
        string value = edited.Context;
        _propagatingContext = true;
        try
        {
            for (int j = i - 1; j >= 0 && SameVisit(_vm.Steps[j], app); j--)
                _vm.Steps[j].Context = value;
            for (int j = i + 1; j < _vm.Steps.Count && SameVisit(_vm.Steps[j], app); j++)
                _vm.Steps[j].Context = value;
        }
        finally { _propagatingContext = false; }
    }

    private static bool SameVisit(Step s, string app) =>
        string.Equals(s.App, app, StringComparison.OrdinalIgnoreCase);

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
        _vm.Meta.Company = Branding.Company;   // fixed branding (logo is bundled)
        _vm.Meta.Department = _settings.DefaultDepartment;
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
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Error);
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

    // ---- settings -----------------------------------------------------------
    private void PersistSettings()
    {
        _settings.DefaultAuthor = _vm.Meta.Author;
        _settings.DefaultDepartment = _vm.Meta.Department;
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

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
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

            // Don't let capture scratch space pile up on disk.
            if (_settings.CleanupSessionsOnExit)
                SessionCleanup.Delete(_sessionDir);
            SessionCleanup.PurgeOrphans(_vm.OutputDir, keep: _settings.CleanupSessionsOnExit ? null : _sessionDir);
        }
        catch { /* ignore */ }
        base.OnClosed(e);
    }
}
