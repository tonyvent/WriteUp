using System.IO;
using System.Text.Json;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Loads/saves <see cref="AppSettings"/> under %AppData%\WriteUp.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string AppDataDir
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WriteUp");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static string DefaultSessionsDir
    {
        get
        {
            string dir = Path.Combine(AppDataDir, "sessions");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (s != null)
                {
                    if (string.IsNullOrWhiteSpace(s.OutputDir)) s.OutputDir = DefaultSessionsDir;
                    return s;
                }
            }
        }
        catch { /* fall through to defaults */ }

        return new AppSettings { OutputDir = DefaultSessionsDir };
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch { /* non-fatal */ }
    }
}
