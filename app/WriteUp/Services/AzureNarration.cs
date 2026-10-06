using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Continuous Azure recognition of the same selected PCM microphone used for local dictation.</summary>
internal sealed class AzureNarration : IDisposable
{
    private readonly object _audioLock = new();
    private readonly TaskCompletionSource<bool> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SpeechConfig? _config;
    private AudioStreamFormat? _format;
    private PushAudioInputStream? _input;
    private AudioConfig? _audio;
    private SpeechRecognizer? _recognizer;
    private WaveInEvent? _capture;
    private DateTime _started;
    private volatile bool _stopping, _finished, _inputClosed;
    private Task? _stopTask;
    public bool IsListening => _recognizer != null && !_stopping && !_finished;
    public event Action<DateTime, string>? Transcribed;
    public event Action<string>? Hypothesis;
    public event Action<string>? Status;
    public event Action<int>? AudioLevel;
    public event Action? Ended;

    public void Start(AppSettings settings)
    {
        var key = SpeechCredential.Decrypt(settings.AzureSpeechKeyEncrypted);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(settings.AzureSpeechRegion) || string.IsNullOrWhiteSpace(settings.AzureSpeechLanguage))
            throw new InvalidOperationException("Enter your Azure Speech key, region and language in Settings, then test the microphone.");
        if (settings.MicrophoneDevice >= 0 && !NarrationService.Microphones().Any(d => d.Number == settings.MicrophoneDevice && d.Name == settings.MicrophoneName))
            throw new InvalidOperationException("The selected microphone changed or was disconnected. Select it again in Settings.");
        try
        {
            _config = SpeechConfig.FromSubscription(key, settings.AzureSpeechRegion.Trim());
            _config.SpeechRecognitionLanguage = settings.AzureSpeechLanguage.Trim();
            // Let a natural pause finish a phrase without splitting every short hesitation.
            _config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "1000");
            _format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            _input = AudioInputStream.CreatePushStream(_format);
            _audio = AudioConfig.FromStreamInput(_input);
            _recognizer = new SpeechRecognizer(_config, _audio);
            var phrases = PhraseListGrammar.FromRecognizer(_recognizer);
            foreach (var phrase in settings.AzureSpeechPhrases.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().Take(500))
                phrases.AddPhrase(phrase);
            _recognizer.Recognizing += (_, e) => Hypothesis?.Invoke(e.Result.Text);
            _recognizer.Recognized += (_, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                    Transcribed?.Invoke(_started.AddTicks((long)e.Result.OffsetInTicks), e.Result.Text);
                else if (e.Result.Reason == ResultReason.NoMatch)
                    Status?.Invoke("Speech was unclear. Please repeat or edit the step notes.");
                Hypothesis?.Invoke("");
            };
            _recognizer.Canceled += (_, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                    Status?.Invoke($"Azure transcription stopped ({e.ErrorCode}). Check your connection, Speech key, region, and quota; screenshots continue recording.");
                Finish();
            };
            _recognizer.SessionStopped += (_, _) => Finish();
            _capture = new WaveInEvent { DeviceNumber = settings.MicrophoneDevice, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
            _capture.DataAvailable += (_, e) =>
            {
                lock (_audioLock)
                {
                    if (_inputClosed || _input == null) return;
                    try { _input.Write(e.Buffer, e.BytesRecorded); }
                    catch { CloseInput(); Status?.Invoke("Azure audio input failed. Stop narration and test the connection again."); return; }
                }
                int peak = 0;
                for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                    peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(e.Buffer, i)));
                AudioLevel?.Invoke(Math.Min(100, peak * 100 / 32768));
            };
            _capture.RecordingStopped += (_, e) =>
            {
                CloseInput();
                if (e.Exception != null) Status?.Invoke("Microphone disconnected or unavailable. Screenshots continue recording.");
            };
            _recognizer.StartContinuousRecognitionAsync().GetAwaiter().GetResult();
            _started = DateTime.Now;
            if (!_finished)
            {
                _capture.StartRecording();
                Status?.Invoke("Listening with Microsoft Azure Speech. Finalized phrases appear beside the recorded step.");
            }
        }
        catch { Dispose(); throw; }
    }

    private void CloseInput()
    {
        lock (_audioLock)
        {
            if (_inputClosed) return;
            _inputClosed = true;
            try { _input?.Close(); } catch { }
        }
    }
    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        try { _capture?.StopRecording(); } catch { }
        _ended.TrySetResult(true);
        AudioLevel?.Invoke(0);
        Ended?.Invoke();
    }
    public Task StopAsync() => _stopTask ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        _stopping = true;
        try
        {
            try { _capture?.StopRecording(); } catch { }
            CloseInput();
            // End-of-stream lets Azure finalize buffered speech before requesting stop.
            if (!_finished && await Task.WhenAny(_ended.Task, Task.Delay(8000)) != _ended.Task)
                Status?.Invoke("Azure did not finalize the last phrase in time. Check the final step notes.");
            if (_recognizer != null)
            {
                var stop = _recognizer.StopContinuousRecognitionAsync();
                if (await Task.WhenAny(stop, Task.Delay(5000)) == stop) await stop;
                else Status?.Invoke("Azure connection did not close in time. Check the final step notes.");
            }
        }
        catch { Status?.Invoke("Azure transcription connection ended. Check the final step notes."); }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        _stopping = true;
        CloseInput();
        try { _capture?.Dispose(); } catch { }
        _capture = null;
        try { _recognizer?.Dispose(); } catch { }
        _recognizer = null;
        _audio?.Dispose(); _audio = null;
        _input?.Dispose(); _input = null;
        _format?.Dispose(); _format = null;
        _config = null; // SpeechConfig does not implement IDisposable.
        _finished = true;
        _ended.TrySetResult(true);
        AudioLevel?.Invoke(0);
    }
}
