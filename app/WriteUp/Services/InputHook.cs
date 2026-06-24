using System.Runtime.InteropServices;
using System.Text;

namespace WriteUp.Services;

public enum ClickButton { Left, Right, Middle }

/// <summary>
/// Installs system-wide low-level mouse and keyboard hooks and surfaces clean,
/// high-level events. Hook callbacks run on the thread that called
/// <see cref="Start"/> (the WPF UI thread, which already pumps messages).
/// </summary>
public sealed class InputHook : IDisposable
{
    public event Action<int, int, ClickButton>? MouseClicked;
    public event Action<string>? TextTyped;     // a single character
    public event Action<string>? SpecialKey;    // "enter" | "tab" | "esc" | "backspace"

    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;

    // Keep delegate instances alive for the lifetime of the hooks.
    private NativeMethods.LowLevelProc? _mouseProc;
    private NativeMethods.LowLevelProc? _keyboardProc;

    private bool _shift;
    private bool _ctrl;
    private bool _alt;

    public bool IsRunning => _mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning) return;
        _mouseProc = MouseCallback;
        _keyboardProc = KeyboardCallback;

        IntPtr module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
    }

    public void Stop()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        _mouseProc = null;
        _keyboardProc = null;
        _shift = _ctrl = _alt = false;
    }

    public void Dispose() => Stop();

    // ---- mouse --------------------------------------------------------------
    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            int msg = (int)wParam;
            ClickButton? button = msg switch
            {
                NativeMethods.WM_LBUTTONDOWN => ClickButton.Left,
                NativeMethods.WM_RBUTTONDOWN => ClickButton.Right,
                NativeMethods.WM_MBUTTONDOWN => ClickButton.Middle,
                _ => null
            };
            if (button is { } b)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                try { MouseClicked?.Invoke(data.pt.x, data.pt.y, b); }
                catch { /* never let an exception escape into the hook chain */ }
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // ---- keyboard -----------------------------------------------------------
    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            int msg = (int)wParam;
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)kb.vkCode;

            bool down = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool up = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

            if (TrackModifier(vk, down, up))
            {
                // modifier key handled, nothing to emit
            }
            else if (down)
            {
                try { HandleKeyDown(vk, kb.scanCode); }
                catch { /* swallow */ }
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private bool TrackModifier(int vk, bool down, bool up)
    {
        switch (vk)
        {
            case NativeMethods.VK_SHIFT:
            case NativeMethods.VK_LSHIFT:
            case NativeMethods.VK_RSHIFT:
                if (down) _shift = true; else if (up) _shift = false;
                return true;
            case NativeMethods.VK_CONTROL:
            case NativeMethods.VK_LCONTROL:
            case NativeMethods.VK_RCONTROL:
                if (down) _ctrl = true; else if (up) _ctrl = false;
                return true;
            case NativeMethods.VK_MENU:
            case NativeMethods.VK_LMENU:
            case NativeMethods.VK_RMENU:
                if (down) _alt = true; else if (up) _alt = false;
                return true;
            default:
                return false;
        }
    }

    private void HandleKeyDown(int vk, uint scanCode)
    {
        switch (vk)
        {
            case NativeMethods.VK_RETURN: SpecialKey?.Invoke("enter"); return;
            case NativeMethods.VK_TAB: SpecialKey?.Invoke("tab"); return;
            case NativeMethods.VK_ESCAPE: SpecialKey?.Invoke("esc"); return;
            case NativeMethods.VK_BACK: SpecialKey?.Invoke("backspace"); return;
            case NativeMethods.VK_SPACE: TextTyped?.Invoke(" "); return;
        }

        // Don't treat keyboard shortcuts (Ctrl+C, Alt+F, ...) as typed text.
        if (_ctrl || _alt) return;

        string? ch = TranslateToChar((uint)vk, scanCode);
        if (!string.IsNullOrEmpty(ch))
            TextTyped?.Invoke(ch);
    }

    private string? TranslateToChar(uint vk, uint scanCode)
    {
        var keyState = new byte[256];
        if (_shift) keyState[NativeMethods.VK_SHIFT] = 0x80;
        bool caps = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
        if (caps) keyState[NativeMethods.VK_CAPITAL] = 0x01;

        var buf = new StringBuilder(8);
        IntPtr layout = NativeMethods.GetKeyboardLayout(0);
        int rc = NativeMethods.ToUnicodeEx(vk, scanCode, keyState, buf, buf.Capacity, 0, layout);
        if (rc <= 0) return null; // 0 = no translation, -1 = dead key
        string s = buf.ToString();
        return s.Length == 0 || char.IsControl(s[0]) ? null : s;
    }
}
