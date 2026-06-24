using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>
/// Turns raw input events into ordered <see cref="Step"/>s. Hook callbacks
/// enqueue lightweight events; a single background worker captures screenshots
/// and builds steps in order, then raises <see cref="StepAdded"/> on the UI
/// thread via the supplied dispatcher.
/// </summary>
public sealed class Recorder : IDisposable
{
    private enum RawKind { Click, Text, Special, Note, Flush }

    private sealed class RawEvent
    {
        public RawKind Kind;
        public int X;
        public int Y;
        public ClickButton Button;
        public int ForegroundPid;   // active app at the instant of the click
        public string Payload = ""; // typed char, special-key name, or note caption
    }

    public event Action<Step>? StepAdded;

    private readonly Dispatcher _dispatcher;
    private readonly string _shotsDir;
    private readonly int _maxImageWidth;
    private readonly InputHook _hook = new();
    private readonly BlockingCollection<RawEvent> _queue = new();
    private Task? _worker;

    // Typed-text accumulation (worker thread only).
    private readonly StringBuilder _typed = new();
    private bool _typing;
    private bool _typedIgnore;          // typing happened in our own window — drop it
    private string _typedApp = "";
    private string _typedWindow = "";
    private string _typedField = "";   // focused control name when typing began

    // Focus tracking (worker thread only) so we can ignore our own window and
    // clicks that merely switch/activate a different application. Tracked by
    // process so in-app dialogs/menus (same process) still record normally.
    private int _ownPid;
    private string _lastApp = "";       // last external app we recorded in,
    private string _lastWindow = "";    // its window title, and
    private string _lastContext = "";   // its section context (surface/app) — reused so
                                        // typing/keys/notes stay in the same section

    public Recorder(Dispatcher dispatcher, string sessionDir, int maxImageWidth)
    {
        _dispatcher = dispatcher;
        _shotsDir = Path.Combine(sessionDir, "screenshots");
        _maxImageWidth = maxImageWidth;
        Directory.CreateDirectory(_shotsDir);
    }

    public void Start()
    {
        _ownPid = Environment.ProcessId;
        _hook.MouseClicked += OnMouseClicked;
        _hook.TextTyped += OnTextTyped;
        _hook.SpecialKey += OnSpecialKey;
        _worker = Task.Factory.StartNew(Consume, TaskCreationOptions.LongRunning);
        _hook.Start();
    }

