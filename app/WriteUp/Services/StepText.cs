using System.Net;
using System.Text.RegularExpressions;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Small, safe formatting vocabulary shared by every export. No raw HTML.</summary>
public static class StepText
{
    public record Span(string Text, bool Bold = false, bool Italic = false, string? Link = null);
    private static readonly Regex Tokens = new(@"\*\*\*(.+?)\*\*\*|\*\*(.+?)\*\*|\*(.+?)\*|\[([^\]]+)\]\(([^\s)]+)\)", RegexOptions.Singleline);
    public static bool SafeLink(string value) =>
        Regex.IsMatch(value, @"^#step-[a-fA-F0-9]{32}$") ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto");
    public static IEnumerable<Span> Parse(string text)
    {
        int offset = 0;
        foreach (Match m in Tokens.Matches(text))
        {
            if (m.Index > offset) yield return new(text[offset..m.Index]);
            if (m.Groups[1].Success) yield return new(m.Groups[1].Value, Bold: true, Italic: true);
            else if (m.Groups[2].Success) yield return new(m.Groups[2].Value, Bold: true);
            else if (m.Groups[3].Success) yield return new(m.Groups[3].Value, Italic: true);
            else if (SafeLink(m.Groups[5].Value)) yield return new(m.Groups[4].Value, Link: m.Groups[5].Value);
            else yield return new(m.Value);
            offset = m.Index + m.Length;
        }
        if (offset < text.Length) yield return new(text[offset..]);
    }
    public static string Html(string text) => string.Concat(Parse(text).Select(s =>
    {
        string t = WebUtility.HtmlEncode(s.Text).Replace("\n", "<br>");
        return s.Link != null ? $"<a href=\"{WebUtility.HtmlEncode(s.Link)}\">{t}</a>" :
            s.Bold && s.Italic ? $"<strong><em>{t}</em></strong>" : s.Bold ? $"<strong>{t}</strong>" : s.Italic ? $"<em>{t}</em>" : t;
    }));
    public static Dictionary<string, string> Numbers(IReadOnlyList<Step> steps)
    {
        var result = new Dictionary<string, string>();
        var counts = new int[5];
        int previous = 0;
        foreach (var step in steps)
        {
            int level = result.Count == 0 ? 0 : Math.Min(step.Level, previous + 1);
            counts[level]++;
            Array.Clear(counts, level + 1, counts.Length - level - 1);
            result[step.Id] = string.Join(".", counts.Take(level + 1));
            previous = level;
        }
        return result;
    }
    public static string ResolveReferences(string text, IReadOnlyDictionary<string, string> numbers) =>
        Regex.Replace(text, @"\[([^\]]+)\]\(#step-([a-fA-F0-9]{32})\)", match =>
        {
            string id = match.Groups[2].Value, label = match.Groups[1].Value;
            if (!numbers.TryGetValue(id, out var number)) return $"[Missing step: {label}]";
            if (Regex.IsMatch(label, @"^Step \d+(\.\d+)*$")) label = "Step " + number;
            return $"[{label}](#step-{id})";
        });

    public static string Reference(Step step, IReadOnlyDictionary<string, string> numbers)
    {
        string marker = string.IsNullOrWhiteSpace(step.Marker) ? "" : $"Marker {step.Marker}. ";
        if (string.IsNullOrEmpty(step.SharedImageStepId)) return marker;
        return marker + (numbers.TryGetValue(step.SharedImageStepId, out var n)
            ? $"See [screenshot in step {n}](#step-{step.SharedImageStepId})."
            : "Screenshot reference is missing — choose another screenshot in Edit.");
    }
}
