namespace WriteUp.Services;

internal static class AudioPeak
{
    // WASAPI shared-mode capture is normally IEEE float; PCM is also supported.
    public static int Level(byte[] buffer, int count, int bits, bool floating)
    {
        int bytes = bits / 8;
        if (bytes == 0 || count < bytes) return 0;
        double peak = 0;
        for (int i = 0; i + bytes <= Math.Min(count, buffer.Length); i += bytes)
        {
            double value = floating && bits == 32 ? BitConverter.ToSingle(buffer, i)
                : floating && bits == 64 ? BitConverter.ToDouble(buffer, i)
                : bits == 16 ? BitConverter.ToInt16(buffer, i) / 32768.0
                : bits == 32 ? BitConverter.ToInt32(buffer, i) / 2147483648.0
                : bits == 24 ? ((buffer[i] | buffer[i+1] << 8 | buffer[i+2] << 16) << 8 >> 8) / 8388608.0
                : bits == 8 ? (buffer[i] - 128) / 128.0 : 0;
            if (double.IsFinite(value)) peak = Math.Max(peak, Math.Abs(value));
        }
        return (int)Math.Round(Math.Clamp(peak * 100, 0, 100));
    }
}