    public void Stop()
    {
        _hook.Stop();
        _hook.MouseClicked -= OnMouseClicked;
        _hook.TextTyped -= OnTextTyped;
        _hook.SpecialKey -= OnSpecialKey;

        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(new RawEvent { Kind = RawKind.Flush });
            _queue.CompleteAdding();
        }
        try { _worker?.Wait(2000); } catch { /* ignore */ }
    }

    /// <summary>Insert a manual note/checkpoint with a screenshot of the screen.</summary>
    public void AddNote(string caption)
    {
        if (_queue.IsAddingCompleted) return;
        _queue.Add(new RawEvent { Kind = RawKind.Note, Payload = caption });
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
        _queue.Dispose();
    }

    // ---- hook handlers (UI thread) -> enqueue -------------------------------
    private void OnMouseClicked(int x, int y, ClickButton button)
    {
        if (_queue.IsAddingCompleted) return;
        // Capture the active app *now*, before this click activates anything, so
        // we can tell a real in-app click from one that just switches apps.
        int fgPid = WindowTracker.ProcessIdOf(NativeMethods.GetForegroundWindow());
        _queue.Add(new RawEvent { Kind = RawKind.Click, X = x, Y = y, Button = button, ForegroundPid = fgPid });
    }

    private void OnTextTyped(string ch)
    {
        if (_queue.IsAddingCompleted) return;
        _queue.Add(new RawEvent { Kind = RawKind.Text, Payload = ch });
    }

    private void OnSpecialKey(string name)
    {
        if (_queue.IsAddingCompleted) return;
        _queue.Add(new RawEvent { Kind = RawKind.Special, Payload = name });
    }

    // ---- worker (background thread) -----------------------------------------
    private void Consume()
    {
        foreach (var ev in _queue.GetConsumingEnumerable())
        {
            switch (ev.Kind)
            {
                case RawKind.Click:
                    FlushTyped();
                    EmitClick(ev);
                    break;
                case RawKind.Text:
                    if (!_typing)
                    {
                        _typing = true;
                        var (hwnd, window, app) = WindowTracker.GetActiveInfo();
                        int pid = WindowTracker.ProcessIdOf(hwnd);
                        _typedWindow = window;
                        _typedApp = app;
                        _typedField = UiaInspector.FocusedName();
                        _typedIgnore = pid == _ownPid;   // typing inside WriteUp itself
                    }
                    if (!_typedIgnore) _typed.Append(ev.Payload);
                    break;
                case RawKind.Special:
                    HandleSpecial(ev.Payload);
                    break;
                case RawKind.Note:
                    FlushTyped();
                    EmitNote(ev.Payload);
                    break;
                case RawKind.Flush:
                    FlushTyped();
                    break;
            }
        }
        FlushTyped();
    }

    private void HandleSpecial(string name)
    {
        if (name == "backspace")
        {
            if (_typing && _typed.Length > 0)
                _typed.Remove(_typed.Length - 1, 1);
            return;
        }

        FlushTyped();
        var (hwnd, window, app) = WindowTracker.GetActiveInfo();
        int pid = WindowTracker.ProcessIdOf(hwnd);
        if (pid == _ownPid) return;  // ignore keys pressed in WriteUp itself

        string ctx = ContextFor(app);
        Remember(app, window, ctx);

        string pretty = name switch
        {
            "enter" => "Enter",
            "tab" => "Tab",
            "esc" => "Esc",
            _ => name
        };
        Emit(new Step
        {
            Kind = StepKind.Key,
            Window = window,
            App = app,
            Context = ctx,
            AutoContext = ctx,
            Caption = $"Press {pretty}."
        });
    }

    /// <summary>Section context for a non-click step: stay in the last recorded
    /// surface (e.g. a palette) when still in the same app, else just the app.</summary>
    private string ContextFor(string app) =>
        !string.IsNullOrEmpty(_lastContext) && string.Equals(app, _lastApp, StringComparison.OrdinalIgnoreCase)
            ? _lastContext : app;

    private void Remember(string app, string window, string ctx)
    {
        _lastApp = app;
        _lastWindow = window;
        _lastContext = ctx;
    }

    private void EmitClick(RawEvent ev)
    {
        // Resolve the top-level window actually under the cursor (not the
        // post-click foreground, which races with activation).
        IntPtr target = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = ev.X, y = ev.Y });
        IntPtr root = target != IntPtr.Zero ? NativeMethods.GetAncestor(target, NativeMethods.GA_ROOT) : IntPtr.Zero;
        int targetPid = WindowTracker.ProcessIdOf(root);

        // Skip clicks that aren't real actions:
        //  • clicks inside WriteUp itself, and
        //  • clicks on a window that wasn't the active app — i.e. the click just
        //    switched/activated another application, which isn't a step.
        // In-app dialogs/menus share the active app's process, so they record.
        bool ours = targetPid == _ownPid;
        bool switchedApp = ev.ForegroundPid != 0 && targetPid != ev.ForegroundPid;
        if (ours || switchedApp)
            return;

        var (window, app) = WindowTracker.DescribeWindow(root);

        // Ask UI Automation what's under the cursor so we can name the control and
        // outline it in the screenshot, and find the specific surface (palette /
        // pane / window) for a descriptive section. Best-effort.
        var el = UiaInspector.Describe(ev.X, ev.Y);

        // Section context: the specific surface when known ("TOOLSPACE palette",
        // "Pipe Network window"), otherwise the app name.
        string ctx = !string.IsNullOrWhiteSpace(el?.Surface) ? el!.Surface : app;
        Remember(app, window, ctx);

        string? baseShot = null, zoomShot = null;
        try
        {
            (baseShot, zoomShot) = ScreenCapturer.CaptureClick(_shotsDir, ev.X, ev.Y,
                el?.Bounds ?? System.Drawing.Rectangle.Empty, _maxImageWidth);
        }
        catch { /* keep the step even if capture fails */ }

        Emit(new Step
        {
            Kind = StepKind.Click,
            Window = window,
            App = app,
            Context = ctx,
            AutoContext = ctx,
            Button = ev.Button.ToString().ToLowerInvariant(),
            X = ev.X,
            Y = ev.Y,
            ScreenshotPath = baseShot,
            ZoomImagePath = zoomShot,
            Caption = ClickCaption(ev.Button, app, el)
        });
    }

    /// <summary>Turn the clicked control (when UI Automation found one) into a
    /// readable instruction; otherwise fall back to a location-based caption.</summary>
    private static string ClickCaption(ClickButton button, string app, UiaInspector.ElementInfo? el)
    {
        string verb = button switch
        {
            ClickButton.Right => "Right-click",
            ClickButton.Middle => "Middle-click",
            _ => "Click"
        };
        string appPart = string.IsNullOrWhiteSpace(app) ? "" : $" in {app}";

        string name = el?.Name?.Trim() ?? "";
        string type = el?.ControlType ?? "";

        if (name.Length is > 0 and <= 60)
        {
            return type switch
            {
                "menu item" => button == ClickButton.Left
                    ? $"Select “{name}” from the menu{appPart}."
                    : $"{verb} the “{name}” menu item{appPart}.",
                "tab"      => $"Switch to the “{name}” tab{appPart}.",
                "field"    => $"{verb} the “{name}” field{appPart}.",
                "link"     => $"{verb} the “{name}” link{appPart}.",
                "checkbox" => $"{verb} the “{name}” checkbox{appPart}.",
                "option"   => $"{verb} the “{name}” option{appPart}.",
                "dropdown" => $"{verb} the “{name}” dropdown{appPart}.",
                "button"   => $"{verb} the “{name}” button{appPart}.",
                "" or "label" => $"{verb} “{name}”{appPart}.",
                _          => $"{verb} the “{name}” {type}{appPart}."
            };
        }

        // No usable name — still mention the kind of control if we know it.
        if (type.Length > 0 && type != "field" && type != "label")
            return $"{verb} the highlighted {type}{appPart}.";

        string appName = string.IsNullOrWhiteSpace(app) ? "the application" : app;
        return $"{verb} at the highlighted location in {appName}.";
    }

    private void EmitNote(string caption)
    {
        // A note is added from WriteUp's own button, so the foreground would be
        // WriteUp — keep the note in the section of the last app the user worked in.
        string app = _lastApp;
        string window = _lastWindow;
        string? shot = null;
        try
        {
            shot = ScreenCapturer.CaptureContext(_shotsDir, _maxImageWidth);
        }
        catch { /* ignore */ }

        string ctx = !string.IsNullOrEmpty(_lastContext) ? _lastContext : app;
        Emit(new Step
        {
            Kind = StepKind.Note,
            Window = window,
            App = app,
            Context = ctx,
            AutoContext = ctx,
            ScreenshotPath = shot,
            Caption = caption
        });
    }

    private void FlushTyped()
    {
        bool ignore = _typedIgnore;
        if (!_typing || _typed.Length == 0 || ignore)
        {
            _typing = false;
            _typedIgnore = false;
            _typed.Clear();
            _typedField = "";
            return;
        }
        string text = _typed.ToString();
        string field = _typedField;
        _typed.Clear();
        _typing = false;
        _typedField = "";

        if (string.IsNullOrWhiteSpace(text)) return;

        string into = field.Length is > 0 and <= 60 ? $" into the \u201c{field}\u201d field" : "";

        string ctx = ContextFor(_typedApp);
        Remember(_typedApp, _typedWindow, ctx);
        Emit(new Step
        {
            Kind = StepKind.Type,
            Window = _typedWindow,
            App = _typedApp,
            Context = ctx,
            AutoContext = ctx,
            Caption = $"Type \u201c{text}\u201d{into}."
        });
    }

    private void Emit(Step step)
    {
        // BeginInvoke (not Invoke) so the worker never blocks on the UI thread.
        // Stop() waits on this worker, and a blocking Invoke here would deadlock
        // until the timeout. Dispatcher keeps these in FIFO order.
        try
        {
            _dispatcher.BeginInvoke(new Action(() => StepAdded?.Invoke(step)));
        }
        catch (TaskCanceledException) { /* shutting down */ }
    }
}
