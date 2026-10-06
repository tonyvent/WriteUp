using WriteUp.Models;
using WriteUp.Services;
using System.Text.Json;

int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    passed++;
    Console.WriteLine("PASS " + description);
}
var defaults = JsonSerializer.Deserialize<AppSettings>("{}");
Check(defaults!.NarrationEnabled && defaults.CaptureClicks && defaults.CaptureTyping, "Old settings enable narration and action capture by default");
var prefs = new AppSettings { MicrophoneDevice = 2, MicrophoneName = "USB microphone", SpeechRecognizerId = "en-US", CaptureTyping = false };
var prefsAgain = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(prefs))!;
Check(prefsAgain.MicrophoneDevice == 2 && prefsAgain.MicrophoneName == "USB microphone" && !prefsAgain.CaptureTyping, "Microphone and capture preferences round-trip");
Check(defaults.TranscriptionProvider == "Windows", "Existing installs retain local dictation without requiring cloud credentials");
var cloud = new AppSettings { TranscriptionProvider = "Azure", AzureSpeechRegion = "eastus", AzureSpeechLanguage = "en-US", AzureSpeechPhrases = "Civil 3D; Dynamic Engineering", AzureSpeechKeyEncrypted = "encrypted-fixture" };
var cloudAgain = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(cloud))!;
Check(cloudAgain.TranscriptionProvider == "Azure" && cloudAgain.AzureSpeechRegion == "eastus" && cloudAgain.AzureSpeechLanguage == "en-US" && cloudAgain.AzureSpeechPhrases == cloud.AzureSpeechPhrases && cloudAgain.AzureSpeechKeyEncrypted == cloud.AzureSpeechKeyEncrypted, "Cloud preferences and encrypted credential round-trip independently of local recognizer");
using (var audio = new MicrophoneStream())
{
    var bytes = new byte[4];
    Check(audio.Read(bytes, 0, 0) == 0, "Zero-length microphone read returns immediately");
    var pending = Task.Run(() => audio.Read(bytes, 0, bytes.Length));
    Check(!pending.Wait(30), "Microphone stream waits for real audio instead of generating silence");
    audio.WriteAudio(new byte[] {1, 2, 3, 4}, 4);
    Check(pending.Wait(1000) && pending.Result == 4 && bytes.SequenceEqual(new byte[] {1,2,3,4}), "Audio samples reach the speech reader intact");
    Check(audio.Length == long.MaxValue && audio.Position == 4 && audio.Seek(0, SeekOrigin.Current) == 4, "Live audio supports System.Speech stream metadata queries");
    var end = Task.Run(() => audio.Read(bytes, 0, 4)); audio.Complete();
    Check(end.Wait(1000) && end.Result == 0, "Stopping audio unblocks recognition reads with EOF");
    Check(!audio.WriteAudio(new byte[] {1}, 1), "Late microphone callbacks cannot write after stopping");
}
using (var audio = new MicrophoneStream())
{
    bool accepted = true;
    for (int i = 0; i < 100; i++) accepted &= audio.WriteAudio(new byte[] {(byte)i}, 1);
    Check(accepted, "PCM buffer accepts bounded audio data");
    Check(!audio.WriteAudio(new byte[] {1}, 1), "Slow speech reader cannot grow microphone memory without bound");
}
var a = new Step { Caption = "Open form", ScreenshotPath = "one.png", Notes = "**Check** the account", Level = 0 };
var b = new Step { Caption = "Approve", SharedImageStepId = a.Id, Marker = "B", Level = 1 };
var steps = new List<Step> { a, b };
var history = new StepHistory();
history.Reset(steps);
steps.Remove(a); history.Record(steps);
var restored = history.Undo()!;
Check(restored.Count == 2 && restored[0].Id == a.Id && restored[0].ScreenshotPath == "one.png", "Undo deletion restores the original step and screenshot");
Check(restored[1].SharedImageStepId == a.Id, "Undo preserves shared screenshot references");
Check(history.Redo()!.Count == 1, "Redo reapplies deletion");
restored = history.Undo()!;
restored[0].Caption = "Different"; history.Record(restored);
Check(!history.CanRedo, "A new edit clears redo history");
Check(history.Undo()![0].Caption == "Open form", "History snapshots do not alias live models");
history.Reset(steps); Check(!history.CanUndo && !history.CanRedo, "Opening another session clears history");
Check(b.ImagePath == null && a.ImagePath == "one.png", "Shared screenshot is rendered once");
a.HideImage = true; Check(!a.HasScreenshot && a.ImagePath == null, "Text-only steps hide images without deleting them");
a.HideImage = false;
Check(a.ImagePath == "one.png", "Hidden screenshot can be restored");
var c = new Step { Level = 4 };
var d = new Step { Level = 0 };
var numbers = StepText.Numbers(new[] { a, b, c, d });
Check(numbers[a.Id] == "1" && numbers[b.Id] == "1.1" && numbers[c.Id] == "1.1.1" && numbers[d.Id] == "2", "Nested numbering normalizes skipped levels");
Check(StepText.Numbers(new[] { c })[c.Id] == "1", "First step always begins at the top level");
Check(StepText.Reference(b, numbers).Contains("#step-" + a.Id), "Screenshot reference uses stable IDs");
Check(StepText.Reference(b, new Dictionary<string,string>()).Contains("missing"), "Missing reference is visible to the author");
Check(StepText.ResolveReferences($"[Step 99](#step-{b.Id})", numbers).StartsWith("[Step 1.1]"), "Step reference labels follow renumbering");
Check(StepText.ResolveReferences($"[Step 1](#step-{Guid.NewGuid():N})", numbers).Contains("Missing step"), "Deleted text references are visible in exported instructions");
Check((int)AnnotationKind.Redact == 4 && (int)AnnotationKind.Blur == 3, "Old annotation enum values stay compatible");
Check(StepText.Html("***Both***").Contains("<strong><em>Both</em></strong>"), "Combined bold and italic survive export");
Check(!StepText.SafeLink("javascript:alert(1)") && !StepText.SafeLink("file:///secret"), "Unsafe link schemes rejected");
Check(StepText.SafeLink("https://example.com") && StepText.SafeLink("#step-" + a.Id), "Web and internal step links supported");
Check(StepText.Html("<script>alert(1)</script>").Contains("&lt;script&gt;"), "HTML text is escaped");
var html = ReportWriter.Html(new SessionMeta(), new[] { a, b });
Check(html.Contains("<strong>Check</strong>") && html.Contains("1.1") && html.Contains("href=\"#step-" + a.Id), "HTML includes formatting, substeps and internal links");
var markdown = ReportWriter.Markdown(new SessionMeta(), new[] { a, b });
Check(markdown.Contains("**Check**") && markdown.Contains("**1.1.**"), "Markdown includes notes and nested numbering");
string dir = Path.Combine(Path.GetTempPath(), "writeup-tests-" + Guid.NewGuid());
Directory.CreateDirectory(dir);
try
{
    // Persistence is path-based; the contents are irrelevant to this test.
    a.ScreenshotPath = Path.Combine(dir, "one.png"); File.WriteAllText(a.ScreenshotPath, "image fixture");
    a.ZoomImagePath = Path.Combine(dir, "zoom.png"); File.WriteAllText(a.ZoomImagePath, "zoom fixture");
    a.ShowZoom = false;
    SessionStore.Save(dir, new SessionMeta { Title = "Payroll" }, new[] { a, b });
    string jsonPath = Path.Combine(dir, SessionStore.FileName);
    var loaded = SessionStore.Load(jsonPath);
    Check(loaded.Meta.Title == "Payroll" && loaded.Steps[0].Notes == a.Notes, "Session persists metadata and rich notes");
    Check(loaded.Steps[1].SharedImageStepId == a.Id && loaded.Steps[1].Marker == "B" && loaded.Steps[1].Level == 1, "Session persists shared image, marker and nesting");
    Check(loaded.Steps[0].ScreenshotPath == a.ScreenshotPath && !loaded.Steps[0].ShowZoom, "Session resolves portable image paths and zoom state");
    Check(!File.ReadAllText(jsonPath).Contains(dir), "Screenshot paths inside the session remain relative");
    File.WriteAllText(jsonPath, "{\"FormatVersion\":1,\"Steps\":[{\"Kind\":\"Click\",\"Caption\":\"Old step\",\"Screenshot\":\"one.png\"}]}");
    var old = SessionStore.Load(jsonPath).Steps[0];
    Check(old.Caption == "Old step" && old.Id.Length == 32 && old.Notes == "" && old.Level == 0, "Version 1 sessions load with safe defaults");
    File.Delete(a.ScreenshotPath);
    Check(SessionStore.Load(jsonPath).Steps[0].ScreenshotPath == null, "Missing image does not destroy a saved instruction");
}
finally { Directory.Delete(dir, true); }
Console.WriteLine($"{passed} checks passed.");

namespace WriteUp.Services
{
    // The report tests exclude only the desktop-specific logo extractor.
    internal static class Branding { public static string LogoPath => ""; }
}
