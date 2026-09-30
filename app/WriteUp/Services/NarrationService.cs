using System.Speech.Recognition;

namespace WriteUp.Services;

/// <summary>Opt-in local dictation; no audio files or cloud service.</summary>
public sealed class NarrationService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private DateTime _started;
    private TaskCompletionSource<bool>? _stopped;
    private Task? _stopTask;
    private bool _completed;
    public event Action<DateTime, string>? Transcribed;
    public event Action<string>? Status;
    public bool IsListening => _engine != null;

    public void Start()
    {
        if (_engine != null) return;
        var available = SpeechRecognitionEngine.InstalledRecognizers();
        var recognizer = available.FirstOrDefault(r => r.Culture.Equals(System.Globalization.CultureInfo.CurrentUICulture))
            ?? available.FirstOrDefault()
            ?? throw new InvalidOperationException("Install a Windows speech recognition language and enable microphone access in Windows Settings.");
        var engine = new SpeechRecognitionEngine(recognizer);
        try
        {
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Result.Text))
                    Transcribed?.Invoke(_started + (e.Result.Audio?.AudioPosition ?? TimeSpan.Zero), e.Result.Text);
            };
            engine.SpeechRecognitionRejected += (_, _) => Status?.Invoke("Speech was unclear. Please repeat or edit the step notes.");
            engine.RecognizeCompleted += (_, e) =>
            {
                _completed = true;
                if (e.Error != null) Status?.Invoke("Narration stopped: " + e.Error.Message);
                _stopped?.TrySetResult(true);
            };
            _started = DateTime.Now;
            _engine = engine;
            engine.RecognizeAsync(RecognizeMode.Multiple);
            Status?.Invoke("Microphone on — narration is added to step notes.");
        }
        catch { _engine = null; engine.Dispose(); throw; }
    }

    public Task StopAsync() => _stopTask ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        var engine = _engine;
        if (engine == null) return;
        if (_completed) { Dispose(); return; }
        _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.RecognizeAsyncStop();
        if (await Task.WhenAny(_stopped.Task, Task.Delay(5000)) != _stopped.Task)
            Status?.Invoke("Narration stopped before the final phrase completed. Check the last step notes.");
        Dispose();
    }

    public void Dispose()
    {
        var engine = _engine;
        _engine = null;
        if (engine == null) return;
        try { if (!_completed) engine.RecognizeAsyncCancel(); }
        finally { engine.Dispose(); }
    }
}
