namespace WriteUp.Services;

internal static class SpeechTiming
{
    public static DateTime PhraseTime(DateTime started, DateTime received, float offsetSeconds)
    {
        // Reject unavailable, corrupt or out-of-session offsets instead of
        // attaching notes to an unrelated future step.
        var elapsed = Math.Max(0, (received - started).TotalSeconds);
        if (!float.IsFinite(offsetSeconds) || offsetSeconds < 0 || offsetSeconds > elapsed + 1)
            return received;
        return started.AddSeconds(Math.Min(offsetSeconds, elapsed));
    }
}
