using System;

namespace WriteUp.Models;

/// <summary>A user-submitted bug report or feedback item. Context fields
/// (version, OS, time) are filled in by the service on submit.</summary>
public class FeedbackReport
{
    public string Category { get; set; } = "Bug";
    public string Message { get; set; } = "";
    public string Contact { get; set; } = "";   // optional name/email
    public string AppVersion { get; set; } = "";
    public string Os { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
