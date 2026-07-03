using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>
/// Persists a recording session to a <c>session.json</c> inside its session
/// folder, so work can be closed and reopened later. Screenshot references are
/// stored relative to the session folder whenever possible, so a whole session
/// folder can be moved, renamed, or shared over a network drive and still open.
/// Note: this only has effect when "Delete session folders when the app
/// closes" is off in Settings — otherwise the folder (including this file) is
/// removed on exit by design.
/// </summary>
public static class SessionStore
{
    public const string FileName = "session.json";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ---- DTOs (kept separate from the UI models so the file format is stable) ----

    public sealed class SessionDto
    {
        public int FormatVersion { get; set; } = 1;
        public string SavedAt { get; set; } = "";
        public MetaDto Meta { get; set; } = new();
        public List<StepDto> Steps { get; set; } = new();
    }

    public sealed class MetaDto
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Author { get; set; } = "";
        public string Company { get; set; } = "";
        public string Department { get; set; } = "";
        public string Revision { get; set; } = "";
        public string Date { get; set; } = "";
        public string LogoPath { get; set; } = "";
    }

    public sealed class StepDto
    {
        public string Kind { get; set; } = "Note";
        public DateTime Timestamp { get; set; }
        public string Window { get; set; } = "";
        public string App { get; set; } = "";
        public string AutoContext { get; set; } = "";
        public string Context { get; set; } = "";
        public string Button { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        /// <summary>Relative to the session folder when the file lives inside
        /// it; absolute otherwise.</summary>
        public string? Screenshot { get; set; }
        public string? ZoomImage { get; set; }
        public bool ShowZoom { get; set; } = true;
        public string Caption { get; set; } = "";
    }

    // ---- save ---------------------------------------------------------------

    public static void Save(string sessionDir, SessionMeta meta, IReadOnlyList<Step> steps)
    {
        var dto = new SessionDto
        {
            SavedAt = DateTime.Now.ToString("o"),
            Meta = new MetaDto
            {
                Title = meta.Title, Subtitle = meta.Subtitle, Author = meta.Author,
                Company = meta.Company, Department = meta.Department,
                Revision = meta.Revision, Date = meta.Date, LogoPath = meta.LogoPath
            },
            Steps = steps.Select(s => new StepDto
            {
                Kind = s.Kind.ToString(),
                Timestamp = s.Timestamp,
                Window = s.Window, App = s.App,
                AutoContext = s.AutoContext, Context = s.Context,
                Button = s.Button, X = s.X, Y = s.Y,
                Screenshot = MakePortable(sessionDir, s.ScreenshotPath),
                ZoomImage = MakePortable(sessionDir, s.ZoomImagePath),
                ShowZoom = s.ShowZoom,
                Caption = s.Caption
            }).ToList()
        };

        string path = Path.Combine(sessionDir, FileName);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private static string? MakePortable(string sessionDir, string? shotPath)
    {
        if (string.IsNullOrWhiteSpace(shotPath)) return null;
        try
        {
            string rel = Path.GetRelativePath(sessionDir, shotPath);
            // Keep it relative only if the file is actually inside the folder.
            return rel.StartsWith("..") || Path.IsPathRooted(rel) ? shotPath : rel;
        }
        catch { return shotPath; }
    }

    // ---- load ---------------------------------------------------------------

    public static (MetaDto Meta, List<Step> Steps) Load(string jsonPath)
    {
        string dir = Path.GetDirectoryName(jsonPath)!;
        var dto = JsonSerializer.Deserialize<SessionDto>(File.ReadAllText(jsonPath))
                  ?? throw new InvalidDataException("The session file is empty or unreadable.");

        var steps = new List<Step>();
        foreach (var d in dto.Steps)
        {
            steps.Add(new Step
            {
                Kind = Enum.TryParse<StepKind>(d.Kind, out var k) ? k : StepKind.Note,
                Timestamp = d.Timestamp,
                Window = d.Window, App = d.App,
                AutoContext = d.AutoContext, Context = d.Context,
                Button = d.Button, X = d.X, Y = d.Y,
                ScreenshotPath = Resolve(dir, d.Screenshot),
                ZoomImagePath = Resolve(dir, d.ZoomImage),
                ShowZoom = d.ShowZoom,
                Caption = d.Caption
            });
        }

        return (dto.Meta, steps);
    }

    private static string? Resolve(string dir, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        string full = Path.IsPathRooted(stored) ? stored : Path.GetFullPath(Path.Combine(dir, stored));
        return File.Exists(full) ? full : null;
    }

    public static void ApplyMeta(MetaDto values, SessionMeta meta)
    {
        meta.Title = values.Title;
        meta.Subtitle = values.Subtitle;
        meta.Author = values.Author;
        meta.Company = values.Company;
        meta.Department = values.Department;
        meta.Revision = values.Revision;
        meta.Date = values.Date;
        meta.LogoPath = values.LogoPath;
    }
}
