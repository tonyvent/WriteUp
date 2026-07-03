namespace WriteUp.Models;

/// <summary>Persisted preferences and branding defaults.</summary>
public class AppSettings
{
    public string OutputDir { get; set; } = "";
    public string LastExportDir { get; set; } = "";
    public string DefaultAuthor { get; set; } = "";
    public string DefaultCompany { get; set; } = "Dynamic Engineering";
    public string DefaultDepartment { get; set; } = "";
    public string DefaultLogoPath { get; set; } = "";
    public bool AlwaysOnTop { get; set; } = true;
    public int MaxImageWidth { get; set; } = 1600;

    /// <summary>Delete the working session folder (screenshots) when the app
    /// closes so captures don't accumulate on disk. Off by default so sessions
    /// can be reopened later (📂 Open…); turn on in Settings to reclaim disk
    /// space at the cost of losing reopenable sessions.</summary>
    public bool CleanupSessionsOnExit { get; set; }

    /// <summary>Run the in-app guided tour the next time the app opens.
    /// True for new installs; cleared after the tour is finished or skipped.
    /// Can be re-enabled from Settings.</summary>
    public bool ShowGuidedTour { get; set; } = true;

    /// <summary>Hide the main window during recording and show a small movable
    /// Stop-recording bar at the top of the screen instead.</summary>
    public bool CompactWhileRecording { get; set; }
}
