# Configurable Shortcuts & Terminal Text Input - Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow users to configure hotkeys via recorder fields, and make text injection work in terminals (Warp, Windows Terminal, cmd).

**Architecture:** Two independent features sharing `MainWindow.xaml.cs` as orchestrator. Feature 1 adds HotkeyRecorder UI + settings persistence. Feature 2 adds foreground window detection and clipboard fallback to `KeyboardInjector`.

**Tech Stack:** WPF (.NET 8), Win32 interop (RegisterHotKey, SendInput, GetClassName, GetWindowThreadProcessId), NAudio

---

### Task 1: Extend Settings to Support Key-Value Pairs

**Files:**
- Modify: `MainWindow.xaml.cs:29-59` (SettingsPath, LoadSettings, SaveSettings)

**Step 1: Update LoadSettings to parse key=value lines**

Replace `LoadSettings()` and `SaveSettings()` with a format that keeps line 1 as the API key and reads additional `key=value` pairs. Add fields for the hotkey settings with defaults.

Add these fields to `MainWindow`:

```csharp
// Shortcut-Einstellungen (Defaults)
private Key _toggleKey = Key.F9;
private ModifierKeys _toggleModifiers = ModifierKeys.None;
private int _pttVKey = VK_RCONTROL;
```

Replace `LoadSettings`:

```csharp
private void LoadSettings()
{
    if (!File.Exists(SettingsPath)) return;

    var lines = File.ReadAllLines(SettingsPath);
    if (lines.Length > 0)
        ApiKeyBox.Password = lines[0].Trim();

    foreach (var line in lines.Skip(1))
    {
        var parts = line.Split('=', 2);
        if (parts.Length != 2) continue;
        var key = parts[0].Trim();
        var value = parts[1].Trim();

        switch (key)
        {
            case "toggle":
                ParseToggleShortcut(value);
                break;
            case "ptt":
                if (Enum.TryParse<Key>(value, out var pttKey))
                    _pttVKey = KeyInterop.VirtualKeyFromKey(pttKey);
                break;
        }
    }
}

private void ParseToggleShortcut(string value)
{
    _toggleModifiers = ModifierKeys.None;
    var parts = value.Split('+');
    foreach (var part in parts)
    {
        var trimmed = part.Trim();
        if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            _toggleModifiers |= ModifierKeys.Control;
        else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            _toggleModifiers |= ModifierKeys.Alt;
        else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            _toggleModifiers |= ModifierKeys.Shift;
        else if (Enum.TryParse<Key>(trimmed, out var k))
            _toggleKey = k;
    }
}
```

Replace `SaveSettings`:

```csharp
private void SaveSettings()
{
    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
    var lines = new List<string>
    {
        ApiKeyBox.Password.Trim(),
        $"toggle={FormatShortcut(_toggleModifiers, _toggleKey)}",
        $"ptt={KeyInterop.KeyFromVirtualKey(_pttVKey)}"
    };
    File.WriteAllLines(SettingsPath, lines);
}

private static string FormatShortcut(ModifierKeys mod, Key key)
{
    var parts = new List<string>();
    if (mod.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
    if (mod.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
    if (mod.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
    parts.Add(key.ToString());
    return string.Join("+", parts);
}
```

Add required using at top of file:

```csharp
using System.Windows.Input;
using System.Linq;
```

**Step 2: Build and verify no regressions**

Run: `dotnet build`
Expected: Build succeeds. App starts, loads existing settings.txt (API key on line 1 still works).

**Step 3: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: extend settings format for configurable shortcuts"
```

---

### Task 2: Add Hotkey Recorder TextBoxes to XAML

**Files:**
- Modify: `MainWindow.xaml:5,98-113,152-161`

**Step 1: Increase window height and add grid rows for shortcut fields**

Change window Height from 460 to 560:

```xml
Width="300" Height="560"
```

Add two new row pairs after the Modus row (Grid.Row="6"). Insert these after the existing `<RowDefinition Height="Auto"/>  <!-- Modus -->` row and its spacer:

```xml
            <RowDefinition Height="Auto"/>  <!-- Modus -->
            <RowDefinition Height="12"/>
            <RowDefinition Height="Auto"/>  <!-- Toggle Shortcut -->
            <RowDefinition Height="12"/>
            <RowDefinition Height="Auto"/>  <!-- PTT Shortcut -->
            <RowDefinition Height="12"/>
            <RowDefinition Height="Auto"/>  <!-- Hotkey Info -->
