using System.Collections.Concurrent;
using System.IO;

namespace WriteUp.Services;

/// <summary>Bounded PCM producer/consumer stream. Reads block until audio or EOF;
/// unlike a silence-filling buffer this cannot run recognition ahead of real time.</summary>
public sealed class MicrophoneStream : Stream
{
    private readonly BlockingCollection<byte[]> _chunks = new(100);
    private byte[]? _current;
    private int _offset;
    private long _position;
    public bool WriteAudio(byte[] source, int count)
    {
        if (count == 0) return true;
        var copy = new byte[count];
        Buffer.BlockCopy(source, 0, copy, 0, count);
        try { return _chunks.TryAdd(copy); }
        catch (InvalidOperationException) { return false; }
    }
    public void Complete() { if (!_chunks.IsAddingCompleted) _chunks.CompleteAdding(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
        if (count == 0) return 0;
        int total = 0;
        while (total < count)
        {
            if (_current == null || _offset == _current.Length)
            {
                try { _current = _chunks.Take(); _offset = 0; }
                catch (InvalidOperationException) { break; }
            }
            int copied = Math.Min(count - total, _current.Length - _offset);
            Buffer.BlockCopy(_current, _offset, buffer, offset + total, copied);
            _offset += copied; total += copied; _position += copied;
        }
        return total;
    }

    protected override void Dispose(bool disposing) { if (disposing) Complete(); base.Dispose(disposing); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    // System.Speech's SpStreamWrapper reads Length and Position even for a
    // forward-only input. Unknown/live length must not look like immediate EOF.
    public override long Length => long.MaxValue;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin)
    {
        if ((origin == SeekOrigin.Current && offset == 0) || (origin == SeekOrigin.Begin && offset == _position)) return _position;
        throw new NotSupportedException("Live microphone audio cannot be rewound.");
    }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
