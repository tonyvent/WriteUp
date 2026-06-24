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
    /// closes so captures don't accumulate on disk. Finished reports are
    /// exported separately and embed their own images.</summary>
    public bool CleanupSessionsOnExit { get; set; } = true;
}
