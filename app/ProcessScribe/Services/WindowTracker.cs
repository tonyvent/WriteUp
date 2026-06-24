using System.Diagnostics;
using System.Text;

namespace ProcessScribe.Services;

/// <summary>Resolves the currently focused window's title and a friendly app name.</summary>
public static class WindowTracker
{
    public static (string title, string app) GetActive()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("", "");

            int len = NativeMethods.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();

            string app = GuessAppFromTitle(title);
            if (string.IsNullOrWhiteSpace(app))
                app = ProcessName(hwnd);

            return (title, app);
        }
        catch
        {
            return ("", "");
        }
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
}
