using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using FormsScreen = System.Windows.Forms.Screen;

namespace WriteUp.Services;

/// <summary>
/// Captures only the monitor the user is working on (not the whole desktop),
/// marks the cursor with a pointer, outlines the control that was clicked, and
/// adds a magnified inset so small buttons stay legible in the write-up.
/// </summary>
public static class ScreenCapturer
{
    private static readonly Color Accent = Color.FromArgb(138, 1, 1);

    /// <summary>Capture the monitor under (globalX, globalY) for a click. Saves
    /// two PNGs — one without the zoom inset and one with it — so the step can
    /// toggle the zoom window on or off. Returns (baseShot, zoomShot) paths.</summary>
    public static (string baseShot, string zoomShot) CaptureClick(string dir, int globalX, int globalY,
        Rectangle elementBounds, int maxWidth)
    {
        Directory.CreateDirectory(dir);

        var spot = new Point(globalX, globalY);
        Rectangle mon = MonitorBoundsFor(spot);
        int lx = spot.X - mon.Left;
        int ly = spot.Y - mon.Top;

        using var full = new Bitmap(mon.Width, mon.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
            g.CopyFromScreen(mon.Left, mon.Top, 0, 0, mon.Size, CopyPixelOperation.SourceCopy);

        // Outline the clicked control on the shared base, so it shows in both.
        if (!elementBounds.IsEmpty)
        {
            var localEl = new Rectangle(elementBounds.X - mon.Left, elementBounds.Y - mon.Top,
                                        elementBounds.Width, elementBounds.Height);
            using var g = Graphics.FromImage(full);
            DrawElementBox(g, localEl);
        }

        // Zoomed variant: copy the base, add the inset, then the pointer.
        using var zoomed = (Bitmap)full.Clone();
        using (var g = Graphics.FromImage(zoomed))
        {
            DrawZoomInset(g, zoomed, lx, ly, mon.Size);
            DrawPointer(g, lx, ly);
        }

        // Base variant: pointer only (no zoom inset).
        using (var g = Graphics.FromImage(full))
            DrawPointer(g, lx, ly);

        string stamp = $"{DateTime.Now:HHmmss_fff}";
        string basePath = Path.Combine(dir, stamp + ".png");
        string zoomPath = Path.Combine(dir, stamp + "_zoom.png");
        SaveScaled(full, basePath, maxWidth);
        SaveScaled(zoomed, zoomPath, maxWidth);
        return (basePath, zoomPath);
    }

    /// <summary>Capture the monitor the cursor is currently on, for notes and
    /// typing steps. Marks the pointer but adds no zoom inset.</summary>
    public static string CaptureContext(string dir, int maxWidth)
    {
        Directory.CreateDirectory(dir);

        Point spot = CursorPos();
        Rectangle mon = MonitorBoundsFor(spot);
        using var full = new Bitmap(mon.Width, mon.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(mon.Left, mon.Top, 0, 0, mon.Size, CopyPixelOperation.SourceCopy);
            DrawPointer(g, spot.X - mon.Left, spot.Y - mon.Top);
        }

        string path = Path.Combine(dir, $"{DateTime.Now:HHmmss_fff}.png");
        SaveScaled(full, path, maxWidth);
        return path;
    }

    /// <summary>Downscale to maxWidth (if wider) and save as PNG.</summary>
    private static void SaveScaled(Bitmap bmp, string path, int maxWidth)
    {
        Bitmap toSave = bmp;
        Bitmap? scaled = null;
        if (maxWidth > 0 && bmp.Width > maxWidth)
        {
            int h = (int)Math.Round(bmp.Height * (maxWidth / (double)bmp.Width));
            scaled = new Bitmap(maxWidth, h);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, maxWidth, h);
            }
            toSave = scaled;
        }
        toSave.Save(path, ImageFormat.Png);
        scaled?.Dispose();
    }

    private static Rectangle MonitorBoundsFor(Point p)
    {
        try
        {
            var b = FormsScreen.FromPoint(p).Bounds;
            if (b.Width > 0 && b.Height > 0) return b;
        }
        catch { /* fall through */ }
        try
        {
            var b = FormsScreen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
            if (b.Width > 0 && b.Height > 0) return b;
        }
        catch { /* fall through */ }
        return new Rectangle(0, 0, 1920, 1080);
    }

    private static Point CursorPos()
    {
        try { if (NativeMethods.GetCursorPos(out var pt)) return new Point(pt.x, pt.y); }
        catch { /* ignore */ }
        return new Point(0, 0);
    }

    // ---- annotation ---------------------------------------------------------

    /// <summary>A magnified, pixel-crisp view of the area around the click so
    /// small/dense controls remain readable after downscaling.</summary>
    private static void DrawZoomInset(Graphics g, Bitmap full, int x, int y, Size mon)
    {
        const int srcW = 220, srcH = 150, scale = 2;
        int sw = Math.Min(srcW, full.Width);
        int sh = Math.Min(srcH, full.Height);
        int sx = Math.Clamp(x - sw / 2, 0, Math.Max(0, full.Width - sw));
        int sy = Math.Clamp(y - sh / 2, 0, Math.Max(0, full.Height - sh));
        var src = new Rectangle(sx, sy, sw, sh);

        int dw = sw * scale, dh = sh * scale;
        const int margin = 24;
        // Place the inset on the side away from the cursor so it never covers it.
        int dx = x < mon.Width / 2 ? mon.Width - dw - margin : margin;
        int dy = margin;
        var dest = new Rectangle(dx, dy, dw, dh);

        g.InterpolationMode = InterpolationMode.NearestNeighbor; // crisp pixels for tiny UI
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(full, dest, src, GraphicsUnit.Pixel);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var border = new Pen(Accent, 3))
            g.DrawRectangle(border, dest);

        // The cursor target, mapped into the magnified inset.
        DrawTarget(g, dx + (x - sx) * scale, dy + (y - sy) * scale);

        using var labelBg = new SolidBrush(Color.FromArgb(225, Accent));
        using var labelFont = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        const string label = "ZOOM 2×";
        var sz = g.MeasureString(label, labelFont);
        g.FillRectangle(labelBg, dx, dy - (int)sz.Height, sz.Width + 8, sz.Height);
        g.DrawString(label, labelFont, Brushes.White, dx + 4, dy - sz.Height + 1);
    }

    private static void DrawElementBox(Graphics g, Rectangle r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        // Pad slightly so the outline sits just outside the control.
        r.Inflate(3, 3);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(235, Accent), 2.5f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r);
    }

    /// <summary>Ring + crosshair, used inside the zoom inset.</summary>
    private static void DrawTarget(Graphics g, int x, int y)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var ring = new Pen(Accent, 3);
        g.DrawEllipse(ring, x - 16, y - 16, 32, 32);
        using var cross = new Pen(Accent, 2);
        g.DrawLine(cross, x - 22, y, x - 8, y);
        g.DrawLine(cross, x + 8, y, x + 22, y);
        g.DrawLine(cross, x, y - 22, x, y - 8);
        g.DrawLine(cross, x, y + 8, x, y + 22);
    }

    /// <summary>A highlight ring plus a classic arrow pointer with its tip at (x, y),
    /// so the write-up clearly shows the mouse sitting on the control.</summary>
    private static void DrawPointer(Graphics g, int x, int y)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var glow = new SolidBrush(Color.FromArgb(55, Accent)))
            g.FillEllipse(glow, x - 24, y - 24, 48, 48);
        using (var ring = new Pen(Accent, 3))
            g.DrawEllipse(ring, x - 17, y - 17, 34, 34);

        PointF[] arrow =
        {
            new(x, y),
            new(x, y + 17),
            new(x + 4, y + 13),
            new(x + 7, y + 19),
            new(x + 10, y + 18),
            new(x + 7, y + 12),
            new(x + 12, y + 12),
        };
        using var fill = new SolidBrush(Color.White);
        using var outline = new Pen(Color.FromArgb(30, 30, 30), 1.5f);
        g.FillPolygon(fill, arrow);
        g.DrawPolygon(outline, arrow);
    }
}