```

This shifts all subsequent Grid.Row indices by +4. Update:
- Hotkey Info: Grid.Row="8" -> Grid.Row="12"
- Transkript: Grid.Row="10" -> Grid.Row="14"
- Button: Grid.Row="12" -> Grid.Row="16"

**Step 2: Add the two TextBox fields**

Insert after the Modus StackPanel (after line 161):

```xml
        <!-- Toggle Shortcut -->
        <StackPanel Grid.Row="8">
            <TextBlock Text="TOGGLE SHORTCUT" Foreground="#BAC2DE"
                       FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
            <TextBox x:Name="ToggleShortcutBox"
                     Background="#313244" Foreground="#CDD6F4"
                     BorderBrush="#45475A" BorderThickness="1"
                     Padding="10,8" FontSize="13"
                     IsReadOnly="True" Cursor="Hand"
                     Text="F9"
                     GotFocus="ShortcutBox_GotFocus"
                     LostFocus="ShortcutBox_LostFocus"
                     PreviewKeyDown="ShortcutBox_PreviewKeyDown"/>
        </StackPanel>

        <!-- PTT Shortcut -->
        <StackPanel Grid.Row="10">
            <TextBlock Text="PTT SHORTCUT" Foreground="#BAC2DE"
                       FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
            <TextBox x:Name="PttShortcutBox"
                     Background="#313244" Foreground="#CDD6F4"
                     BorderBrush="#45475A" BorderThickness="1"
                     Padding="10,8" FontSize="13"
                     IsReadOnly="True" Cursor="Hand"
                     Text="Rechte Strg"
                     GotFocus="ShortcutBox_GotFocus"
                     LostFocus="ShortcutBox_LostFocus"
                     PreviewKeyDown="ShortcutBox_PreviewKeyDown"/>
        </StackPanel>
```

**Step 3: Build and verify layout**

Run: `dotnet build`
Expected: Build fails (event handlers not yet implemented). That is expected.

**Step 4: Commit XAML changes**

```bash
git add MainWindow.xaml
git commit -m "feat: add hotkey recorder TextBox fields to UI"
```

---

### Task 3: Implement Hotkey Recorder Logic

**Files:**
- Modify: `MainWindow.xaml.cs`

**Step 1: Add the event handlers for ShortcutBox**

Add these methods to `MainWindow.xaml.cs` after the `ModeCombo_SelectionChanged` method:

```csharp
private string? _shortcutBoxPreviousText;

private void ShortcutBox_GotFocus(object sender, RoutedEventArgs e)
{
    var box = (System.Windows.Controls.TextBox)sender;
    _shortcutBoxPreviousText = box.Text;
    box.Text = "Taste drücken…";
    box.Foreground = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)); // Yellow
}

private void ShortcutBox_LostFocus(object sender, RoutedEventArgs e)
{
    var box = (System.Windows.Controls.TextBox)sender;
    if (box.Text == "Taste drücken…")
        box.Text = _shortcutBoxPreviousText ?? "";
    box.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)); // CDD6F4
}

private void ShortcutBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
{
    e.Handled = true;
    var box = (System.Windows.Controls.TextBox)sender;

    // Ignore standalone modifier keys
    var key = e.Key == Key.System ? e.SystemKey : e.Key;
    if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        return;

    var modifiers = Keyboard.Modifiers;
    var displayText = FormatShortcut(modifiers, key);

    // Escape cancels
    if (key == Key.Escape)
    {
        box.Text = _shortcutBoxPreviousText ?? "";
        box.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
        Keyboard.ClearFocus();
        return;
    }

    bool isToggleBox = box == ToggleShortcutBox;

    // Check duplicate: compare with the other shortcut box
    var otherBox = isToggleBox ? PttShortcutBox : ToggleShortcutBox;
    if (otherBox.Text == displayText)
    {
        box.Text = "Bereits vergeben!";
        box.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)); // Red
        return;
    }

    if (isToggleBox)
    {
        _toggleKey = key;
        _toggleModifiers = modifiers;

        // Re-register if connected
        if (_connected)
        {
            _toggleHotkey?.Dispose();
            try
            {
                var mod = ToWin32Modifiers(modifiers);
                var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                _toggleHotkey = new GlobalHotkey(this, id: 1, mod, vk);
                _toggleHotkey.Pressed += OnToggleHotkey;
            }
            catch
            {
                box.Text = "Bereits belegt!";
                box.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                return;
            }
        }
    }
    else
    {
        _pttVKey = KeyInterop.VirtualKeyFromKey(key);

        // Re-register if connected
        if (_connected)
        {
            _pttHook?.Dispose();
            _pttHook = new LowLevelKeyboardHook(_pttVKey);
            _pttHook.KeyDown += OnPttKeyDown;
            _pttHook.KeyUp += OnPttKeyUp;
        }
    }

    box.Text = displayText;
    box.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
    SaveSettings();
    UpdateHotkeyInfoText();
    Keyboard.ClearFocus();
}

