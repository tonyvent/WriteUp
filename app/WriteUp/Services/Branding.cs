using System;
using System.IO;

namespace WriteUp.Services;

/// <summary>
/// Fixed branding bundled with the app: the Dynamic Engineering logo (embedded
/// in every report and the live preview) and the company name. The logo file
/// ships next to the executable via the project's Content item.
/// </summary>
public static class Branding
{
    public const string Company = "Dynamic Engineering";

    /// <summary>Absolute path to the bundled logo on disk, or "" if missing.</summary>
    public static string LogoPath
    {
        get
        {
            try
            {
                string p = Path.Combine(AppContext.BaseDirectory, "Assets", "dynamic-logo.png");
                return File.Exists(p) ? p : "";
            }
            catch { return ""; }
        }
    }
}
