using NAudio.CoreAudioApi;
using Windows.Globalization;
using Windows.Media.Capture;
using Windows.Media;
using System.IO;
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
    private Task? _phraseLoop;
    private string _continuousFailure = "";
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
            stage = "checking the selected microphone";
            WindowsMicrophones.RequireSelected(settings.WindowsMicrophoneId);
            stage = "checking microphone permission and availability";
            // Follow the Windows sample: request/check audio access, then release
            // the capture device before opening the speech recognizer.
            using (var permissionCheck = new MediaCapture())
            {
                await permissionCheck.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Audio,
                    MediaCategory = MediaCategory.Speech
                }).AsTask(_lifetime.Token);
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            stage = "initializing the recognizer";
            var language = string.IsNullOrWhiteSpace(settings.WindowsSpeechLanguage)
                ? SpeechRecognizer.SystemSpeechLanguage : new Language(settings.WindowsSpeechLanguage);
            var recognizer = new SpeechRecognizer(language); _recognizer = recognizer;
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            Status?.Invoke("Connecting to Windows online speech recognition…");
            stage = "connecting to Windows dictation";
            var compilation = await recognizer.CompileConstraintsAsync().AsTask(_lifetime.Token);
            if (compilation.Status != SpeechRecognitionResultStatus.Success)
                throw new InvalidOperationException("Windows dictation is unavailable (" + compilation.Status + "). Enable Online speech recognition and microphone access in Windows Settings, and check your internet connection.");
            _lifetime.Token.ThrowIfCancellationRequested();
            recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(5);
            recognizer.HypothesisGenerated += (_, e) => { if (!_disposed && ReferenceEquals(_recognizer, recognizer)) Hypothesis?.Invoke(e.Hypothesis.Text); };
            recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) =>
            {
                if (_disposed || !ReferenceEquals(_recognizer, recognizer)) return;
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
            {
                if (!_disposed && ReferenceEquals(_recognizer, recognizer))
                    Status?.Invoke("Windows microphone quality: " + e.Problem + ". Check input volume and background noise.");
            };
            recognizer.ContinuousRecognitionSession.Completed += (_, e) =>
            {
                if (!ReferenceEquals(_recognizer, recognizer)) return;
                _listening = false;
                StopMeter();
                if (!_stopping && !_disposed)
                    Status?.Invoke("Windows dictation ended (" + e.Status + "). Use Narrate to restart. Screenshots continue.");
                Ended?.Invoke();
            };
            stage = "starting microphone recognition";
            if (recognizer.State != SpeechRecognizerState.Idle)
                throw new InvalidOperationException("Windows recognizer is not ready to start: " + recognizer.State);
            try
            {
                stage = "requesting continuous recognition";
                var start = recognizer.ContinuousRecognitionSession.StartAsync();
                stage = "awaiting continuous recognition startup";
                await start.AsTask(_lifetime.Token);
            }
            catch (Exception ex) when (!_stopping && !_disposed &&
                unchecked((uint)WinRT.ExceptionHelpers.GetHRForException(ex)) == 0x80131509)
            {
                _continuousFailure = $"{stage}: {ex}";
                // The failure does not establish that Windows voice typing is broken.
                // Try the separately exposed single-phrase API once, using a fresh
                // recognizer so the failed session cannot overlap this one.
                stage = "starting Windows phrase recognition fallback";
                _recognizer = null;
                recognizer.Dispose();
                await StartPhraseRecognitionAsync(language);
                return;
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            _listening = true;
            StartMeter();
            Status?.Invoke("Listening with Windows online dictation using your Windows default microphone. Speak naturally; phrases become step notes.");
        }
        catch (Exception ex)
        {
            string recognizerState;
            try { recognizerState = _recognizer?.State.ToString() ?? "not created"; }
            catch { recognizerState = "unavailable"; }
            // C#/WinRT maps some native HRESULTs to CLR exception codes. Preserve
            // the native error instead of reporting only COR_E_INVALIDOPERATION.
            uint code = unchecked((uint)WinRT.ExceptionHelpers.GetHRForException(ex));
            string diagnosticPath = "";
            try
            {
                diagnosticPath = Path.Combine(SettingsStore.AppDataDir, "speech-error.txt");
                File.WriteAllText(diagnosticPath, $"Time: {DateTimeOffset.Now:O}\nStage: {stage}\nApp: {identity}\nState: {recognizerState}\nWindows: {Environment.OSVersion}\nNative HRESULT: 0x{code:X8}\nCLR HRESULT: 0x{ex.HResult:X8}\n{ex}\nOriginal continuous failure: {_continuousFailure}");
            }
            catch { diagnosticPath = ""; }
            Dispose();
            string help = code switch
            {
                0x80045509 => "Windows has not accepted online speech access. Click Windows speech settings, turn Online speech recognition on, then test again.",
                0x80070005 => "Windows denied access. Click Microphone permissions and allow microphone access for apps and desktop apps.",
                _ => "WriteUp could not start the Windows speech API. Win+H can work independently of this API; share the diagnostic file to investigate this failure."
            };
            if (identity == "unpackaged")
                help += " This is the unregistered EXE/Visual Studio launch. Run app\\register-windows-app.cmd, then launch WriteUp (Windows dictation) from Start.";
            throw new InvalidOperationException($"Windows dictation failed while {stage}. Error 0x{code:X8}. App: {identity}. State: {recognizerState}.\n{help}\nWindows detail: {ex.Message}\nDiagnostic file: {diagnosticPath}", ex);
        }
    }
    private async Task StartPhraseRecognitionAsync(Language language)
    {
        var recognizer = new SpeechRecognizer(language);
        _recognizer = recognizer;
        recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        var compiled = await recognizer.CompileConstraintsAsync().AsTask(_lifetime.Token);
        if (compiled.Status != SpeechRecognitionResultStatus.Success)
            throw new InvalidOperationException("Windows phrase recognition compilation failed: " + compiled.Status);
        recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
        recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recognizer.StateChanged += (_, e) =>
        {
            if (_disposed || _stopping) return;
            if (e.State == SpeechRecognizerState.Capturing)
            {
                ready.TrySetResult();
                Status?.Invoke("Windows phrase mode: listening. Pause between phrases while each is transcribed.");
            }
            else if (e.State == SpeechRecognizerState.Processing)
                Status?.Invoke("Windows phrase mode: transcribing — wait for Listening before speaking again.");
        };
        recognizer.HypothesisGenerated += (_, e) => { if (!_disposed) Hypothesis?.Invoke(e.Hypothesis.Text); };
        _phraseLoop = RunPhraseLoopAsync(recognizer, ready);
        // Do not report microphone ON merely because RecognizeAsync returned an
        // operation. Windows must first report that it is capturing audio.
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        if (_phraseLoop.IsCompleted)
            throw new InvalidOperationException("Windows phrase recognition ended during startup.");
        _listening = true;
        StartMeter();
    }
    private async Task RunPhraseLoopAsync(SpeechRecognizer recognizer, TaskCompletionSource ready)
    {
        try
        {
            while (!_stopping && !_disposed)
            {
                var result = await recognizer.RecognizeAsync().AsTask(_lifetime.Token);
                if (_disposed) break;
                if (result.Status == SpeechRecognitionResultStatus.Success)
                {
                    if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        var at = result.PhraseStartTime.LocalDateTime;
                        if (at.Year < 2000) at = DateTime.Now - result.PhraseDuration;
                        Transcribed?.Invoke(at, result.Text);
                    }
                }
                else if (result.Status != SpeechRecognitionResultStatus.TimeoutExceeded && !_stopping)
                    throw new InvalidOperationException("Windows phrase recognition failed: " + result.Status);
                Hypothesis?.Invoke("");
                // Bound retries even if Windows returns silence immediately.
                if (!_stopping) await Task.Delay(100, _lifetime.Token);
            }
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            if (!_stopping && !_disposed)
            {
                try
                {
                    File.WriteAllText(Path.Combine(SettingsStore.AppDataDir, "speech-error.txt"),
                        $"Time: {DateTimeOffset.Now:O}\nStage: Windows phrase recognition\nWindows: {Environment.OSVersion}\n{ex}\nOriginal continuous failure: {_continuousFailure}");
                }
                catch { }
                Status?.Invoke("Windows phrase recognition stopped: " + ex.Message + ". Screenshots continue. Diagnostic: speech-error.txt");
            }
        }
        finally
        {
            ready.TrySetCanceled();
            _listening = false;
            StopMeter();
            if (!_disposed) Ended?.Invoke();
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
            if (_recognizer != null && _phraseLoop != null && _listening)
            {
                if (_recognizer.State != SpeechRecognizerState.Idle)
                    await _recognizer.StopRecognitionAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                await _phraseLoop.WaitAsync(TimeSpan.FromSeconds(10));
            }
            else if (_recognizer != null && _listening)
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
