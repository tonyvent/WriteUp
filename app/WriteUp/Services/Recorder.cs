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
    private enum RawKind { Click, MidUp, Wheel, Text, Special, Note, Flush }

    private sealed class RawEvent
    {
        public RawKind Kind;
        public int X;
        public int Y;
        public ClickButton Button;
        public int ForegroundPid;   // active app at the instant of the event
        public int Delta;           // wheel rotation (+up / -down)
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

    // Double-click detection (worker thread only).
    private uint _dblClickMs = 500;
    private Step? _lastClickStep;
    private ClickButton _lastClickButton;
    private int _lastClickTick;
    private int _lastClickX, _lastClickY;

    // Middle button is deferred to its release so we can tell a pan (drag) from a click.
    private RawEvent? _midDown;
    private const int PanThreshold = 8; // px of movement that counts as a pan

    // Mouse-wheel accumulation, so a burst of scrolling becomes one step.
    private bool _wheelActive;
    private int _wheelDelta, _wheelX, _wheelY, _wheelFgPid;

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
        try { _dblClickMs = NativeMethods.GetDoubleClickTime(); } catch { /* keep default */ }
        _hook.MouseDown += OnMouseDown;
        _hook.MouseUp += OnMouseUp;
        _hook.MouseWheel += OnMouseWheel;
        _hook.TextTyped += OnTextTyped;
        _hook.SpecialKey += OnSpecialKey;
        _worker = Task.Factory.StartNew(Consume, TaskCreationOptions.LongRunning);
        _hook.Start();
    }

    public void Stop()
    {
        _hook.Stop();
        _hook.MouseDown -= OnMouseDown;
        _hook.MouseUp -= OnMouseUp;
        _hook.MouseWheel -= OnMouseWheel;
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
    private void OnMouseDown(int x, int y, ClickButton button)
    {
        if (_queue.IsAddingCompleted) return;
        // Capture the active app *now*, before this click activates anything, so
        // we can tell a real in-app click from one that just switches apps.
        int fgPid = WindowTracker.ProcessIdOf(NativeMethods.GetForegroundWindow());
        _queue.Add(new RawEvent { Kind = RawKind.Click, X = x, Y = y, Button = button, ForegroundPid = fgPid });
    }

    private void OnMouseUp(int x, int y, ClickButton button)
    {
        if (_queue.IsAddingCompleted || button != ClickButton.Middle) return;
        _queue.Add(new RawEvent { Kind = RawKind.MidUp, X = x, Y = y, Button = button });
    }

    private void OnMouseWheel(int x, int y, int delta)
    {
        if (_queue.IsAddingCompleted) return;
        int fgPid = WindowTracker.ProcessIdOf(NativeMethods.GetForegroundWindow());
        _queue.Add(new RawEvent { Kind = RawKind.Wheel, X = x, Y = y, Delta = delta, ForegroundPid = fgPid });
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
                    FlushWheel();
                    if (ev.Button == ClickButton.Middle)
                        _midDown = ev;                 // defer: pan or click is known at release
                    else
                        HandleLeftRightClick(ev);
                    break;
                case RawKind.MidUp:
                    FlushTyped();
                    FlushWheel();
                    HandleMiddleUp(ev);
                    break;
                case RawKind.Wheel:
                    FlushTyped();
                    AccumulateWheel(ev);               // FlushWheel emits the merged step later
                    break;
                case RawKind.Text:
                    FlushWheel();
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
                    FlushWheel();
                    HandleSpecial(ev.Payload);
                    break;
                case RawKind.Note:
                    FlushTyped();
                    FlushWheel();
                    EmitNote(ev.Payload);
                    break;
                case RawKind.Flush:
                    FlushTyped();
                    FlushWheel();
                    break;
            }
        }
        FlushTyped();
        FlushWheel();
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

    /// <summary>Resolve the top-level window under a point and decide whether it's
    /// a real in-app action (not our own window, not an app-switch). Returns false
    /// to skip; otherwise yields the window title and generic app name.</summary>
    private bool PassesSkip(int x, int y, int foregroundPid, out string window, out string app)
    {
        window = ""; app = "";
        // WindowFromPoint (not the post-click foreground, which races with activation).
        IntPtr target = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        IntPtr root = target != IntPtr.Zero ? NativeMethods.GetAncestor(target, NativeMethods.GA_ROOT) : IntPtr.Zero;
        int targetPid = WindowTracker.ProcessIdOf(root);

        bool ours = targetPid == _ownPid;
        bool switchedApp = foregroundPid != 0 && targetPid != foregroundPid;
        if (ours || switchedApp) return false;

        (window, app) = WindowTracker.DescribeWindow(root);
        return true;
    }

    /// <summary>Left/right click: emit a step, but fold a fast second click in the
    /// same spot into a double-click rather than two separate steps.</summary>
    private void HandleLeftRightClick(RawEvent ev)
    {
        int now = Environment.TickCount;
        bool isDouble = _lastClickStep != null
            && ev.Button == _lastClickButton
            && unchecked((uint)(now - _lastClickTick)) <= _dblClickMs
            && Math.Abs(ev.X - _lastClickX) <= 4
            && Math.Abs(ev.Y - _lastClickY) <= 4;

        if (isDouble)
        {
            UpgradeToDouble(_lastClickStep!);
            _lastClickStep = null;   // a third click starts fresh
            return;
        }

        var step = EmitClick(ev);
        _lastClickStep = step;
        _lastClickButton = ev.Button;
        _lastClickTick = now;
        _lastClickX = ev.X;
        _lastClickY = ev.Y;
    }

    /// <summary>Middle release: a pan when the cursor moved, otherwise a middle click.</summary>
    private void HandleMiddleUp(RawEvent up)
    {
        var down = _midDown;
        _midDown = null;
        if (down == null) return;

        if (Math.Abs(up.X - down.X) + Math.Abs(up.Y - down.Y) >= PanThreshold)
            EmitPan(down);
        else
            EmitClick(down);   // a plain middle click
    }

    private Step? EmitClick(RawEvent ev)
    {
        if (!PassesSkip(ev.X, ev.Y, ev.ForegroundPid, out string window, out string app))
            return null;

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

        var step = new Step
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
        };
        Emit(step);
        return step;
    }

    /// <summary>A middle-button drag — recorded as a pan of the view.</summary>
    private void EmitPan(RawEvent down)
    {
        if (!PassesSkip(down.X, down.Y, down.ForegroundPid, out string window, out string app))
            return;

        var el = UiaInspector.Describe(down.X, down.Y);
        string ctx = !string.IsNullOrWhiteSpace(el?.Surface) ? el!.Surface : app;
        Remember(app, window, ctx);

        string? shot = null;
        try { shot = ScreenCapturer.CaptureContext(_shotsDir, _maxImageWidth); }
        catch { /* ignore */ }

        string appPart = string.IsNullOrWhiteSpace(ctx) ? "" : $" in {ctx}";
        Emit(new Step
        {
            Kind = StepKind.Pan,
            Window = window,
            App = app,
            Context = ctx,
            AutoContext = ctx,
            ScreenshotPath = shot,
            Caption = $"Pan the view{appPart} (middle-button drag)."
        });
    }

    /// <summary>Promote the previous click step into a double-click.</summary>
    private void UpgradeToDouble(Step s)
    {
        string c = s.Caption;
        string nc =
            c.StartsWith("Right-click", StringComparison.Ordinal) ? "Double-right-click" + c.Substring("Right-click".Length) :
            c.StartsWith("Click", StringComparison.Ordinal) ? "Double-click" + c.Substring("Click".Length) :
            "Double-click — " + c;
        try { _dispatcher.BeginInvoke(new Action(() => s.Caption = nc)); }
        catch (TaskCanceledException) { /* shutting down */ }
    }

    // ---- mouse wheel --------------------------------------------------------
    private void AccumulateWheel(RawEvent ev)
    {
        if (!_wheelActive)
        {
            _wheelActive = true;
            _wheelDelta = 0;
            _wheelX = ev.X; _wheelY = ev.Y; _wheelFgPid = ev.ForegroundPid;
        }
        _wheelDelta += ev.Delta;
    }

    private void FlushWheel()
    {
        if (!_wheelActive) return;
        int delta = _wheelDelta, x = _wheelX, y = _wheelY, fg = _wheelFgPid;
        _wheelActive = false;
        _wheelDelta = 0;
        if (delta == 0) return;

        if (!PassesSkip(x, y, fg, out string window, out string app)) return;

        var el = UiaInspector.Describe(x, y);
        string ctx = !string.IsNullOrWhiteSpace(el?.Surface) ? el!.Surface : app;
        Remember(app, window, ctx);

        string? shot = null;
        try { shot = ScreenCapturer.CaptureContext(_shotsDir, _maxImageWidth); }
        catch { /* ignore */ }

        string dir = delta > 0 ? "up" : "down";
        Emit(new Step
        {
            Kind = StepKind.Scroll,
            Window = window,
            App = app,
            Context = ctx,
            AutoContext = ctx,
            ScreenshotPath = shot,
            Caption = $"Scroll {dir} (mouse wheel)."
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
