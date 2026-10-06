using System.IO;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Speech;
using NAudio.CoreAudioApi;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Modern on-device Windows AI speech only. Never downloads a model or selects another engine.</summary>
public sealed class NarrationService : IDisposable
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _stopLock = new();
    private SpeechRecognitionModel? _model;
    private StreamingRecognition? _recognition;
    private MMDevice? _microphone;
    private System.Threading.Timer? _meter;
    private Task? _startup, _stop;
    private DateTime _started;
    private volatile bool _listening, _stopping, _disposed;
    private string _stage = "checking Windows offline speech", _readyState = "not checked";
    public bool IsListening => _listening && !_stopping && !_disposed;
    public event Action<DateTime, string>? Transcribed;
    public event Action<string>? Hypothesis;
    public event Action<string>? Status;
    public event Action<int>? AudioLevel;
    public event Action? Ended;

    public static string CheckAvailability()
    {
        RequireWindowsPackage();
        var ready = SpeechRecognitionModelFactory.Default.GetReadyState();
        RequireReady(ready);
        return "Windows offline speech model is installed and ready. No download is needed.";
    }
    private static void RequireWindowsPackage()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            throw new InvalidOperationException("Windows offline AI speech requires Windows 11 24H2 (build 26100) or later.");
        try { _ = Windows.ApplicationModel.Package.Current.Id.Name; }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Launch the registered WriteUp 0.5.4 app from Start. Run app\\register-windows-app.cmd after updating; the offline API requires app identity and the systemAIModels capability.", ex);
        }
    }
    private static void RequireReady(AIFeatureReadyState state)
    {
        if (state == AIFeatureReadyState.Ready) return;
        if (state == AIFeatureReadyState.NotSupportedOnCurrentSystem)
            throw new InvalidOperationException("Windows reports that its offline AI speech API is not supported on this system. No other engine was started.");
        throw new InvalidOperationException($"Windows offline speech model is not ready ({state}). This API requires Microsoft's on-device speech component. WriteUp has not downloaded anything and cannot transcribe until Windows makes that component available. Win+H or Voice Access working does not establish that this separate API's model is installed.");
    }
    public Task StartAsync(AppSettings? settings = null) => _startup ??= StartCoreAsync(settings ?? new AppSettings());
    private async Task StartCoreAsync(AppSettings settings)
    {
        await _operations.WaitAsync();
        try
        {
            _cancel.Token.ThrowIfCancellationRequested();
            RequireWindowsPackage();
            var factory = SpeechRecognitionModelFactory.Default;
            var ready = factory.GetReadyState();
            _readyState = ready.ToString();
            RequireReady(ready);
            _stage = "opening selected microphone";
            var input = WindowsMicrophones.RequireSelected(settings.WindowsMicrophoneId);
            using (var devices = new MMDeviceEnumerator()) _microphone = devices.GetDevice(input.Id);
            _stage = "loading installed Windows offline speech model";
            Status?.Invoke("Loading the installed Windows offline speech model…");
            // Deliberately no EnsureReadyAsync: it can download a system model.
            _model = await factory.CreateAsync().AsTask(_cancel.Token);
            _cancel.Token.ThrowIfCancellationRequested();
            _stage = "starting offline transcription";
            var config = AudioConfiguration.FromAudioDevice(input.Id);
            var recognition = new StreamingRecognition(config, _model);
            _recognition = recognition;
            recognition.Recognizing += (_, e) => { if (!_disposed) Hypothesis?.Invoke(e.Text); };
            recognition.Recognized += (_, e) =>
            {
                if (_disposed || !e.IsFinal || string.IsNullOrWhiteSpace(e.Text)) return;
                // Windows reports the phrase offset relative to this audio session.
                var at = SpeechTiming.PhraseTime(_started, DateTime.Now, e.Offset);
                Transcribed?.Invoke(at, e.Text);
                Hypothesis?.Invoke("");
            };
            _started = DateTime.Now;
            await recognition.StartContinuousRecognitionAsync().AsTask(_cancel.Token);
            _cancel.Token.ThrowIfCancellationRequested();
            _listening = true;
            _meter = new System.Threading.Timer(_ =>
            {
                if (!IsListening) return;
                try
                {
                    if (_microphone!.State != DeviceState.Active)
                        throw new InvalidOperationException("Selected microphone disconnected or disabled.");
                    AudioLevel?.Invoke((int)(_microphone.AudioMeterInformation.MasterPeakValue * 100));
                }
                catch (Exception ex)
                {
                    if (!_stopping)
                    {
                        Status?.Invoke(ex.Message + " Narration stopped; select a microphone in Settings.");
                        _ = StopAsync();
                    }
                }
            }, null, 0, 100);
            Status?.Invoke("Listening offline — " + input.Name + ". Finalized phrases become step notes.");
        }
        catch (Exception ex)
        {
            _listening = false;
            ReleaseResources();
            if (_stopping && ex is OperationCanceledException) throw;
            string path = WriteDiagnostic(ex);
            throw new InvalidOperationException($"Windows offline speech failed while {_stage}. {ex.Message}\nDiagnostic: {path}", ex);
        }
        finally { _operations.Release(); }
    }
    private string WriteDiagnostic(Exception ex)
    {
        string path = Path.Combine(SettingsStore.AppDataDir, "speech-error.txt");
        try
        {
            File.WriteAllText(path, $"Time: {DateTimeOffset.Now:O}\nWriteUp: 0.5.4\nEngine: Microsoft.Windows.AI.Speech (offline only)\nSDK: 2.5.4-experimental\nStage: {_stage}\nModel readiness: {_readyState}\nWindows: {Environment.OSVersion}\nHRESULT: 0x{ex.HResult:X8}\n{ex}");
            return path;
        }
        catch { return "could not write diagnostic file"; }
    }
    public Task StopAsync()
    {
        lock (_stopLock)
        {
            _stopping = true;
            _cancel.Cancel();
            return _stop ??= StopCoreAsync();
        }
    }
    private async Task StopCoreAsync()
    {
        await _operations.WaitAsync();
        try
        {
            if (_recognition != null)
            {
                // Keep final-result handlers alive while the native stop drains.
                await _recognition.StopContinuousRecognitionAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic(ex);
            Status?.Invoke("Windows did not finish the last phrase. Check the final step notes. " + ex.Message);
        }
        finally
        {
            _disposed = true; _listening = false;
            ReleaseResources();
            _operations.Release();
            AudioLevel?.Invoke(0); Ended?.Invoke();
        }
    }
    private void ReleaseResources()
    {
        _meter?.Dispose(); _meter = null;
        try { _recognition?.Dispose(); } catch { } _recognition = null;
        try { _model?.Dispose(); } catch { } _model = null;
        try { _microphone?.Dispose(); } catch { } _microphone = null;
    }
    public void Dispose()
    {
        // Serialize cleanup with native startup/stop; never dispose a model under
        // an in-flight operation. Window-close callers suppress further callbacks.
        _disposed = true;
        _ = StopAsync();
    }
}
