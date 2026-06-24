using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace WriteUp.Services;

/// <summary>
/// Best-effort UI Automation lookup: identifies the control under the cursor (or
/// the focused field) so a step can say "Click the <b>Save</b> button" instead of
/// "click here". Every call is guarded with a timeout because some apps answer
/// UI Automation slowly — a miss just falls back to a generic caption.
/// </summary>
public static class UiaInspector
{
    public sealed class ElementInfo
    {
        public string Name = "";
        public string ControlType = "";
        public Rectangle Bounds;   // screen pixels; empty when unknown
        public string Surface = ""; // descriptive container, e.g. "TOOLSPACE palette"
    }

    private const int TimeoutMs = 800;

    /// <summary>Describe the control at screen point (x, y), including the
    /// descriptive container surface (palette/pane/window) it lives in.</summary>
    public static ElementInfo? Describe(int x, int y) => RunGuarded(() =>
    {
        var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
        if (el == null) return null;
        var info = Read(el);
        info.Surface = FindSurface(el) ?? "";
        return info;
    });

    /// <summary>Name of the currently focused control (e.g. the field being typed into).</summary>
    public static string FocusedName()
    {
        var info = RunGuarded(() =>
        {
            var el = AutomationElement.FocusedElement;
            return el == null ? null : Read(el);
        });
        return info?.Name ?? "";
    }

    private static ElementInfo Read(AutomationElement el)
    {
        var info = new ElementInfo();
        try { info.Name = (el.Current.Name ?? "").Trim(); } catch { /* ignore */ }
        try { info.ControlType = Friendly(el.Current.ControlType); } catch { /* ignore */ }
        try
        {
            var r = el.Current.BoundingRectangle;
            if (!r.IsEmpty && r.Width > 0 && r.Height > 0 &&
                !double.IsInfinity(r.Width) && !double.IsInfinity(r.Height))
                info.Bounds = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        }
        catch { /* ignore */ }
        return info;
    }

    /// <summary>Map UI Automation control types to plain words for captions.</summary>
    private static string Friendly(ControlType t)
    {
        if (t == ControlType.Button || t == ControlType.SplitButton) return "button";
        if (t == ControlType.CheckBox) return "checkbox";
        if (t == ControlType.RadioButton) return "option";
        if (t == ControlType.MenuItem) return "menu item";
        if (t == ControlType.TabItem) return "tab";
        if (t == ControlType.Edit || t == ControlType.Document) return "field";
        if (t == ControlType.ComboBox) return "dropdown";
        if (t == ControlType.Hyperlink) return "link";
        if (t == ControlType.ListItem) return "item";
        if (t == ControlType.TreeItem) return "item";
        if (t == ControlType.Text) return "label";
        if (t == ControlType.Slider) return "slider";
        if (t == ControlType.Spinner) return "spinner";
        try { return t.ProgrammaticName.Replace("ControlType.", "").ToLowerInvariant(); }
        catch { return ""; }
    }

    /// <summary>Walk up from the clicked element to find a descriptive container —
    /// a named palette/pane/window/toolbar — so a step can say "in the TOOLSPACE
    /// palette" or "in the Pipe Network window" instead of just the app name.
    /// Returns null when nothing more specific than the main window is found.</summary>
    private static string? FindSurface(AutomationElement start)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var node = start;
            for (int depth = 0; node != null && depth < 16; depth++)
            {
                ControlType ct;
                string name;
                try { ct = node.Current.ControlType; name = (node.Current.Name ?? "").Trim(); }
                catch { break; }

                if (IsSurfaceType(ct) && IsMeaningfulName(name))
                    return Compose(name, ct);

                try { node = walker.GetParent(node); } catch { break; }
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static bool IsSurfaceType(ControlType t) =>
        t == ControlType.Pane || t == ControlType.Window || t == ControlType.ToolBar;

    private static bool IsMeaningfulName(string name)
    {
        if (name.Length < 2 || name.Length > 45) return false;
        if (name.Contains(" - ")) return false;   // "Document - App" main window title

        // Reject names that look like a file/document (e.g. "Drawing1.dwg").
        int dot = name.LastIndexOf('.');
        if (dot > 0 && name.Length - dot - 1 is >= 2 and <= 5)
        {
            bool looksExt = true;
            for (int i = dot + 1; i < name.Length; i++)
                if (!char.IsLetterOrDigit(name[i])) { looksExt = false; break; }
            if (looksExt) return false;
        }
        return true;
    }

    private static readonly string[] Nouns =
    {
        "window", "palette", "pane", "panel", "dialog", "manager", "editor",
        "toolbar", "bar", "browser", "inspector", "view", "vista", "vistas"
    };

    /// <summary>Append a plain noun for the surface type, unless the name is
    /// already descriptive.</summary>
    private static string Compose(string name, ControlType t)
    {
        string lower = name.ToLowerInvariant();
        foreach (var n in Nouns)
            if (lower == n || lower.EndsWith(" " + n)) return name;

        string noun =
            t == ControlType.Window ? "window" :
            t == ControlType.Pane ? "palette" :
            t == ControlType.ToolBar ? "toolbar" : "";
        return string.IsNullOrEmpty(noun) ? name : name + " " + noun;
    }

    private static T? RunGuarded<T>(Func<T?> work) where T : class
    {
        try
        {
            var task = Task.Run(work);
            return task.Wait(TimeoutMs) ? task.Result : null;
        }
        catch { return null; }
    }
}
