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
    }

    private const int TimeoutMs = 700;

    /// <summary>Describe the control at screen point (x, y).</summary>
    public static ElementInfo? Describe(int x, int y) => RunGuarded(() =>
    {
        var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
        return el == null ? null : Read(el);
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
