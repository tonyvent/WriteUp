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
    private readonly StepHistory _history = new();
    private bool _restoringHistory;
    private bool _stopping;
    private NarrationService? _narration;
    private readonly List<(DateTime at, string text)> _pendingNarration = new();
    private DateTime _recordingStart;
    private bool _dirty;            // recorded steps changed since the last export
    private bool _closeConfirmed;   // the export-on-close prompt has been resolved

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsStore.Load();
        ApplySettingsToUi();
        DataContext = _vm;
        _history.Reset(_vm.Steps);

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
        RememberHistory();
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
        RememberHistory();
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
        NativeMethods.RegisterHotKey(helper.Handle, HotkeyId + 1,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, 0x53);
        NativeMethods.RegisterHotKey(helper.Handle, HotkeyId + 2,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, 0x41);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleRecording();
            handled = true;
        }
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() is var id && (id == HotkeyId + 1 || id == HotkeyId + 2))
        { _recorder?.CaptureNow(id == HotkeyId + 2); handled = true; }
        return IntPtr.Zero;
    }

    // ---- recording ----------------------------------------------------------
    private void RecordBtn_Click(object sender, RoutedEventArgs e) => ToggleRecording();

    private async void ToggleRecording()
    {
        if (_stopping) return;
        if (_vm.IsRecording) await StopRecording();
        else if (!_narrationStarting) await StartRecording();
    }

    private async Task StartRecording()
    {
        try
        {
            string root = string.IsNullOrWhiteSpace(_vm.OutputDir)
                ? SettingsStore.DefaultSessionsDir : _vm.OutputDir;
            _sessionDir ??= Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_sessionDir);

            _recorder = new Recorder(Dispatcher, _sessionDir, _settings.MaxImageWidth, _settings);
            _recorder.StepAdded += OnStepAdded;
            _recorder.NarrationReady += AttachNarration;
            _recordingStart = DateTime.Now;
            _recorder.CaptureWarning += message => _vm.RecordingNotice = "Capture issue: " + message;
            _recorder.Start();

            _startTime = DateTime.Now;
            _vm.Elapsed = "00:00";
            _vm.IsRecording = true;
            _vm.RecordingNotice = "Recording actions. Speak to add business context alongside your steps.";
            _vm.LiveTranscript = "";
            if (_settings.NarrationEnabled) await StartNarration();
            else _vm.RecordingNotice = "Recording actions. Microphone is disabled in Settings.";
            if (!_vm.IsRecording || _stopping) return;
            _timer.Start();

            if (_vm.CompactWhileRecording)
                EnterCompactMode();
        }
        catch (Exception ex)
        {
            _recorder?.Dispose(); _recorder = null; _vm.IsRecording = false;
            MessageBox.Show(this, "Could not start recording:\n" + ex.Message,
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopRecording()
    {
        if (_stopping) return;
        _stopping = true;
        RecordBtn.IsEnabled = false;
        try
        {
            ExitCompactMode();
            _timer.Stop();
            if (_recorder != null)
            {
                await _recorder.StopAsync();
                _recorder.StepAdded -= OnStepAdded;
                _recorder.Dispose();
                _recorder = null;
            }
            if (_narration != null) { await _narration.StopAsync(); _narration = null; }
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            foreach (var (at, text) in _pendingNarration) AttachNarration(at, text);
            _pendingNarration.Clear();
            _vm.MicrophoneLabel = "Microphone off"; _vm.AudioLevel = 0;
            _vm.IsRecording = false;
            _history.Reset(_vm.Steps);
            PersistSettings();
            SaveSession();
            RefreshPreview();
        }
        finally { _stopping = false; RecordBtn.IsEnabled = true; }
    }

    // ---- compact (minimized) recording bar ----------------------------------
    private void EnterCompactMode()
    {
        var bar = new CompactBar { DataContext = _vm };
        bar.StopClicked += () => ToggleRecording();
        bar.NoteClicked += () => _recorder?.AddNote("");
        bar.NarrationClicked += () => Narrate_Click(this, new RoutedEventArgs());
        bar.Closed += CompactBar_Closed;
        _compact = bar;
        bar.Show();
        Hide();   // tuck the main window away; the bar drives recording
    }

    private async void CompactBar_Closed(object? sender, EventArgs e)
    {
        // Bar closed without us tearing it down (e.g. Alt+F4) — stop and restore.
        if (_compact == null) return;
        _compact = null;
        if (_vm.IsRecording) await StopRecording();
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
        EnsureSession();
        _vm.Steps.Add(new Step { Kind = StepKind.Note, Caption = "New instruction" });
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRecording) return;
        if (sender is FrameworkElement fe && fe.DataContext is Step step)
        {
            // Keep a shared image reachable if its original step is removed.
            _restoringHistory = true;
            var dependents = _vm.Steps.Where(s => s.SharedImageStepId == step.Id).ToList();
            if (dependents.Count > 0)
            {
                var owner = dependents[0];
                owner.ScreenshotPath = step.ScreenshotPath; owner.ZoomImagePath = step.ZoomImagePath;
                owner.ShowZoom = step.ShowZoom; owner.HideImage = false; owner.SharedImageStepId = null;
                foreach (var child in dependents.Skip(1)) child.SharedImageStepId = owner.Id;
                owner.RaiseImageChanged();
            }
            _vm.Steps.Remove(step);
            _restoringHistory = false;
            RememberHistory();
        }
    }

    private void AnnotateStep_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRecording) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not Step step) return;
        string? img = step.ImagePath;
        if (string.IsNullOrWhiteSpace(img) || !File.Exists(img))
        {
            MessageBox.Show(this, "This step's image could not be found on disk.",
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // The editor opens on the shown variant and can switch plain/zoom inset.
        var editor = new AnnotationEditorWindow(step) { Owner = this };
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
        if (_vm.IsRecording) return;
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

            _restoringHistory = true;
            foreach (var old in _vm.Steps) old.PropertyChanged -= OnStepEdited;
            _vm.Steps.Clear();
            SessionStore.ApplyMeta(meta, _vm.Meta);
            foreach (var s in steps) _vm.Steps.Add(s);
            _restoringHistory = false;
            _history.Reset(_vm.Steps);

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
        if (_vm.IsRecording || _stopping)
        { MessageBox.Show(this, "Stop recording before changing microphone or recording settings.", "Settings"); return; }
        _settings.CompactWhileRecording = _vm.CompactWhileRecording;
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        SettingsStore.Save(_settings);
        _vm.CompactWhileRecording = _settings.CompactWhileRecording;
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

    // ---- shutdown -----------------------------------------------------------
    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeConfirmed) return;

        // Finish any in-progress recording so the steps are final before we ask.
        if (_vm.IsRecording || _stopping)
        {
            e.Cancel = true;
            if (_stopping) return;
            await StopRecording();
            Close();
            return;
        }

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
                NativeMethods.UnregisterHotKey(helper.Handle, HotkeyId + 1);
                NativeMethods.UnregisterHotKey(helper.Handle, HotkeyId + 2);
                _source.RemoveHook(WndProc);
            }
            _narration?.Dispose();
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

    private void EnsureSession()
    {
        _sessionDir ??= Path.Combine(_vm.OutputDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sessionDir);
    }

    private void RememberHistory()
    {
        if (!_vm.IsRecording && !_restoringHistory && !_propagatingContext) _history.Record(_vm.Steps);
    }
    private void RestoreHistory(List<Step>? steps)
    {
        if (steps == null) return;
        _restoringHistory = true;
        try
        {
            foreach (var old in _vm.Steps) old.PropertyChanged -= OnStepEdited;
            _vm.Steps.Clear();
            foreach (var s in steps) _vm.Steps.Add(s);
        }
        finally { _restoringHistory = false; }
        RefreshPreview();
    }
    private void UndoSteps_Click(object sender, RoutedEventArgs e) { if (!_vm.IsRecording) RestoreHistory(_history.Undo()); }
    private void RedoSteps_Click(object sender, RoutedEventArgs e) { if (!_vm.IsRecording) RestoreHistory(_history.Redo()); }
    private void EditStep_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRecording || sender is not FrameworkElement fe || fe.DataContext is not Step step) return;
        _restoringHistory = true;
        try { new StepEditorWindow(step, _vm.Steps.ToList()) { Owner = this }.ShowDialog(); }
        finally { _restoringHistory = false; RememberHistory(); }
    }
    private void CaptureScreens_Click(object sender, RoutedEventArgs e) => _recorder?.CaptureNow(true);
    private async void Narrate_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsRecording || _stopping || _narrationStarting || !NarrateBtn.IsEnabled) return;
        NarrateBtn.IsEnabled = false;
        try
        {
            if (_narration != null && !_narration.IsListening)
            { _narration.Dispose(); _narration = null; }
            if (_narration != null)
            {
                await _narration.StopAsync(); _narration = null;
                _vm.MicrophoneLabel = "Microphone off"; _vm.AudioLevel = 0;
            }
            else
            {
                await StartNarration();
            }
        }
        catch (Exception ex)
        {
            _narration?.Dispose(); _narration = null;
            _vm.MicrophoneLabel = "Microphone off"; _vm.AudioLevel = 0;
            MessageBox.Show(this, "Could not start narration: " + ex.Message, "Narration");
        }
        finally { NarrateBtn.IsEnabled = true; }
    }
    private bool _narrationStarting;
    private async Task StartNarration()
    {
        if (_narrationStarting) return;
        _narrationStarting = true;
        var narration = new NarrationService();
        _narration = narration;
        void Update(Action action) => Dispatcher.BeginInvoke(new Action(() =>
        { if (ReferenceEquals(_narration, narration)) action(); }));
        narration.Transcribed += (at, text) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_stopping) _pendingNarration.Add((at, text));
            else _recorder?.AddNarration(at, text);
            _vm.LiveTranscript = "Transcribed: " + text;
        }));
        narration.Hypothesis += text => Update(() => { if (text.Length > 0) _vm.LiveTranscript = "Hearing: " + text; });
        narration.AudioLevel += level => Update(() => _vm.AudioLevel = level);
        narration.Status += text => Update(() => _vm.RecordingNotice = text);
        narration.Ended += () => Update(() => { _vm.MicrophoneLabel = "Microphone off"; _vm.AudioLevel = 0; _vm.AudioLevel = 0; });
        try
        {
            await narration.StartAsync(_settings);
            if (!_stopping && ReferenceEquals(_narration, narration)) _vm.MicrophoneLabel = "Microphone ON";
        }
        catch (Exception ex)
        {
            narration.Dispose(); _narration = null;
            _vm.MicrophoneLabel = "Microphone off"; _vm.AudioLevel = 0;
            _vm.RecordingNotice = "Screen capture is running. Narration unavailable: " + ex.Message + " Open Settings after stopping to test your microphone.";
        }
        finally { _narrationStarting = false; }
    }

    private void AttachNarration(DateTime at, string text)
    {
        var step = _vm.Steps.Where(s => s.Timestamp >= _recordingStart && s.Timestamp <= at).OrderBy(s => s.Timestamp).LastOrDefault();
        if (step == null)
        {
            step = new Step { Kind = StepKind.Note, Timestamp = at, Caption = "Business context" };
            int index = _vm.Steps.TakeWhile(s => s.Timestamp <= at).Count();
            _vm.Steps.Insert(index, step);
        }
        step.Notes = string.IsNullOrWhiteSpace(step.Notes) ? text : step.Notes + "\n" + text;
    }
}
