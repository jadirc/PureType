using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace PureType.Services;

/// <summary>
/// Injects text as simulated keyboard input into the active window
/// via the Windows SendInput API (Unicode support).
/// </summary>
public static class KeyboardInjector
{
    private static readonly SemaphoreSlim ClipboardLock = new(1, 1);

    /// <summary>
    /// Delay in milliseconds between each character when typing via SendInput.
    /// 0 = send all at once (default). Useful for apps that drop fast input.
    /// </summary>
    public static int InputDelayMs { get; set; }

    #region Win32

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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
        // Padding for Mouse/Hardware Input (same size)
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
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;

    #endregion

    /// <summary>
    /// Types the given text character by character into the currently focused window.
    /// Supports Unicode (umlauts, special characters, etc.).
    /// </summary>
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var suffix = text.EndsWith('\n') ? "" : " ";
        var inputs = BuildInputs(text + suffix);
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
            Log.Warning("SendInput returned 0 — text injection failed (no focused window or elevated target?)");
        else if (sent < inputs.Length)
            Log.Warning("SendInput sent {Sent}/{Total} events — partial injection", sent, inputs.Length);
    }

    public static async Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var isTerminal = IsTerminalWindow();
        Log.Debug("TypeTextAsync: isTerminal={IsTerminal}, text={Text}", isTerminal, text);

        var suffix = text.EndsWith('\n') ? "" : " ";

        if (isTerminal)
        {
            await PasteViaClipboardAsync(text + suffix);
        }
        else if (InputDelayMs > 0)
        {
            foreach (char c in text + suffix)
            {
                var inputs = BuildInputs(c.ToString());
                uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                if (sent == 0)
                    Log.Warning("SendInput returned 0 for char '{Char}' — target window may be elevated", c);
                await Task.Delay(InputDelayMs);
            }
        }
        else
        {
            var inputs = BuildInputs(text + suffix);
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent == 0)
                Log.Warning("SendInput returned 0 — text injection failed (no focused window or elevated target?)");
            else if (sent < inputs.Length)
                Log.Warning("SendInput sent {Sent}/{Total} events — partial injection", sent, inputs.Length);
        }
    }

    public static async Task PasteTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Log.Debug("PasteTextAsync: text={Text}", text);

        var suffix = text.EndsWith('\n') ? "" : " ";
        await PasteViaClipboardAsync(text + suffix);
    }

    private static async Task PasteViaClipboardAsync(string text)
    {
        await ClipboardLock.WaitAsync();
        try
        {
            // Clipboard can be locked by other apps — retry with backoff
            bool clipboardSet = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    clipboardSet = true;
                    break;
                }
                catch (COMException) when (attempt < 4)
                {
                    await Task.Delay(50 * (attempt + 1)); // 50, 100, 150, 200ms
                }
            }

            if (clipboardSet)
            {
                var inputs = BuildCtrlV();
                uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                if (sent == 0)
                    Log.Warning("Ctrl+V SendInput returned 0 — paste failed (no focused window or elevated target?)");

                // Wait for target app to process paste
                await Task.Delay(200);
            }
            else
            {
                // Clipboard unavailable — fall back to SendInput (slower but reliable)
                Log.Warning("Clipboard locked, falling back to SendInput for {Length} chars", text.Length);
                var inputs = BuildInputs(text);
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            }
        }
        catch (Exception ex)
        {
            // Last-resort fallback: try SendInput so text is never lost
            Log.Warning(ex, "Clipboard paste failed, falling back to SendInput");
            try
            {
                var inputs = BuildInputs(text);
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            }
            catch (Exception fallbackEx)
            {
                Log.Error(fallbackEx, "SendInput fallback also failed");
            }
        }
        finally
        {
            ClipboardLock.Release();
        }
    }

    private static INPUT[] BuildCtrlV()
    {
        return new[]
        {
            new INPUT { Type = INPUT_KEYBOARD, Data = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL } } },
            new INPUT { Type = INPUT_KEYBOARD, Data = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_V } } },
            new INPUT { Type = INPUT_KEYBOARD, Data = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { Type = INPUT_KEYBOARD, Data = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
        };
    }

    private static INPUT[] BuildInputs(string text)
    {
        var list = new List<INPUT>(text.Length * 2);

        foreach (char c in text)
        {
            // Skip \r (handle \n as Enter, avoid double-Enter from \r\n)
            if (c == '\r') continue;

            if (c == '\n')
            {
                // Emit VK_RETURN key down + key up
                list.Add(new INPUT
                {
                    Type = INPUT_KEYBOARD,
                    Data = new INPUTUNION
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = VK_RETURN,
                            wScan = 0,
                            dwFlags = 0,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                });
                list.Add(new INPUT
                {
                    Type = INPUT_KEYBOARD,
                    Data = new INPUTUNION
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = VK_RETURN,
                            wScan = 0,
                            dwFlags = KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                });
                continue;
            }

            // Key Down (Unicode)
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

            // Key Up (Unicode)
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

    private static readonly HashSet<string> TerminalWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass",
        "CASCADIA_HOSTING_WINDOW_CLASS"
    };

    private static readonly HashSet<string> TerminalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal",
        "warp",
        "alacritty",
        "kitty",
        "wezterm-gui",
        "cmd",
        "powershell",
        "pwsh"
    };

    private static bool IsTerminalWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var className = new System.Text.StringBuilder(256);
        GetClassName(hwnd, className, 256);
        var cls = className.ToString();

        if (TerminalWindowClasses.Contains(cls))
        {
            Log.Debug("Terminal detected via WindowClass: {ClassName}", cls);
            return true;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = Path.GetFileNameWithoutExtension(process.ProcessName);

            if (TerminalProcessNames.Contains(name))
            {
                Log.Debug("Terminal detected via process: {ProcessName} (Class: {ClassName})", name, cls);
                return true;
            }

            Log.Debug("Not a terminal: Class={ClassName}, Process={ProcessName}", cls, name);
            return false;
        }
        catch
        {
            return false;
        }
    }
}
