using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace WriteUp;

/// <summary>
/// Binds a file path to a thumbnail-sized <see cref="BitmapImage"/>, bypassing
/// WPF's URI cache. Needed because the annotation editor rewrites screenshot
/// files in place: a plain string binding would keep showing the stale cached
/// bitmap. Loads with OnLoad so files are never kept locked, and freezes for
/// cheap reuse.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.DecodePixelWidth = 320; // thumbnail size; keeps the list light
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