private static uint ToWin32Modifiers(ModifierKeys mod)
{
    uint result = 0;
    if (mod.HasFlag(ModifierKeys.Alt)) result |= GlobalHotkey.MOD_ALT;
    if (mod.HasFlag(ModifierKeys.Control)) result |= GlobalHotkey.MOD_CTRL;
    if (mod.HasFlag(ModifierKeys.Shift)) result |= GlobalHotkey.MOD_SHIFT;
    return result;
}

private void UpdateHotkeyInfoText()
{
    if (HotkeyInfoText is null) return;
    if (_isPttMode)
        HotkeyInfoText.Text = $"{PttShortcutBox.Text} gedrückt halten für Aufnahme";
    else
        HotkeyInfoText.Text = $"{ToggleShortcutBox.Text} drücken um Aufnahme zu starten/stoppen";
}
```

**Step 2: Update RegisterHotkeys to use dynamic values**

Replace the `RegisterHotkeys` method:

```csharp
private void RegisterHotkeys()
{
    try
    {
        var mod = ToWin32Modifiers(_toggleModifiers);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(_toggleKey);
        _toggleHotkey = new GlobalHotkey(this, id: 1, mod, vk);
        _toggleHotkey.Pressed += OnToggleHotkey;
    }
    catch (Exception ex)
    {
        AppendTranscript($"[Hotkey-Fehler: {ex.Message}]");
    }

    _pttHook = new LowLevelKeyboardHook(_pttVKey);
    _pttHook.KeyDown += OnPttKeyDown;
    _pttHook.KeyUp += OnPttKeyUp;
}
```

**Step 3: Update ModeCombo_SelectionChanged to use UpdateHotkeyInfoText**

Replace the body:

```csharp
private void ModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
{
    var item = (System.Windows.Controls.ComboBoxItem)ModeCombo.SelectedItem;
    _isPttMode = (string)item.Tag == "ptt";
    UpdateHotkeyInfoText();
}
```

**Step 4: Update LoadSettings to set TextBox text after loading**

At the end of `LoadSettings()`, after parsing, add:

```csharp
// Update UI after loading (controls may not be ready in constructor)
Loaded += (_, _) =>
{
    ToggleShortcutBox.Text = FormatShortcut(_toggleModifiers, _toggleKey);
    PttShortcutBox.Text = KeyInterop.KeyFromVirtualKey(_pttVKey).ToString();
};
```

**Step 5: Remove hardcoded VK_F9 constant and update VK_RCONTROL**

Remove `private const uint VK_F9 = 0x78;`. Keep `VK_RCONTROL` as the default value but it is no longer used directly by RegisterHotkeys (the field `_pttVKey` is used instead).

**Step 6: Build and test manually**

Run: `dotnet build && dotnet run`
Expected: App shows two shortcut fields with "F9" and "RightCtrl". Click a field, press a key, it updates. Settings persist across restarts.

**Step 7: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: implement hotkey recorder with validation and persistence"
```

---

### Task 4: Terminal Detection in KeyboardInjector

**Files:**
- Modify: `Services/KeyboardInjector.cs`

**Step 1: Add Win32 imports for window detection**

Add these to the `#region Win32` section:

```csharp
[DllImport("user32.dll")]
private static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

[DllImport("user32.dll")]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
```

**Step 2: Add terminal detection methods**

Add after the `BuildInputs` method:

