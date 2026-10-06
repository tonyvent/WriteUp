using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Builds a WPF <see cref="FlowDocument"/> that mirrors the exported
/// report, for the live in-app preview pane.</summary>
public static class FlowReport
{
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x8A, 0x01, 0x01));
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

        // Branding is the logo only — the company/department text lived here but
        // duplicated the logo, so it's gone. Department still shows in the meta row.
        var logo = LoadImage(Branding.LogoPath, 260);
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

        var numbers = StepText.Numbers(steps);
        int n = 0;
        string? current = null;
        foreach (var s in steps)
        {
            if (!string.IsNullOrWhiteSpace(s.Context) && s.Context != current)
            {
                current = s.Context;
                doc.Blocks.Add(new Paragraph(new Run("IN " + s.Context.ToUpperInvariant()))
                { Foreground = Accent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 14, 0, 2) });
            }

            n++;
            var sp = new Paragraph { Margin = new Thickness(s.Level * 20, 6, 0, 0), Tag = s, Name = "step_" + s.Id };  // Tag links back to the step
            sp.Inlines.Add(new Run(numbers[s.Id] + ".  ") { FontWeight = FontWeights.Bold });
            AddText(sp, StepText.ResolveReferences(s.Caption, numbers));
            if (!string.IsNullOrWhiteSpace(s.Notes)) { sp.Inlines.Add(new LineBreak()); AddText(sp, StepText.ResolveReferences(s.Notes, numbers)); }
            string reference = StepText.Reference(s, numbers);
            if (reference.Length > 0) { sp.Inlines.Add(new LineBreak()); AddText(sp, reference); }
            doc.Blocks.Add(sp);

            var bmp = LoadImage(s.ImagePath, 720);
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

    private static void AddText(Paragraph p, string text)
    {
        foreach (var span in StepText.Parse(text))
        {
            var run = new Run(span.Text) { FontWeight = span.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = span.Italic ? FontStyles.Italic : FontStyles.Normal };
            if (span.Link == null) { p.Inlines.Add(run); continue; }
            var link = new Hyperlink(run);
            link.Click += (_, _) =>
            {
                if (span.Link.StartsWith("#step-"))
                {
                    if (p.Parent is FlowDocument document)
                        foreach (var block in document.Blocks)
                            if (block is Paragraph target && target.Name == span.Link[1..].Replace('-', '_')) target.BringIntoView();
                }
                else try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(span.Link) { UseShellExecute = true }); } catch { }
            };
            p.Inlines.Add(link);
        }
    }

    private static Block Rule()
    {
        var border = new Border { Height = 2, Background = Accent, Margin = new Thickness(0, 6, 0, 0) };
        return new BlockUIContainer(border) { Margin = new Thickness(0) };
    }

    private static BitmapImage? LoadImage(string? path, int decodeWidth) =>
        ImageLoad.FromFile(path, decodeWidth);
}
