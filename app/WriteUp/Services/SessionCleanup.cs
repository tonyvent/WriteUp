using System;
using System.IO;
using System.Text.RegularExpressions;

namespace WriteUp.Services;

/// <summary>
/// Keeps the working sessions folder from growing without bound. Session folders
/// are transient capture scratch space — finished write-ups are exported
/// elsewhere and embed their own images — so we delete the current session on
/// exit and sweep up any orphans a previous crash left behind.
/// </summary>
public static class SessionCleanup
{
    // Session folders are named yyyyMMdd-HHmmss (see MainWindow.StartRecording).
    private static readonly Regex SessionName = new(@"^\d{8}-\d{6}$", RegexOptions.Compiled);

    public static void Delete(string? sessionDir)
    {
        if (string.IsNullOrWhiteSpace(sessionDir)) return;
        try
        {
            if (Directory.Exists(sessionDir))
                Directory.Delete(sessionDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Remove leftover session folders directly under <paramref name="root"/>.
    /// Only deletes immediate subfolders whose name matches the session pattern,
    /// so a user-chosen output folder that also holds other files is never harmed.
    /// </summary>
    public static void PurgeOrphans(string? root, string? keep = null)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            if (!Directory.Exists(root)) return;
            string? keepFull = string.IsNullOrWhiteSpace(keep) ? null : Path.GetFullPath(keep);
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (!SessionName.IsMatch(Path.GetFileName(dir))) continue;
                if (keepFull != null &&
                    string.Equals(Path.GetFullPath(dir), keepFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(dir, recursive: true); } catch { /* skip locked */ }
            }
        }
        catch { /* best-effort */ }
    }
}
