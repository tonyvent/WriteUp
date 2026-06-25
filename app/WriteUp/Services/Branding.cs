using System;
using System.IO;
using System.Windows;
using System.Windows.Resources;

namespace WriteUp.Services;

/// <summary>
/// Fixed branding compiled into the app: the Dynamic Engineering logo (embedded
/// as a WPF resource so the build is a single self-contained .exe) and the
/// company name. The logo is exposed both as raw bytes (for HTML/preview) and as
/// a temp-file path (MigraDoc's PDF/RTF export needs a file path).
/// </summary>
public static class Branding
{
    public const string Company = "Dynamic Engineering";

    private const string ResourceUri = "pack://application:,,,/Assets/dynamic-logo.png";
    private static string? _cachedPath;

    /// <summary>The embedded logo's bytes, or null if it can't be loaded.</summary>
    public static byte[]? LogoBytes()
    {
        try
        {
            StreamResourceInfo? info = Application.GetResourceStream(new Uri(ResourceUri, UriKind.Absolute));
            if (info?.Stream == null) return null;
            using var stream = info.Stream;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Path to the logo on disk. The embedded resource is extracted to a
    /// temp file (cached) so file-based consumers like MigraDoc can use it.
    /// Returns "" if the logo is unavailable.</summary>
    public static string LogoPath
    {
        get
        {
            try
            {
                if (_cachedPath != null && File.Exists(_cachedPath)) return _cachedPath;

                byte[]? bytes = LogoBytes();
                if (bytes is not { Length: > 0 }) return "";

                string path = Path.Combine(Path.GetTempPath(), "WriteUp-logo.png");
                File.WriteAllBytes(path, bytes);
                _cachedPath = path;
                return path;
            }
            catch { return ""; }
        }
    }
}
