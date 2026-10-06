using System.Speech.Recognition;
using System.Speech.AudioFormat;
using NAudio.Wave;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Built-in Windows online dictation, with an explicit legacy local fallback.</summary>
public sealed class NarrationService : IDisposable
{
    public record Microphone(int Number, string Name) { public override string ToString() => Name; }
    public record Language(string Id, string Name) { public override string ToString() => Name; }
    public static List<Microphone> Microphones()
    {
        var result = new List<Microphone> { new(-1, "Windows default microphone") };
        for (int i = 0; i < WaveIn.DeviceCount; i++) result.Add(new(i, WaveIn.GetCapabilities(i).ProductName));
        return result;
    }
    public static List<Language> Languages() => SpeechRecognitionEngine.InstalledRecognizers()
        .Select(r => new Language(r.Id, r.Culture.DisplayName + " — " + r.Name)).ToList();

    private WindowsNarration? _windows;
    private Task? _startup;
    private SpeechRecognitionEngine? _engine;
    private WaveInEvent? _capture;
    private MicrophoneStream? _stream;
    private DateTime _started;
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _stopTask;
    private volatile bool _completed, _stopping;
    public event Action<DateTime, string>? Transcribed;
    public event Action<string>? Hypothesis;
    public event Action<string>? Status;
    public event Action<int>? AudioLevel;
    public event Action? Ended;
    public bool IsListening => _windows?.IsListening ?? (_engine != null && !_completed && !_stopping);

    public Task StartAsync(AppSettings? settings = null) => _startup ??= StartCoreAsync(settings ?? new AppSettings());
    private async Task StartCoreAsync(AppSettings settings)
    {
        if (settings.TranscriptionProvider != "WindowsLegacy")
        {
            _windows = new WindowsNarration();
            _windows.Transcribed += (at, text) => Transcribed?.Invoke(at, text);
            _windows.Hypothesis += text => Hypothesis?.Invoke(text);
            _windows.Status += text => Status?.Invoke(text);
            _windows.AudioLevel += level => AudioLevel?.Invoke(level);
            _windows.Ended += () => Ended?.Invoke();
            try { await _windows.StartAsync(settings); } catch { Dispose(); throw; }
            return;
        }
        StartLegacy(settings);
    }
    private void StartLegacy(AppSettings settings)
    {
        var available = SpeechRecognitionEngine.InstalledRecognizers();
        var recognizer = string.IsNullOrEmpty(settings.SpeechRecognizerId)
            ? available.FirstOrDefault(r => r.Culture.Equals(System.Globalization.CultureInfo.CurrentUICulture)) ?? available.FirstOrDefault()
            : available.FirstOrDefault(r => r.Id == settings.SpeechRecognizerId);
        if (recognizer == null)
            throw new InvalidOperationException("No matching Windows speech recognizer is installed. Install a speech language in Windows Settings, then select it in WriteUp Settings.");
        var engine = new SpeechRecognitionEngine(recognizer);
        _engine = engine;
        try
        {
            engine.LoadGrammar(new DictationGrammar());
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(600);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1000);
            if (settings.MicrophoneDevice < 0) engine.SetInputToDefaultAudioDevice();
            else
            {
                var device = Microphones().FirstOrDefault(d => d.Number == settings.MicrophoneDevice && d.Name == settings.MicrophoneName)
                    ?? throw new InvalidOperationException("The selected microphone changed or was disconnected. Select it again in Settings.");
                _stream = new MicrophoneStream();
                _capture = new WaveInEvent { DeviceNumber = device.Number, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
                var stream = _stream;
                _capture.DataAvailable += (_, e) =>
                {
                    if (!stream.WriteAudio(e.Buffer, e.BytesRecorded) && !_stopping)
                    {
                        stream.Complete();
                        Status?.Invoke("Microphone audio could not be processed in time. Stop narration and retry.");
                    }
                };
                _capture.RecordingStopped += (_, e) =>
                {
                    stream.Complete();
                    if (e.Exception != null) Status?.Invoke("Microphone disconnected or unavailable: " + e.Exception.Message);
                };
                engine.SetInputToAudioStream(stream, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            }
            engine.AudioLevelUpdated += (_, e) => AudioLevel?.Invoke(e.AudioLevel);
            engine.SpeechHypothesized += (_, e) => Hypothesis?.Invoke(e.Result.Text);
            engine.SpeechRecognized += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Result.Text))
                    Transcribed?.Invoke(_started + (e.Result.Audio?.AudioPosition ?? TimeSpan.Zero), e.Result.Text);
                Hypothesis?.Invoke("");
            };
            engine.SpeechRecognitionRejected += (_, _) =>
            { Hypothesis?.Invoke(""); Status?.Invoke("Speech was unclear. Please repeat or edit the step notes."); };
            engine.RecognizeCompleted += (_, e) =>
            {
                _completed = true;
                if (e.Error != null) Status?.Invoke("Narration stopped: " + e.Error.Message);
                else if (!_stopping) Status?.Invoke("Microphone input ended. Check the device in Settings and restart narration.");
                _stopping = true;
                try { _capture?.StopRecording(); } catch { }
                _stopped.TrySetResult(true);
                AudioLevel?.Invoke(0);
                Ended?.Invoke();
            };
            _started = DateTime.Now;
            _capture?.StartRecording();
            engine.RecognizeAsync(RecognizeMode.Multiple);
            Status?.Invoke("Listening — speak naturally; finalized phrases appear beside the recorded step.");
        }
        catch { Dispose(); throw; }
    }

    public Task StopAsync() => _stopTask ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        if (_windows != null) { await _windows.StopAsync(); Dispose(); return; }
        var engine = _engine;
        if (engine == null) return;
        _stopping = true;
        try
        {
            if (!_completed)
            {
                engine.RecognizeAsyncStop();
                _capture?.StopRecording();
                _stream?.Complete();
                if (await Task.WhenAny(_stopped.Task, Task.Delay(5000)) != _stopped.Task)
                    Status?.Invoke("The last phrase did not finish. Check the final step notes.");
            }
        }
        catch (Exception ex) { Status?.Invoke("Narration stopped: " + ex.Message); }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        _stopping = true;
        _windows?.Dispose(); _windows = null;
        var engine = _engine; _engine = null;
        _stream?.Complete(); // unblock the recognizer before waiting for disposal
        try { _capture?.Dispose(); } catch { }
        _capture = null;
        try { if (engine != null && !_completed) engine.RecognizeAsyncCancel(); } catch { }
        try { engine?.Dispose(); } catch { }
        _stream?.Dispose(); _stream = null;
        AudioLevel?.Invoke(0);
    }
}
