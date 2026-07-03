using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>
/// Handles everything around annotating a screenshot file in place:
///
///  - <c>shot.png</c>            the flattened image every exporter/preview uses
///  - <c>shot.orig.png</c>       pristine backup, created on first edit; vector
///                               annotations are re-rendered from it so they stay
///                               editable across sessions
///  - <c>shot.ann.json</c>       sidecar with the editable (vector) annotations
///
/// Blur/Redact are privacy features, so on save they are burned into the
/// original backup as well and dropped from the sidecar — after saving, no
/// un-blurred/un-redacted copy of that region exists on disk.
/// </summary>
public static class AnnotationStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string OriginalPathFor(string shotPath) =>
        Path.ChangeExtension(shotPath, null) + ".orig.png";

    public static string SidecarPathFor(string shotPath) =>
        Path.ChangeExtension(shotPath, null) + ".ann.json";

    /// <summary>The image the editor should draw on top of: the pristine
    /// original if one exists, otherwise the shot itself (first edit).</summary>
    public static string BasePathFor(string shotPath)
    {
        string orig = OriginalPathFor(shotPath);
        return File.Exists(orig) ? orig : shotPath;
    }

    public static List<Annotation> Load(string shotPath)
    {
        try
        {
            string sidecar = SidecarPathFor(shotPath);
            if (File.Exists(sidecar))
                return JsonSerializer.Deserialize<List<Annotation>>(File.ReadAllText(sidecar)) ?? new();
        }
        catch { /* corrupt sidecar -> start clean */ }
        return new();
    }

    /// <summary>
    /// Applies <paramref name="annotations"/> to the screenshot and persists
    /// everything. Destructive marks (blur/redact) are burned into the original
    /// backup; the rest are kept editable in the sidecar.
    /// </summary>
    public static void Save(string shotPath, List<Annotation> annotations)
    {
        string origPath = OriginalPathFor(shotPath);

        // First edit: back up the pristine capture before we overwrite it.
        if (!File.Exists(origPath))
            File.Copy(shotPath, origPath);

        var destructive = annotations.Where(a => a.IsDestructive).ToList();
        var vector = annotations.Where(a => !a.IsDestructive).ToList();

        // 1) Burn blur/redact into the ORIGINAL so sensitive pixels are gone
        //    for good (an "editable" redaction would defeat the point).
        if (destructive.Count > 0)
        {
            using (var bmp = LoadBitmapUnlocked(origPath))
            {
                using var g = Graphics.FromImage(bmp);
                foreach (var a in destructive) RenderDestructive(g, bmp, a);
                SaveAtomic(bmp, origPath);
            }
        }

        // 2) Render the editable marks from the (now possibly redacted)
        //    original into the flattened shot that reports embed.
        using (var bmp = LoadBitmapUnlocked(origPath))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                foreach (var a in vector) RenderVector(g, a);
            }
            SaveAtomic(bmp, shotPath);
        }

        // 3) Persist only the still-editable annotations.
        string sidecar = SidecarPathFor(shotPath);
        if (vector.Count > 0)
            File.WriteAllText(sidecar, JsonSerializer.Serialize(vector, JsonOpts));
        else if (File.Exists(sidecar))
            File.Delete(sidecar);
    }

    /// <summary>Discards all editable annotations: restores the shot from the
    /// original backup (burned-in blur/redact cannot be undone).</summary>
    public static void ResetToOriginal(string shotPath)
    {
        string orig = OriginalPathFor(shotPath);
        if (File.Exists(orig)) File.Copy(orig, shotPath, overwrite: true);
        string sidecar = SidecarPathFor(shotPath);
        if (File.Exists(sidecar)) File.Delete(sidecar);
    }

    // ---- rendering -----------------------------------------------------------

    private static void RenderDestructive(Graphics g, Bitmap bmp, Annotation a)
    {
        var r = ClampRect(a, bmp.Width, bmp.Height);
        if (r.Width < 1 || r.Height < 1) return;

        if (a.Kind == AnnotationKind.Redact)
        {
            using var black = new SolidBrush(Color.Black);
            g.FillRectangle(black, r);
            return;
        }

        // Pixelate: downscale the region hard, then upscale with nearest
        // neighbour. Block size scales with the region so small areas still
        // become unreadable.
        int block = Math.Max(12, Math.Max(r.Width, r.Height) / 24);
        int dw = Math.Max(1, r.Width / block);
        int dh = Math.Max(1, r.Height / block);

        using var region = bmp.Clone(r, bmp.PixelFormat);
        using var small = new Bitmap(dw, dh);
        using (var gs = Graphics.FromImage(small))
        {
            gs.InterpolationMode = InterpolationMode.HighQualityBilinear;
            gs.DrawImage(region, new Rectangle(0, 0, dw, dh),
                new Rectangle(0, 0, region.Width, region.Height), GraphicsUnit.Pixel);
        }

        var prevInterp = g.InterpolationMode;
        var prevPixOff = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, r, new Rectangle(0, 0, dw, dh), GraphicsUnit.Pixel);
        g.InterpolationMode = prevInterp;
        g.PixelOffsetMode = prevPixOff;
    }

    private static void RenderVector(Graphics g, Annotation a)
    {
        var color = ParseColor(a.ColorHex);
        float w = (float)Math.Max(1, a.StrokeWidth);

        switch (a.Kind)
        {
            case AnnotationKind.Box:
            {
                using var pen = new Pen(color, w) { LineJoin = LineJoin.Round };
                g.DrawRectangle(pen, (float)a.Left, (float)a.Top, (float)a.Width, (float)a.Height);
                break;
            }
            case AnnotationKind.Arrow:
            {
                DrawArrow(g, color, w, (float)a.X1, (float)a.Y1, (float)a.X2, (float)a.Y2);
                break;
            }
            case AnnotationKind.Callout:
            {
                DrawCallout(g, color, w, a);
                break;
            }
        }
    }

    private static void DrawArrow(Graphics g, Color color, float w, float x1, float y1, float x2, float y2)
    {
        float head = Math.Max(12f, w * 3.5f);
        double ang = Math.Atan2(y2 - y1, x2 - x1);

        // Shorten the shaft slightly so it doesn't poke through the head.
        float sx2 = x2 - (float)(Math.Cos(ang) * head * 0.6);
        float sy2 = y2 - (float)(Math.Sin(ang) * head * 0.6);

        using var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, x1, y1, sx2, sy2);

        var p1 = new PointF(x2, y2);
        var p2 = new PointF(
            x2 - (float)(Math.Cos(ang - 0.42) * head),
            y2 - (float)(Math.Sin(ang - 0.42) * head));
        var p3 = new PointF(
            x2 - (float)(Math.Cos(ang + 0.42) * head),
            y2 - (float)(Math.Sin(ang + 0.42) * head));
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[] { p1, p2, p3 });
    }

    private static void DrawCallout(Graphics g, Color color, float w, Annotation a)
    {
        string text = string.IsNullOrWhiteSpace(a.Text) ? "…" : a.Text;
        float fontSize = Math.Max(14f, w * 4f); // keep in sync with the editor preview
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);

        SizeF size = g.MeasureString(text, font, 420);
        float padX = 10, padY = 6;
        var box = new RectangleF((float)a.X1, (float)a.Y1, size.Width + padX * 2, size.Height + padY * 2);

        // Leader line from the nearest edge of the label to the target point.
        float lx = Clamp((float)a.X2, box.Left, box.Right);
        float ly = Clamp((float)a.Y2, box.Top, box.Bottom);
        using (var pen = new Pen(color, Math.Max(2f, w * 0.75f)) { EndCap = LineCap.Round })
            g.DrawLine(pen, lx, ly, (float)a.X2, (float)a.Y2);
        using (var dot = new SolidBrush(color))
            g.FillEllipse(dot, (float)a.X2 - 5, (float)a.Y2 - 5, 10, 10);

        using var path = RoundedRect(box, 8);
        using (var fill = new SolidBrush(Color.FromArgb(240, Color.White)))
            g.FillPath(fill, path);
        using (var border = new Pen(color, 2.5f))
            g.DrawPath(border, path);
        using (var ink = new SolidBrush(color))
            g.DrawString(text, font, ink,
                new RectangleF(box.X + padX, box.Y + padY, size.Width + 2, size.Height + 2));
    }

    // ---- helpers -------------------------------------------------------------

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Rectangle ClampRect(Annotation a, int maxW, int maxH)
    {
        int x = (int)Math.Round(Math.Max(0, a.Left));
        int y = (int)Math.Round(Math.Max(0, a.Top));
        int r = (int)Math.Round(Math.Min(maxW, a.Left + a.Width));
        int b = (int)Math.Round(Math.Min(maxH, a.Top + a.Height));
        return new Rectangle(x, y, Math.Max(0, r - x), Math.Max(0, b - y));
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(hex);
            return Color.FromArgb(255, c);
        }
        catch { return Color.FromArgb(138, 1, 1); }
    }

    private static float Clamp(float v, float lo, float hi) => Math.Min(Math.Max(v, lo), hi);

    /// <summary>Loads a bitmap without keeping the file handle open, so we can
    /// overwrite the same path afterwards.</summary>
    private static Bitmap LoadBitmapUnlocked(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var img = Image.FromStream(stream);
        return new Bitmap(img); // 32bpp copy detached from the stream
    }

    /// <summary>Writes to a temp file first so a crash mid-save never leaves a
    /// half-written PNG behind.</summary>
    private static void SaveAtomic(Bitmap bmp, string path)
    {
        string tmp = path + ".tmp";
        bmp.Save(tmp, ImageFormat.Png);
        File.Move(tmp, path, overwrite: true);
    }
}
