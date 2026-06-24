using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProcessScribe.Models;

namespace ProcessScribe.Services;

/// <summary>Builds a WPF <see cref="FlowDocument"/> that mirrors the exported
/// report, for the live in-app preview pane.</summary>
public static class FlowReport
{
    private static readonly Brush Accent = Frozen(Color.FromRgb(0xFF, 0x4C, 0x1F));
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x6B, 0x6B, 0x6B));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static FlowDocument Build(SessionMeta m, IReadOnlyList<Step> steps)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(30, 26, 30, 26),
            Background = Brushes.White,
            ColumnWidth = double.PositiveInfinity
        };

        if (!string.IsNullOrWhiteSpace(m.Company))
        {
            doc.Blocks.Add(new Paragraph(new Run(m.Company))
            { FontWeight = FontWeights.Bold, FontSize = 18, Margin = new Thickness(0) });
            if (!string.IsNullOrWhiteSpace(m.Department))
                doc.Blocks.Add(new Paragraph(new Run(m.Department))
                { Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
        }

        var logo = LoadImage(m.LogoPath, 260);
        if (logo != null)
        {
            var img = new Image { Source = logo, Stretch = Stretch.Uniform, Height = 42,
                HorizontalAlignment = HorizontalAlignment.Left };
            doc.Blocks.Add(new BlockUIContainer(img) { Margin = new Thickness(0, 0, 0, 6) });
        }

        doc.Blocks.Add(Rule());

        doc.Blocks.Add(new Paragraph(new Run(ReportWriter.TitleOf(m)))
        { FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) });

        if (!string.IsNullOrWhiteSpace(m.Subtitle))
            doc.Blocks.Add(new Paragraph(new Run(m.Subtitle))
            { Foreground = Muted, FontStyle = FontStyles.Italic, FontSize = 15, Margin = new Thickness(0, 0, 0, 6) });

        var bits = ReportWriter.MetaPairs(m);
        if (bits.Count > 0)
        {
            var mp = new Paragraph { FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
            for (int i = 0; i < bits.Count; i++)
            {
                mp.Inlines.Add(new Run(bits[i].Item1 + ": ") { FontWeight = FontWeights.SemiBold, Foreground = Muted });
                mp.Inlines.Add(new Run(bits[i].Item2));
                if (i < bits.Count - 1) mp.Inlines.Add(new Run("      "));
            }
            doc.Blocks.Add(mp);
        }

        int n = 0;
        string? current = null;
        foreach (var s in steps)
        {
            if (!string.IsNullOrWhiteSpace(s.App) && s.App != current)
            {
                current = s.App;
                doc.Blocks.Add(new Paragraph(new Run("IN " + s.App.ToUpperInvariant()))
                { Foreground = Accent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 14, 0, 2) });
            }

            n++;
            var sp = new Paragraph { Margin = new Thickness(0, 6, 0, 0) };
            sp.Inlines.Add(new Run(n + ".  ") { FontWeight = FontWeights.Bold });
            sp.Inlines.Add(new Run(s.Caption));
            doc.Blocks.Add(sp);

            var bmp = LoadImage(s.ScreenshotPath, 720);
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Stretch = Stretch.Uniform, MaxWidth = 660,
                    HorizontalAlignment = HorizontalAlignment.Left };
                doc.Blocks.Add(new BlockUIContainer(img) { Margin = new Thickness(0, 8, 0, 4) });
            }
        }

        if (steps.Count == 0)
            doc.Blocks.Add(new Paragraph(new Run("Nothing recorded yet — your steps will preview here as you go."))
            { Foreground = Muted, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 8, 0, 0) });

        return doc;
    }

    private static Block Rule()
    {
        var border = new Border { Height = 2, Background = Accent, Margin = new Thickness(0, 6, 0, 0) };
        return new BlockUIContainer(border) { Margin = new Thickness(0) };
    }

    private static BitmapImage? LoadImage(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;          // don't keep the file locked
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.DecodePixelWidth = decodeWidth;                  // downscale for snappy preview
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
