using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace WriteUp.Services;

/// <summary>
/// Resolves the focused window into a generic, app-level label (e.g. "Google
/// Chrome", "SOLIDWORKS") rather than the document title, and exposes the window
/// handle / owning process so the recorder can ignore its own window and
/// focus-switch clicks.
/// </summary>
public static class WindowTracker
{
    /// <summary>Known executables mapped to clean product names so section
    /// headers never show a specific file/drawing name.</summary>
    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google Chrome", ["msedge"] = "Microsoft Edge", ["firefox"] = "Firefox",
        ["iexplore"] = "Internet Explorer", ["opera"] = "Opera", ["brave"] = "Brave",
        ["sldworks"] = "SOLIDWORKS", ["acad"] = "AutoCAD", ["accoreconsole"] = "AutoCAD",
        ["revit"] = "Revit", ["inventor"] = "Inventor", ["fusion360"] = "Fusion 360",
        ["fusion"] = "Fusion 360", ["onshape"] = "Onshape", ["catia"] = "CATIA",
        ["creo"] = "Creo", ["nx"] = "Siemens NX", ["solidedge"] = "Solid Edge",
        ["blender"] = "Blender", ["sketchup"] = "SketchUp",
        ["excel"] = "Microsoft Excel", ["winword"] = "Microsoft Word",
        ["powerpnt"] = "Microsoft PowerPoint", ["outlook"] = "Microsoft Outlook",
        ["onenote"] = "OneNote", ["msaccess"] = "Microsoft Access",
        ["code"] = "Visual Studio Code", ["devenv"] = "Visual Studio", ["notepad"] = "Notepad",
        ["notepad++"] = "Notepad++", ["sublime_text"] = "Sublime Text",
        ["explorer"] = "File Explorer", ["cmd"] = "Command Prompt", ["powershell"] = "PowerShell",
        ["windowsterminal"] = "Windows Terminal", ["wt"] = "Windows Terminal",
        ["teams"] = "Microsoft Teams", ["ms-teams"] = "Microsoft Teams", ["slack"] = "Slack",
        ["zoom"] = "Zoom", ["acrobat"] = "Adobe Acrobat", ["acrord32"] = "Adobe Acrobat Reader",
        ["photoshop"] = "Adobe Photoshop", ["illustrator"] = "Adobe Illustrator",
    };

    /// <summary>Foreground window handle, its title, and a generic app label.</summary>
    public static (IntPtr hwnd, string title, string app) GetActiveInfo()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        var (title, app) = DescribeWindow(hwnd);
        return (hwnd, title, app);
    }

    /// <summary>Title + generic app label for any window handle.</summary>
    public static (string title, string app) DescribeWindow(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return ("", "");
            int len = NativeMethods.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();
            return (title, FriendlyApp(hwnd, title));
        }
        catch
        {
            return ("", "");
        }
    }

    /// <summary>Convenience overload returning just the title and app label.</summary>
    public static (string title, string app) GetActive()
    {
        var (_, title, app) = GetActiveInfo();
        return (title, app);
    }

    /// <summary>Process id that owns a window, or 0.</summary>
    public static int ProcessIdOf(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return 0;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }
        catch { return 0; }
    }

    /// <summary>A generic, document-free app name: the known product name for the
    /// process, else the trailing "- App" portion of the title, else the process.</summary>
    private static string FriendlyApp(IntPtr hwnd, string title)
    {
        string proc = ProcessName(hwnd);
        if (proc.Length > 0 && Friendly.TryGetValue(proc, out var name))
            return name;

        // Window titles are usually "Document - App"; the last segment is the app
        // and avoids the specific document name.
        if (!string.IsNullOrWhiteSpace(title))
        {
            var parts = title.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return parts[^1].Trim();
        }

        return Pretty(proc);
    }

    /// <summary>Most apps title windows as "Document - App Name".</summary>
    public static string GuessAppFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var parts = title.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1].Trim() : title.Trim();
    }

    private static string ProcessName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static string Pretty(string proc)
    {
        if (string.IsNullOrWhiteSpace(proc)) return "";
        return char.ToUpperInvariant(proc[0]) + proc[1..];
    }
}
