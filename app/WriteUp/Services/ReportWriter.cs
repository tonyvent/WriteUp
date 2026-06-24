using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Renders a session into Markdown and a standalone HTML report.</summary>
public static class ReportWriter
{
    public static string TitleOf(SessionMeta m) =>
        string.IsNullOrWhiteSpace(m.Title) ? "Process Write-up" : m.Title.Trim();

    private static string DateOf(SessionMeta m) =>
        string.IsNullOrWhiteSpace(m.Date) ? DateTime.Now.ToString("yyyy-MM-dd") : m.Date.Trim();

    private static List<(string label, string value)> MetaBits(SessionMeta m)
    {
        var bits = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(m.Author)) bits.Add(("Author", m.Author.Trim()));
        bits.Add(("Date", DateOf(m)));
        if (!string.IsNullOrWhiteSpace(m.Revision)) bits.Add(("Revision", m.Revision.Trim()));
        if (!string.IsNullOrWhiteSpace(m.Department)) bits.Add(("Department", m.Department.Trim()));
        return bits;
    }

    /// <summary>Public view of the title-block meta pairs, shared by the PDF/RTF
    /// exporter and the live preview.</summary>
    public static IReadOnlyList<(string Label, string Value)> MetaPairs(SessionMeta m) => MetaBits(m);

    // ---- Markdown -----------------------------------------------------------
    public static string Markdown(SessionMeta m, IReadOnlyList<Step> steps)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(m.Company))
        {
            sb.Append("**").Append(m.Company.Trim()).Append("**");
            if (!string.IsNullOrWhiteSpace(m.Department))
                sb.Append(" — ").Append(m.Department.Trim());
            sb.AppendLine().AppendLine();
        }
        sb.Append("# ").AppendLine(TitleOf(m)).AppendLine();
        if (!string.IsNullOrWhiteSpace(m.Subtitle))
            sb.Append('_').Append(m.Subtitle.Trim()).Append('_').AppendLine().AppendLine();

        sb.AppendLine(string.Join(" | ", MetaBits(m).Select(b => $"**{b.label}:** {b.value}")));
        sb.AppendLine().AppendLine("---").AppendLine();

        int n = 0;
        string? currentApp = null;
        foreach (var s in steps)
        {
            if (!string.IsNullOrWhiteSpace(s.Context) && s.Context != currentApp)
            {
                currentApp = s.Context;
                sb.AppendLine().Append("## In ").AppendLine(s.Context).AppendLine();
            }
            n++;
            sb.Append("**").Append(n).Append(".** ").AppendLine(s.Caption);
            if (s.HasScreenshot)
                sb.AppendLine().Append("![Step ").Append(n).Append("](")
                  .Append(RelativeShot(s)).AppendLine(")");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static string RelativeShot(Step s)
    {
        // Reports are written into the session folder, screenshots live in ./screenshots
        var name = Path.GetFileName(s.ScreenshotPath!);
        return "screenshots/" + name;
    }

    // ---- HTML (single self-contained file) ----------------------------------
    public static string Html(SessionMeta m, IReadOnlyList<Step> steps)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang='en'><head><meta charset='utf-8'>");
        sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        sb.Append("<title>").Append(Enc(TitleOf(m))).Append("</title><style>")
          .Append(Css).Append("</style></head><body><div class='wrap'>");

        // Branding is the logo only (the company/department text duplicated it).
        string? logo = EmbedImage(Branding.LogoPath);
        if (logo != null)
        {
            sb.Append("<div class='brandbar'><div></div>");
            sb.Append("<img class='logo' src='").Append(logo).Append("' alt='logo'>");
            sb.Append("</div>");
        }

        sb.Append("<h1>").Append(Enc(TitleOf(m))).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(m.Subtitle))
            sb.Append("<p class='subtitle'>").Append(Enc(m.Subtitle.Trim())).Append("</p>");

        sb.Append("<div class='metarow'>");
        foreach (var (label, value) in MetaBits(m))
            sb.Append("<div class='item'><span>").Append(Enc(label)).Append("</span>")
              .Append(Enc(value)).Append("</div>");
        sb.Append("</div>");

        int n = 0;
        string? currentApp = null;
        foreach (var s in steps)
        {
            if (!string.IsNullOrWhiteSpace(s.Context) && s.Context != currentApp)
            {
                currentApp = s.Context;
                sb.Append("<h2>In ").Append(Enc(s.Context)).Append("</h2>");
            }
            n++;
            sb.Append("<div class='step'><div class='num'>").Append(n).Append("</div><div class='body'><p>")
              .Append(Enc(s.Caption)).Append("</p>");
            string? img = EmbedImage(s.ScreenshotPath);
            if (img != null)
                sb.Append("<img src='").Append(img).Append("' alt='Step ").Append(n).Append("'>");
            sb.Append("</div></div>");
        }

        sb.Append("<footer>Generated by WriteUp</footer></div></body></html>");
        return sb.ToString();
    }

    private static string? EmbedImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            string mime = ext is "jpg" or "jpeg" ? "image/jpeg" : ext == "gif" ? "image/gif" : "image/png";
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private const string Css = @"
:root { --accent:#8a0101; --ink:#1a1a1a; --muted:#6b6b6b; --line:#e7e7e7; }
* { box-sizing:border-box; }
body { margin:0; font:16px/1.6 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif; color:var(--ink); background:#fafafa; }
.wrap { max-width:820px; margin:0 auto; padding:48px 24px 96px; }
.brandbar { display:flex; align-items:center; justify-content:space-between; gap:16px; padding-bottom:16px; border-bottom:2px solid var(--accent); margin-bottom:28px; }
.brandbar .co { font-weight:700; font-size:18px; }
.brandbar .co small { display:block; font-weight:500; font-size:13px; color:var(--muted); }
.brandbar img.logo { height:44px; width:auto; }
h1 { font-size:32px; margin:0 0 4px; letter-spacing:-.02em; }
.subtitle { color:var(--muted); font-size:17px; margin:0 0 16px; }
.metarow { display:flex; flex-wrap:wrap; gap:8px 24px; margin:0 0 28px; padding:12px 0; border-top:1px solid var(--line); border-bottom:1px solid var(--line); }
.metarow .item { font-size:14px; }
.metarow .item span { color:var(--muted); text-transform:uppercase; letter-spacing:.06em; font-size:11px; display:block; }
h2 { font-size:14px; text-transform:uppercase; letter-spacing:.08em; color:var(--accent); margin:40px 0 8px; }
.step { display:flex; gap:16px; padding:16px 0; border-top:1px solid var(--line); }
.num { flex:0 0 32px; height:32px; border-radius:50%; background:var(--accent); color:#fff; font-weight:700; display:flex; align-items:center; justify-content:center; font-size:14px; }
.body { flex:1; min-width:0; }
.body p { margin:4px 0 0; }
img { width:100%; border:1px solid var(--line); border-radius:8px; margin-top:12px; display:block; }
footer { margin-top:48px; color:var(--muted); font-size:13px; text-align:center; }
@media print { body { background:#fff; } .wrap { max-width:none; padding:0 12px; } .step,.metarow,.brandbar { break-inside:avoid; } img { break-inside:avoid; } footer { display:none; } }
";
}
