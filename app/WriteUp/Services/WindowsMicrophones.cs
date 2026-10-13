using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WriteUp.Services;

public static class WindowsMicrophones
{
    public record Input(string Id, string Name) { public override string ToString() => Name; }
    public static List<Input> List()
    {
        using var devices = new MMDeviceEnumerator();
        var result = new List<Input>();
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        { using (device) result.Add(new Input(device.ID, device.FriendlyName)); }
        return result;
    }
    public static Input? Default(Role role)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, role);
            return new Input(device.ID, device.FriendlyName);
        }
        catch { return null; }
    }
    public static Input RequireSelected(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Choose a microphone in WriteUp Settings before starting narration.");
        return List().FirstOrDefault(d => d.Id == id)
            ?? throw new InvalidOperationException("Your selected microphone is disconnected or disabled. Select an available microphone in Settings.");
    }
}

/// <summary>Direct test of the chosen endpoint, independent of speech services.</summary>
internal sealed class MicrophoneLevelTest : IDisposable
{
    private MMDevice? _device;
    private WasapiCapture? _capture;
    public event Action<int>? Level;
    public event Action<string>? Failed;
    public void Start(string id)
    {
        using var devices = new MMDeviceEnumerator();
        _device = devices.GetDevice(id);
        _capture = new WasapiCapture(_device);
        var format = _capture.WaveFormat;
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extended && extended.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        _capture.DataAvailable += (_, e) =>
            Level?.Invoke(AudioPeak.Level(e.Buffer, e.BytesRecorded, format.BitsPerSample, floating));
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null) Failed?.Invoke(e.Exception.Message);
        };
        _capture.StartRecording();
    }
    public void Dispose()
    {
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose(); _capture = null;
        _device?.Dispose(); _device = null;
    }
}
