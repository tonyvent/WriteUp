using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using WinFormsScreen = System.Windows.Forms.SystemInformation;

namespace ProcessScribe.Services;

/// <summary>Captures the whole virtual desktop and marks the click location.</summary>
public static class ScreenCapturer
{
    private static readonly Color Accent = Color.FromArgb(255, 76, 31);

    /// <summary>
    /// Grab the screen, optionally draw a target ring at the global (x, y) click
    /// point, downscale, and save a PNG. Returns the saved file path.
    /// </summary>
    public static string Capture(string dir, int globalX, int globalY, bool mark, int maxWidth)
    {
        Directory.CreateDirectory(dir);
        Rectangle vs = WinFormsScreen.VirtualScreen;
        if (vs.Width <= 0 || vs.Height <= 0)
            vs = new Rectangle(0, 0, 1920, 1080);

        using var full = new Bitmap(vs.Width, vs.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
            if (mark)
                DrawMarker(g, globalX - vs.Left, globalY - vs.Top);
        }

        Bitmap toSave = full;
        Bitmap? scaled = null;
        if (maxWidth > 0 && full.Width > maxWidth)
        {
            int h = (int)Math.Round(full.Height * (maxWidth / (double)full.Width));
            scaled = new Bitmap(maxWidth, h);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(full, 0, 0, maxWidth, h);
            }
            toSave = scaled;
        }

        string path = Path.Combine(dir, $"{DateTime.Now:HHmmss_fff}.png");
        toSave.Save(path, ImageFormat.Png);
        scaled?.Dispose();
        return path;
    }

    private static void DrawMarker(Graphics g, int x, int y)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(60, Accent));
        g.FillEllipse(glow, x - 26, y - 26, 52, 52);

        using var ringOuter = new Pen(Accent, 4);
        using var ringInner = new Pen(Accent, 3);
        g.DrawEllipse(ringOuter, x - 22, y - 22, 44, 44);
        g.DrawEllipse(ringInner, x - 12, y - 12, 24, 24);

        using var cross = new Pen(Accent, 2);
        g.DrawLine(cross, x - 30, y, x - 14, y);
        g.DrawLine(cross, x + 14, y, x + 30, y);
        g.DrawLine(cross, x, y - 30, x, y - 14);
        g.DrawLine(cross, x, y + 14, x, y + 30);
    }
}
