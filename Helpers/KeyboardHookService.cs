using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace VoiceDictation.Helpers;

/// <summary>
/// Unified low-level keyboard hook that handles toggle shortcut detection,
/// push-to-talk key tracking, and Win key suppression.
/// Replaces GlobalHotkey, LowLevelKeyboardHook, and WinKeyInterceptor.
/// </summary>
public class KeyboardHookService : IDisposable
{
    #region Win32

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi; // ensures correct union size on x64
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy, mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>Magic value placed in dwExtraInfo to identify our own simulated Win key taps.</summary>
    private static readonly IntPtr SIMULATED_WIN_EXTRA = (IntPtr)0x56444B48; // "VDKH"

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN    = 0x0100;
    private const int WM_KEYUP      = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP   = 0x0105;
    private const int VK_LWIN       = 0x5B;
    private const int VK_RWIN       = 0x5C;
    private const int VK_LCONTROL   = 0xA2;
    private const int VK_RCONTROL   = 0xA3;
    private const int VK_LMENU      = 0xA4;
    private const int VK_RMENU      = 0xA5;
    private const int VK_LSHIFT     = 0xA0;
    private const int VK_RSHIFT     = 0xA1;

    #endregion

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private bool _disposed;

    // ── Toggle shortcut config ──
    private int _toggleVKey;
    private ModifierKeys _toggleModifiers = ModifierKeys.None;
    private bool _toggleFired;

    // ── PTT config ──
    private int _pttVKey;
    private ModifierKeys _pttModifiers = ModifierKeys.None;
    private bool _pttDown;

    // ── State ──
    /// <summary>True while any Win key is physically held down.</summary>
    public bool IsWinDown { get; private set; }

    /// <summary>Set true while shortcut-recording TextBox is focused.
    /// Suppresses Win key and disables toggle/PTT event firing.</summary>
    public bool SuppressWinKey { get; set; }

    /// <summary>True when Win was consumed by one of our shortcuts (don't replay on release).</summary>
    private bool _winConsumed;

    // ── Events ──
    public event Action? TogglePressed;
    public event Action? PttKeyDown;
    public event Action? PttKeyUp;

    public KeyboardHookService()
    {
        _proc = HookCallback;
    }

    public void SetToggleShortcut(ModifierKeys modifiers, Key key)
    {
        _toggleModifiers = modifiers;
        _toggleVKey = KeyInterop.VirtualKeyFromKey(key);
        _toggleFired = false;
    }

    public void SetPttShortcut(ModifierKeys modifiers, Key key)
    {
        if (_pttDown)
        {
            _pttDown = false;
            PttKeyUp?.Invoke();
        }
        _pttModifiers = modifiers;
        _pttVKey = KeyInterop.VirtualKeyFromKey(key);
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        IsWinDown = false;
        _pttDown = false;
        _toggleFired = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp   = wParam == WM_KEYUP   || wParam == WM_SYSKEYUP;

            // ── Let our own simulated Win key through ──
            // KBDLLHOOKSTRUCT: vkCode(0) scanCode(4) flags(8) time(12) dwExtraInfo(16)
            IntPtr extraInfo = Marshal.ReadIntPtr(lParam, 16);
            if (extraInfo == SIMULATED_WIN_EXTRA && (vkCode is VK_LWIN or VK_RWIN))
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            bool winIsShortcutModifier = _toggleModifiers.HasFlag(ModifierKeys.Windows)
                                      || _pttModifiers.HasFlag(ModifierKeys.Windows);

            // ── Win key tracking & suppression ──
            if (vkCode is VK_LWIN or VK_RWIN)
            {
                if (isDown && !IsWinDown)
                {
                    IsWinDown = true;
                    _winConsumed = false;
                }
                else if (isUp)
                {
                    IsWinDown = false;
                    _toggleFired = false;

                    // If Win was suppressed but never used for a shortcut, replay it
                    if (!SuppressWinKey && winIsShortcutModifier && !_winConsumed)
                    {
                        SimulateWinKeyTap(vkCode);
                    }
                }

                if (SuppressWinKey || winIsShortcutModifier)
                    return (IntPtr)1;
            }

            // ── Skip shortcut detection while recording a new shortcut ──
            if (SuppressWinKey)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            // ── Toggle shortcut detection ──
            if (vkCode == _toggleVKey && _toggleVKey != 0 && isDown && !_toggleFired)
            {
                if (AreModifiersHeld(_toggleModifiers))
                {
                    _toggleFired = true;
                    if (_toggleModifiers.HasFlag(ModifierKeys.Windows))
                        _winConsumed = true;
                    TogglePressed?.Invoke();
                }
            }
            // Reset toggle-fired when the main key is released
            if (vkCode == _toggleVKey && isUp)
                _toggleFired = false;

            // ── PTT key detection ──
            if (vkCode == _pttVKey && _pttVKey != 0)
            {
                if (isDown && !_pttDown && AreModifiersHeld(_pttModifiers))
                {
                    _pttDown = true;
                    if (_pttModifiers.HasFlag(ModifierKeys.Windows))
                        _winConsumed = true;
                    PttKeyDown?.Invoke();
                }
                else if (isUp && _pttDown)
                {
                    _pttDown = false;
                    PttKeyUp?.Invoke();
                }
            }

            // ── Mark Win consumed for unrelated Win combos (Win+E, Win+D, etc.) ──
            if (IsWinDown && winIsShortcutModifier && isDown
                && vkCode is not VK_LWIN and not VK_RWIN
                && vkCode != _toggleVKey && vkCode != _pttVKey)
            {
                _winConsumed = true;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool AreModifiersHeld(ModifierKeys required)
    {
        if (required.HasFlag(ModifierKeys.Windows) && !IsWinDown)
            return false;
        if (required.HasFlag(ModifierKeys.Control)
            && (GetAsyncKeyState(VK_LCONTROL) & 0x8000) == 0
            && (GetAsyncKeyState(VK_RCONTROL) & 0x8000) == 0)
            return false;
        if (required.HasFlag(ModifierKeys.Alt)
            && (GetAsyncKeyState(VK_LMENU) & 0x8000) == 0
            && (GetAsyncKeyState(VK_RMENU) & 0x8000) == 0)
            return false;
        if (required.HasFlag(ModifierKeys.Shift)
            && (GetAsyncKeyState(VK_LSHIFT) & 0x8000) == 0
            && (GetAsyncKeyState(VK_RSHIFT) & 0x8000) == 0)
            return false;
        return true;
    }

    private void SimulateWinKeyTap(int vkCode)
    {
        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)vkCode, dwExtraInfo = SIMULATED_WIN_EXTRA } }
            },
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)vkCode, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = SIMULATED_WIN_EXTRA } }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
    }
}
