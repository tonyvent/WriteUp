using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace WriteUp.Services;

/// <summary>
/// Loads image files into frozen <see cref="BitmapImage"/>s while bypassing WPF's
/// URI cache. The annotation editor rewrites screenshot files in place, so a
/// cached load would keep showing the stale bitmap; <c>OnLoad</c> also avoids
/// keeping the file locked, and freezing makes the bitmap cheap to reuse/marshal.
/// </summary>
public static class ImageLoad
{
    /// <summary>Load <paramref name="path"/>, optionally decoding down to
    /// <paramref name="decodePixelWidth"/> px wide (0 = full resolution). Returns
    /// null if the path is empty, missing, or can't be decoded.</summary>
    public static BitmapImage? FromFile(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (decodePixelWidth > 0) bi.DecodePixelWidth = decodePixelWidth;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}
