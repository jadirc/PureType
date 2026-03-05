using System.Runtime.InteropServices;

namespace VoiceDictation.Services;

/// <summary>
/// Injiziert Text als simulierte Tastatureingaben ins aktive Fenster
/// via Windows SendInput API (Unicode-Unterstützung).
/// </summary>
public static class KeyboardInjector
{
    #region Win32

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        // Padding für Mouse/Hardware Input (gleiche Größe)
        [FieldOffset(0)] public MOUSEINPUT mi;
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
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP  = 0x0002;

    #endregion

    /// <summary>
    /// Tippt den übergebenen Text zeichenweise in das aktuell fokussierte Fenster.
    /// Unterstützt Unicode (Umlaute, Sonderzeichen etc.).
    /// </summary>
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Leerzeichen vor dem Text, damit das Wort vom Vorherigen getrennt wird
        // (Deepgram liefert Wörter ohne führendes Leerzeichen beim ersten Wort)
        var inputs = BuildInputs(text + " ");
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT[] BuildInputs(string text)
    {
        var list = new List<INPUT>(text.Length * 2);

        foreach (char c in text)
        {
            // Key Down
            list.Add(new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });

            // Key Up
            list.Add(new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        return list.ToArray();
    }
}
