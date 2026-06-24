namespace ProcessScribe.Models;

/// <summary>Persisted preferences and branding defaults.</summary>
public class AppSettings
{
    public string OutputDir { get; set; } = "";
    public string LastExportDir { get; set; } = "";
    public string DefaultAuthor { get; set; } = "";
    public string DefaultCompany { get; set; } = "";
    public string DefaultDepartment { get; set; } = "";
    public string DefaultLogoPath { get; set; } = "";
    public bool AlwaysOnTop { get; set; } = true;
    public int MaxImageWidth { get; set; } = 1600;
}
