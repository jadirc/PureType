using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace PureType.Helpers;

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

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN    = 0x0100;
    private const int WM_KEYUP      = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP   = 0x0105;
    private const int VK_LWIN       = 0x5B;
    private const int VK_RWIN       = 0x5C;
    internal const int VK_LCONTROL  = 0xA2;
    internal const int VK_RCONTROL  = 0xA3;
    internal const int VK_LMENU     = 0xA4;
    internal const int VK_RMENU     = 0xA5;
    internal const int VK_LSHIFT    = 0xA0;
    internal const int VK_RSHIFT    = 0xA1;
    internal const int VK_CAPITAL   = 0x14;

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

    // ── Mute shortcut config ──
    private int _muteVKey;
    private ModifierKeys _muteModifiers = ModifierKeys.None;
    private bool _muteFired;

    // ── Language switch shortcut config ──
    private int _langSwitchVKey;
    private ModifierKeys _langSwitchModifiers = ModifierKeys.None;
    private bool _langSwitchFired;

    // ── State ──
    /// <summary>True while any Win key is physically held down.</summary>
    public bool IsWinDown { get; private set; }

    /// <summary>Set true while shortcut-recording TextBox is focused.
    /// Suppresses Win key and disables toggle/PTT event firing.</summary>
    public bool SuppressWinKey { get; set; }

    // ── Prompt keys ──
    private HashSet<int> _promptVKeys = new();
    private bool _promptKeyDetectionEnabled;

    // ── Events ──
    /// <summary>Fired when the toggle shortcut is pressed.</summary>
    public event Action? TogglePressed;
    /// <summary>Fired when the PTT key goes down.</summary>
    public event Action? PttKeyDown;
    /// <summary>Fired when a registered prompt key is pressed during recording.
    /// Parameter is the virtual key code. The key is suppressed (not forwarded to the focused window).</summary>
    public event Action<int>? PromptKeyPressed;
    public event Action? PttKeyUp;
    /// <summary>Fired when the mute shortcut is pressed.</summary>
    public event Action? MutePressed;
    /// <summary>Fired when the language switch shortcut is pressed.</summary>
    public event Action? LanguageSwitchPressed;

    /// <summary>Fired during shortcut recording when Win is pressed while a modifier key is already held.
    /// The parameter is the VK code of the held modifier key.</summary>
    public event Action<int>? RecordingWinPlusModifier;

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

    /// <summary>Registers the set of virtual key codes that trigger named prompts.</summary>
    public void SetPromptKeys(HashSet<int> vKeys)
    {
        _promptVKeys = vKeys;
    }

    /// <summary>Enable/disable prompt key detection (only active during recording).</summary>
    public void SetPromptKeyDetection(bool enabled)
    {
        _promptKeyDetectionEnabled = enabled;
    }

    public void SetMuteShortcut(ModifierKeys modifiers, Key key)
    {
        _muteModifiers = modifiers;
        _muteVKey = KeyInterop.VirtualKeyFromKey(key);
        _muteFired = false;
    }

    public void SetLanguageSwitchShortcut(ModifierKeys modifiers, Key key)
    {
        _langSwitchModifiers = modifiers;
        _langSwitchVKey = KeyInterop.VirtualKeyFromKey(key);
        _langSwitchFired = false;
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

            // ── Win key tracking ──
            if (vkCode is VK_LWIN or VK_RWIN)
            {
                if (isDown) IsWinDown = true;
                else if (isUp) { IsWinDown = false; _toggleFired = false; }

                // Only suppress during shortcut recording
                if (SuppressWinKey)
                {
                    // When Win is pressed while a modifier key is already held,
                    // notify so shortcut recording works regardless of key press order.
                    if (isDown)
                    {
                        int heldVk = 0;
                        if ((GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0) heldVk = VK_LCONTROL;
                        else if ((GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0) heldVk = VK_RCONTROL;
                        else if ((GetAsyncKeyState(VK_LMENU) & 0x8000) != 0) heldVk = VK_LMENU;
                        else if ((GetAsyncKeyState(VK_RMENU) & 0x8000) != 0) heldVk = VK_RMENU;
                        else if ((GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0) heldVk = VK_LSHIFT;
                        else if ((GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0) heldVk = VK_RSHIFT;

                        if (heldVk != 0)
                            RecordingWinPlusModifier?.Invoke(heldVk);
                    }
                    return (IntPtr)1;
                }
            }

            // ── Skip shortcut detection while recording a new shortcut ──
            if (SuppressWinKey)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            // ── Prompt key detection (only during recording) ──
            if (_promptKeyDetectionEnabled && isDown && _promptVKeys.Contains(vkCode))
            {
                PromptKeyPressed?.Invoke(vkCode);
                return (IntPtr)1; // suppress key
            }

            // ── Toggle shortcut detection ──
            if (vkCode == _toggleVKey && _toggleVKey != 0 && isDown && !_toggleFired)
            {
                if (AreModifiersHeld(_toggleModifiers))
                {
                    _toggleFired = true;
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
                    PttKeyDown?.Invoke();
                }
                else if (isUp && _pttDown)
                {
                    _pttDown = false;
                    PttKeyUp?.Invoke();
                }
            }

            // ── Mute shortcut detection ──
            if (vkCode == _muteVKey && _muteVKey != 0 && isDown && !_muteFired)
            {
                if (AreModifiersHeld(_muteModifiers))
                {
                    _muteFired = true;
                    MutePressed?.Invoke();
                }
            }
            if (vkCode == _muteVKey && isUp)
                _muteFired = false;

            // ── Language switch shortcut detection ──
            if (vkCode == _langSwitchVKey && _langSwitchVKey != 0 && isDown && !_langSwitchFired)
            {
                if (AreModifiersHeld(_langSwitchModifiers))
                {
                    _langSwitchFired = true;
                    LanguageSwitchPressed?.Invoke();
                }
            }
            if (vkCode == _langSwitchVKey && isUp)
                _langSwitchFired = false;

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
    }
}
