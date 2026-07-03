using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
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
    private readonly DispatcherTimer _saveTimer;
    private AppSettings _settings;

    private Recorder? _recorder;
    private string? _sessionDir;
    private DateTime _startTime;
    private HwndSource? _source;
    private CompactBar? _compact;
    private bool _dirty;            // recorded steps changed since the last export
    private bool _closeConfirmed;   // the export-on-close prompt has been resolved

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsStore.Load();
        ApplySettingsToUi();
        DataContext = _vm;

        // Sweep up any session folders a previous run/crash left behind —
        // but only when the user has opted into cleanup; otherwise sessions
        // are kept on disk so they can be reopened with 📂 Open….
        if (_settings.CleanupSessionsOnExit)
        {
            SessionCleanup.PurgeOrphans(_vm.OutputDir);
            SessionCleanup.PurgeOrphans(SettingsStore.DefaultSessionsDir);
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            var t = DateTime.Now - _startTime;
            _vm.Elapsed = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        };

        // Debounced live preview refresh.
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RefreshPreview(); };

        // Debounced session autosave: any edit lands in session.json shortly
        // after, so the session can be reopened later (when cleanup is off).
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSession(); };

        _vm.Steps.CollectionChanged += OnStepsChanged;
        _vm.Meta.PropertyChanged += (_, _) => { _dirty = true; SchedulePreview(); ScheduleAutosave(); };

        Loaded += (_, _) =>
        {
            RefreshPreview();
            // First launch: run the guided tour once layout has settled.
            if (_settings.ShowGuidedTour)
                Dispatcher.BeginInvoke(StartTour, DispatcherPriority.Loaded);
        };
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
        _dirty = true;
        SchedulePreview();
        ScheduleAutosave();
    }

    private bool _propagatingContext;

    private void OnStepEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Renaming one step's section label renames the whole contiguous visit to
        // that surface/app, so the user edits a category once, not step-by-step.
        if (!_propagatingContext && e.PropertyName == nameof(Step.Context) && sender is Step s)
            PropagateContext(s);
        _dirty = true;
        SchedulePreview();
        ScheduleAutosave();
    }

    private void PropagateContext(Step edited)
    {
        int i = _vm.Steps.IndexOf(edited);
        if (i < 0 || string.IsNullOrWhiteSpace(edited.AutoContext)) return;  // unknown: leave alone

        string visit = edited.AutoContext;
        string value = edited.Context;
        _propagatingContext = true;
        try
        {
            for (int j = i - 1; j >= 0 && SameVisit(_vm.Steps[j], visit); j--)
                _vm.Steps[j].Context = value;
            for (int j = i + 1; j < _vm.Steps.Count && SameVisit(_vm.Steps[j], visit); j++)
                _vm.Steps[j].Context = value;
        }
        finally { _propagatingContext = false; }
    }

    private static bool SameVisit(Step s, string autoContext) =>
        string.Equals(s.AutoContext, autoContext, StringComparison.OrdinalIgnoreCase);

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

    // ---- click a list item -> scroll the preview to that step ---------------
    private void StepCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Step s)
            JumpToPreview(s);
    }

    private void JumpToPreview(Step step)
    {
        var para = FindParagraph(step);
        if (para == null)
        {
            // Preview can be stale (auto-preview off) — rebuild once and retry.
            RefreshPreview();
            para = FindParagraph(step);
        }
        if (para is { } p)
            Dispatcher.BeginInvoke(new Action(() => { try { p.BringIntoView(); } catch { /* ignore */ } }),
                DispatcherPriority.Background);
    }

    private Paragraph? FindParagraph(Step step)
    {
        if (PreviewViewer.Document is not { } doc) return null;
        foreach (var block in doc.Blocks)
            if (block is Paragraph p && ReferenceEquals(p.Tag, step))
                return p;
        return null;
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
        _vm.CompactWhileRecording = _settings.CompactWhileRecording;
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

            if (_vm.CompactWhileRecording)
                EnterCompactMode();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not start recording:\n" + ex.Message,
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopRecording()
    {
        ExitCompactMode();
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
        SaveSession();
        RefreshPreview();
    }

    // ---- compact (minimized) recording bar ----------------------------------
    private void EnterCompactMode()
    {
        var bar = new CompactBar { DataContext = _vm };
        bar.StopClicked += () => ToggleRecording();
        bar.NoteClicked += () => _recorder?.AddNote("");
        bar.Closed += CompactBar_Closed;
        _compact = bar;
        bar.Show();
        Hide();   // tuck the main window away; the bar drives recording
    }

    private void CompactBar_Closed(object? sender, EventArgs e)
    {
        // Bar closed without us tearing it down (e.g. Alt+F4) — stop and restore.
        if (_compact == null) return;
        _compact = null;
        if (_vm.IsRecording) StopRecording();
        else RestoreFromCompact();
    }

    private void ExitCompactMode()
    {
        var bar = _compact;
        _compact = null;
        if (bar != null)
        {
            bar.Closed -= CompactBar_Closed;
            bar.Close();
        }
        RestoreFromCompact();
    }

    private void RestoreFromCompact()
    {
        if (IsVisible) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
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

    private void AnnotateStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Step step) return;
        string? img = step.ImagePath;
        if (string.IsNullOrWhiteSpace(img) || !File.Exists(img))
        {
            MessageBox.Show(this, "This step's image could not be found on disk.",
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Edits apply to the image as currently shown (zoomed variant when the
        // zoom inset is on, plain screenshot otherwise).
        var editor = new AnnotationEditorWindow(img) { Owner = this };
        editor.ShowDialog();
        if (editor.ChangedOnDisk)
        {
            step.RaiseImageChanged();   // reload thumbnail + preview from disk
            _dirty = true;
            ScheduleAutosave();
        }
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs e) => MoveStep(sender, -1);
    private void MoveStepDown_Click(object sender, RoutedEventArgs e) => MoveStep(sender, +1);

    private void MoveStep(object sender, int delta)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Step step) return;
        int i = _vm.Steps.IndexOf(step);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _vm.Steps.Count) return;
        _vm.Steps.Move(i, j);
    }

    // ---- session persistence --------------------------------------------------
    private void ScheduleAutosave()
    {
        if (_sessionDir == null) return; // nothing recorded/opened yet
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSession()
    {
        if (_sessionDir == null) return;
        try
        {
            Directory.CreateDirectory(_sessionDir);
            SessionStore.Save(_sessionDir, _vm.Meta, _vm.Steps.ToList());
        }
        catch { /* autosave is best-effort; the explicit exports are the deliverable */ }
    }

    private void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRecording)
        {
            MessageBox.Show(this, "Stop recording before opening a session.",
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Open a saved session",
            Filter = "WriteUp session (session.json)|session.json|JSON files (*.json)|*.json"
        };
        string initial = FirstExistingDir(_vm.OutputDir, SettingsStore.DefaultSessionsDir);
        if (!string.IsNullOrEmpty(initial)) dlg.InitialDirectory = initial;
        if (dlg.ShowDialog(this) != true) return;

        if (_vm.HasSteps)
        {
            var keep = MessageBox.Show(this,
                "Replace the current steps with the opened session?",
                "Open session", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (keep != MessageBoxResult.OK) return;
            SaveSession(); // flush any pending edits before switching
        }

        try
        {
            var (meta, steps) = SessionStore.Load(dlg.FileName);

            _vm.Steps.Clear();
            SessionStore.ApplyMeta(meta, _vm.Meta);
            foreach (var s in steps) _vm.Steps.Add(s);

            // Future edits/annotations/autosaves belong to the opened session.
            _sessionDir = Path.GetDirectoryName(dlg.FileName);
            _saveTimer.Stop();  // opening isn't an edit; don't rewrite immediately
            _dirty = false;     // freshly-opened = nothing unexported *and changed* yet

            int missing = steps.Count(s => s.Kind == StepKind.Click && !s.HasScreenshot);
            RefreshPreview();

            if (missing > 0)
                MessageBox.Show(this,
                    $"Opened, but {missing} step(s) reference images that could not be found. " +
                    "Their captions are intact.",
                    "Open session", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open the session:\n" + ex.Message,
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        SettingsStore.Save(_settings);
        if (dlg.TourRequested) StartTour();
    }

    // ---- settings -----------------------------------------------------------
    private void PersistSettings()
    {
        _settings.DefaultAuthor = _vm.Meta.Author;
        _settings.DefaultDepartment = _vm.Meta.Department;
        _settings.AlwaysOnTop = _vm.AlwaysOnTop;
        _settings.CompactWhileRecording = _vm.CompactWhileRecording;
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
        _dirty = false;   // current steps are now saved to a report
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
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeConfirmed) return;

        // Finish any in-progress recording so the steps are final before we ask.
        if (_vm.IsRecording) StopRecording();

        if (!_vm.HasSteps || !_dirty) return;   // nothing unsaved to lose

        var result = MessageBox.Show(this,
            $"You have {_vm.StepCount} recorded step(s) that haven't been exported.\n\n" +
            "Export before closing?",
            "WriteUp", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;        // stay open
            return;
        }
        if (result == MessageBoxResult.Yes)
        {
            e.Cancel = true;        // hold the close until the export is resolved
            if (ExportForClose())   // user chose a file and it saved
            {
                _closeConfirmed = true;
                Close();            // re-close; the guards above now pass
            }
            return;
        }
        // No → close and discard.
        _closeConfirmed = true;
    }

    /// <summary>Run the PDF export as part of closing. Returns true if it saved,
    /// false if the user cancelled the dialog or it failed (so we keep the app
    /// open rather than lose the work).</summary>
    private bool ExportForClose()
    {
        try
        {
            string? path = AskSavePath("Save PDF as", "PDF document (*.pdf)|*.pdf", "pdf");
            if (path == null) return false;
            DocumentExporter.SavePdf(_vm.Meta, _vm.Steps.ToList(), path);
            RememberExportDir(path);
            OpenFile(path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PDF export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            if (_compact != null)
            {
                _compact.Closed -= CompactBar_Closed;
                _compact.Close();
                _compact = null;
            }
            if (_source != null)
            {
                var helper = new WindowInteropHelper(this);
                NativeMethods.UnregisterHotKey(helper.Handle, HotkeyId);
                _source.RemoveHook(WndProc);
            }
            _recorder?.Dispose();
            PersistSettings();
            SaveSession();

            // Don't let capture scratch space pile up on disk — only when the
            // user opted in; otherwise sessions stay reopenable via 📂 Open….
            if (_settings.CleanupSessionsOnExit)
            {
                SessionCleanup.Delete(_sessionDir);
                SessionCleanup.PurgeOrphans(_vm.OutputDir);
            }
        }
        catch { /* ignore */ }
        base.OnClosed(e);
    }
}