```csharp
private static readonly HashSet<string> TerminalWindowClasses = new(StringComparer.OrdinalIgnoreCase)
{
    "ConsoleWindowClass",
    "CASCADIA_HOSTING_WINDOW_CLASS"
};

private static readonly HashSet<string> TerminalProcessNames = new(StringComparer.OrdinalIgnoreCase)
{
    "warp",
    "alacritty",
    "kitty",
    "wezterm-gui"
};

private static bool IsTerminalWindow()
{
    var hwnd = GetForegroundWindow();
    if (hwnd == IntPtr.Zero) return false;

    // Check window class name
    var className = new System.Text.StringBuilder(256);
    GetClassName(hwnd, className, 256);
    if (TerminalWindowClasses.Contains(className.ToString()))
        return true;

    // Check process name
    GetWindowThreadProcessId(hwnd, out uint pid);
    try
    {
        var process = System.Diagnostics.Process.GetProcessById((int)pid);
        var name = Path.GetFileNameWithoutExtension(process.ProcessName);
        return TerminalProcessNames.Contains(name);
    }
    catch
    {
        return false;
    }
}
```

**Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add Services/KeyboardInjector.cs
git commit -m "feat: add terminal window detection via class name and process name"
```

---

### Task 5: Clipboard Fallback for Terminals

**Files:**
- Modify: `Services/KeyboardInjector.cs`
- Modify: `MainWindow.xaml.cs:235-245`

**Step 1: Add clipboard paste method to KeyboardInjector**

Add these constants and method:

```csharp
private const ushort VK_CONTROL = 0x11;
private const ushort VK_V = 0x56;

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
```

**Step 2: Add TypeTextAsync method**

Add a new public async method and keep the old `TypeText` for backward compat:

```csharp
/// <summary>
/// Tippt Text ins aktive Fenster. Erkennt Terminals automatisch
/// und nutzt dort Clipboard-basiertes Einfuegen als Fallback.
/// Muss auf dem STA/UI-Thread aufgerufen werden (fuer Clipboard-Zugriff).
/// </summary>
public static async Task TypeTextAsync(string text)
{
    if (string.IsNullOrEmpty(text)) return;

    if (IsTerminalWindow())
    {
        await PasteViaClipboardAsync(text + " ");
    }
    else
    {
        var inputs = BuildInputs(text + " ");
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}

private static async Task PasteViaClipboardAsync(string text)
{
    // Clipboard-Inhalt sichern
    var previousData = System.Windows.Clipboard.GetDataObject();
    var hadText = previousData?.GetDataPresent(System.Windows.DataFormats.UnicodeText) == true;
    var previousText = hadText ? previousData?.GetData(System.Windows.DataFormats.UnicodeText) as string : null;

    try
    {
        System.Windows.Clipboard.SetText(text);
        var inputs = BuildCtrlV();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        // Warten bis Ziel-App den Paste verarbeitet hat
        await Task.Delay(100);
    }
    finally
    {
        // Clipboard wiederherstellen
        if (previousText != null)
            System.Windows.Clipboard.SetText(previousText);
        else
            System.Windows.Clipboard.Clear();
    }
}
```

**Step 3: Update OnTranscriptReceived in MainWindow.xaml.cs**

Replace the method:

```csharp
private async void OnTranscriptReceived(string text)
{
    await Dispatcher.InvokeAsync(async () =>
    {
        await KeyboardInjector.TypeTextAsync(text);
        AppendTranscript(text);
    });
}
```

**Step 4: Build and test manually**

Run: `dotnet build && dotnet run`
Expected: Build succeeds. Test by:
1. Open Notepad, dictate -- text appears via SendInput (character by character)
2. Open Warp/Windows Terminal, dictate -- text appears via Ctrl+V paste

**Step 5: Commit**

```bash
git add Services/KeyboardInjector.cs MainWindow.xaml.cs
git commit -m "feat: clipboard fallback for terminal text injection"
```

---

### Task 6: Final Cleanup and Integration Test

**Files:**
- All modified files

**Step 1: Full build**

Run: `dotnet build`
Expected: Clean build, no warnings.

**Step 2: Manual integration test checklist**

1. Start app, verify default shortcuts show "F9" and "RightCtrl"
2. Click Toggle Shortcut field, press Ctrl+F8 -- field updates
3. Click PTT Shortcut field, press same Ctrl+F8 -- shows "Bereits vergeben!"
4. Click PTT Shortcut field, press F10 -- field updates
5. Close and reopen app -- shortcuts persist
6. Connect to Deepgram, test toggle with new shortcut
7. Switch to PTT mode, test with new shortcut
8. Dictate into Notepad -- text typed character by character
9. Dictate into Warp -- text pasted via clipboard

**Step 3: Commit any final fixes**

```bash
git add -A
git commit -m "chore: final cleanup for shortcuts and terminal input"
```
