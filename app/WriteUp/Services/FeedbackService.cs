using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>
/// Collects user feedback / bug reports.
///
/// Today each report is dropped as a JSON file under
/// <c>%AppData%\WriteUp\feedback\</c> so they can be gathered off a machine. The
/// single <see cref="Submit"/> entry point is intentionally the only coupling
/// the UI has, so a future sink (email, an HTTP endpoint, a GitHub issue) can be
/// added here without changing the Settings window.
/// </summary>
public static class FeedbackService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string FeedbackDir
    {
        get
        {
            string dir = Path.Combine(SettingsStore.AppDataDir, "feedback");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Persist a report (stamping app/OS/time context) and return the
    /// saved file path.</summary>
    public static string Submit(FeedbackReport report)
    {
        report.AppVersion = AppVersion();
        report.Os = Environment.OSVersion.VersionString;
        if (report.CreatedUtc == default) report.CreatedUtc = DateTime.UtcNow;

        string name = $"feedback-{report.CreatedUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json";
        string path = Path.Combine(FeedbackDir, name);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOpts));
        return path;
    }

    /// <summary>Opens the user's mail client with the report pre-filled to the
    /// maintainer. Returns true if a mail client was launched.</summary>
    public static bool EmailTo(FeedbackReport report)
    {
        const string to = "anthonyventurini827@gmail.com";   // where reports go

        report.AppVersion = AppVersion();
        report.Os = Environment.OSVersion.VersionString;

        string subject = $"WriteUp {report.Category}: " +
            (report.Message.Length > 60 ? report.Message[..60] + "…" : report.Message);

        string body =
            report.Message + "\r\n\r\n" +
            "----\r\n" +
            $"Type: {report.Category}\r\n" +
            $"From: {report.Contact}\r\n" +
            $"App version: {report.AppVersion}\r\n" +
            $"OS: {report.Os}\r\n";

        string uri = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}" +
                     $"&body={Uri.EscapeDataString(body)}";
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private static string AppVersion()
    {
        try { return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?"; }
        catch { return "?"; }
    }
}
