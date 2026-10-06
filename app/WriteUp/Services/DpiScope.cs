namespace WriteUp.Services;
// UIA, monitor bounds and CopyFromScreen must all use physical screen pixels.
internal sealed class DpiScope : IDisposable
{
    private readonly IntPtr _previous = NativeMethods.SetThreadDpiAwarenessContext(new IntPtr(-4));
    public void Dispose() { if (_previous != IntPtr.Zero) NativeMethods.SetThreadDpiAwarenessContext(_previous); }
}
