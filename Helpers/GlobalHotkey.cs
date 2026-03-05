using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoiceDictation.Helpers;

/// <summary>
/// Registriert einen systemweiten Hotkey und feuert ein Event bei Betätigung.
/// Modifier-Konstanten: MOD_ALT=0x0001, MOD_CTRL=0x0002, MOD_SHIFT=0x0004, MOD_WIN=0x0008
/// </summary>
public class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private readonly IntPtr _handle;
    private readonly int _id;
    private HwndSource? _hwndSource;
    private bool _disposed;

    public event Action? Pressed;

    // Häufige Modifier-Kombinationen
    public const uint MOD_NONE  = 0x0000;
    public const uint MOD_ALT   = 0x0001;
    public const uint MOD_CTRL  = 0x0002;
    public const uint MOD_SHIFT = 0x0004;

    public GlobalHotkey(Window window, int id, uint modifiers, uint vKey)
    {
        _id = id;
        _handle = new WindowInteropHelper(window).EnsureHandle();

        bool success = RegisterHotKey(_handle, id, modifiers, vKey);
        if (!success)
            throw new InvalidOperationException($"Hotkey-Registrierung fehlgeschlagen (ID={id}). Möglicherweise bereits belegt.");

        _hwndSource = HwndSource.FromHwnd(_handle);
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hwndSource?.RemoveHook(WndProc);
        UnregisterHotKey(_handle, _id);
    }
}
