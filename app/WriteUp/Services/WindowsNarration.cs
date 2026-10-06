using NAudio.CoreAudioApi;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Windows' built-in online dictation. No cloud account, key, or downloaded model.</summary>
internal sealed class WindowsNarration : IDisposable
{
    private SpeechRecognizer? _recognizer;
    private MMDeviceEnumerator? _devices;
    private MMDevice? _microphone;
    private System.Threading.Timer? _meter;
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _stopping, _listening, _disposed;
    private Task? _stop;
    public bool IsListening => _listening && !_stopping;
    public event Action<DateTime, string>? Transcribed;
    public event Action<string>? Hypothesis;
    public event Action<string>? Status;
    public event Action<int>? AudioLevel;
    public event Action? Ended;

    public static List<NarrationService.Language> Languages() => SpeechRecognizer.SupportedTopicLanguages
        .Select(l => new NarrationService.Language(l.LanguageTag, l.DisplayName)).ToList();

    public async Task StartAsync(AppSettings settings)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsNarration));
        string stage = "initializing the recognizer";
        string identity;
        try { identity = Windows.ApplicationModel.Package.Current.Id.Name; }
        catch { identity = "unpackaged"; }
        try
        {
            var language = string.IsNullOrWhiteSpace(settings.WindowsSpeechLanguage)
                ? SpeechRecognizer.SystemSpeechLanguage : new Language(settings.WindowsSpeechLanguage);
            var recognizer = new SpeechRecognizer(language); _recognizer = recognizer;
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(5);
            recognizer.HypothesisGenerated += (_, e) => { if (!_disposed) Hypothesis?.Invoke(e.Hypothesis.Text); };
            recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) =>
            {
                if (_disposed) return;
                var result = e.Result;
                if (result.Status == SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    var at = result.PhraseStartTime.LocalDateTime;
                    if (at.Year < 2000) at = DateTime.Now - result.PhraseDuration;
                    Transcribed?.Invoke(at, result.Text);
                }
                else Status?.Invoke("Windows could not understand that phrase. Repeat it or edit the step notes.");
                Hypothesis?.Invoke("");
            };
            recognizer.RecognitionQualityDegrading += (_, e) =>
                Status?.Invoke("Windows microphone quality: " + e.Problem + ". Check input volume and background noise.");
            recognizer.ContinuousRecognitionSession.Completed += (_, e) =>
            {
                _listening = false;
                StopMeter();
                if (!_stopping && !_disposed)
                    Status?.Invoke("Windows dictation ended (" + e.Status + "). Use Narrate to restart. Screenshots continue.");
                Ended?.Invoke();
            };
            Status?.Invoke("Connecting to Windows online speech recognition…");
            stage = "connecting to Windows dictation";
            var compilation = await recognizer.CompileConstraintsAsync().AsTask(_lifetime.Token);
            if (compilation.Status != SpeechRecognitionResultStatus.Success)
                throw new InvalidOperationException("Windows dictation is unavailable (" + compilation.Status + "). Enable Online speech recognition and microphone access in Windows Settings, and check your internet connection.");
            _lifetime.Token.ThrowIfCancellationRequested();
            stage = "starting microphone recognition";
            await recognizer.ContinuousRecognitionSession.StartAsync().AsTask(_lifetime.Token);
            _listening = true;
            StartMeter();
            Status?.Invoke("Listening with Windows online dictation using your Windows default microphone. Speak naturally; phrases become step notes.");
        }
        catch (Exception ex)
        {
            Dispose();
            uint code = unchecked((uint)ex.HResult);
            string help = code switch
            {
                0x80045509 => "Windows has not accepted online speech access. Click Windows speech settings, turn Online speech recognition on, then test again.",
                0x80070005 => "Windows denied access. Click Microphone permissions and allow microphone access for apps and desktop apps.",
                _ => "Check Online speech recognition, microphone permissions and the internet connection."
            };
            if (identity == "unpackaged")
                help += " This is the unregistered EXE/Visual Studio launch. Run app\\register-windows-app.cmd, then launch WriteUp (Windows dictation) from Start.";
            throw new InvalidOperationException($"Windows dictation failed while {stage}. Error 0x{code:X8}. App: {identity}.\n{help}\nWindows detail: {ex.Message}", ex);
        }
    }
    private void StartMeter()
    {
        try
        {
            _devices = new MMDeviceEnumerator();
            _microphone = _devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            _meter = new System.Threading.Timer(_ =>
            {
                try { if (!_disposed && _listening) AudioLevel?.Invoke((int)(_microphone!.AudioMeterInformation.MasterPeakValue * 100)); }
                catch { }
            }, null, 0, 100);
        }
        catch { /* The recognizer can work even when Windows cannot expose a level meter. */ }
    }
    private void StopMeter() { _meter?.Dispose(); _meter = null; AudioLevel?.Invoke(0); }
    public Task StopAsync() => _stop ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        _stopping = true;
        try
        {
            if (_recognizer != null && _listening)
                await _recognizer.ContinuousRecognitionSession.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            else _lifetime.Cancel();
        }
        catch { Status?.Invoke("Windows did not finish the last phrase. Check the final step notes."); }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _stopping = true; _listening = false;
        _lifetime.Cancel(); StopMeter();
        try { _recognizer?.Dispose(); } catch { } _recognizer = null;
        try { _microphone?.Dispose(); _devices?.Dispose(); } catch { }
        _microphone = null; _devices = null;
    }
}
