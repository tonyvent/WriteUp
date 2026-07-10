using System;
using System.Globalization;
using System.Windows.Data;
using WriteUp.Services;

namespace WriteUp;

/// <summary>
/// Binds a file path to a thumbnail-sized bitmap via <see cref="ImageLoad"/>, so
/// the step list stays light and reloads screenshots the annotation editor
/// rewrites in place instead of showing a stale cached bitmap.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ImageLoad.FromFile(value as string, 320); // thumbnail width

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
