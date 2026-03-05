# Design: Configurable Shortcuts & Terminal Text Input

Date: 2026-03-05

## Feature 1: Configurable Shortcuts

### Problem

Hotkeys are hardcoded (F9 for toggle, Right Ctrl for PTT). Users cannot change them.

### Solution

Two HotkeyRecorder TextBox fields in the main window, one per mode. Click the field, press a key combination, it validates and stores the shortcut.

### UI

Below the mode ComboBox, two labeled readonly TextBox fields:

- **TOGGLE SHORTCUT** -- displays current toggle hotkey (default: F9)
- **PTT SHORTCUT** -- displays current PTT hotkey (default: Right Ctrl)

### Interaction Flow

1. User clicks the field -- text changes to "Taste drucken..."
2. `PreviewKeyDown` captures the key combination, suppresses default behavior
3. Validation runs:
   - Not empty (at least one key)
   - Not identical to the other shortcut
   - `RegisterHotKey` test for toggle (fails if system-wide conflict) -- show "Bereits belegt!" briefly
4. If valid: display and persist. If connected: unregister old hotkey, register new one.
5. If invalid: show error briefly, restore previous value.

### Data Model

Toggle hotkey: WPF `ModifierKeys` + `Key` (mapped to Win32 modifier flags + virtual key code for `RegisterHotKey`).

PTT hotkey: `int` virtual key code (used directly by `LowLevelKeyboardHook`).

### Settings Format

`settings.txt` (line-based, backward-compatible):

```
<api-key>
toggle=F9
ptt=RControlKey
```

Line 1 remains the API key. New lines as `key=value` pairs. Missing lines use defaults.

### Affected Files

- `MainWindow.xaml` -- two new TextBox fields + labels
- `MainWindow.xaml.cs` -- HotkeyRecorder logic (GotFocus, PreviewKeyDown, LostFocus), settings read/write, `RegisterHotkeys()` accepts dynamic keys
- `GlobalHotkey` -- no changes needed (already accepts dynamic modifier + vk)
- `LowLevelKeyboardHook` -- no changes needed (already accepts dynamic vk)

---

## Feature 2: Terminal-Compatible Text Input

### Problem

`KeyboardInjector.TypeText()` uses `SendInput` with `KEYEVENTF_UNICODE`. This works in GUI apps but fails in console windows (cmd, PowerShell, Windows Terminal, Warp).

### Solution

Automatic detection of console/terminal windows with clipboard-based fallback.

### Detection Strategy

Two-tier detection:

1. **Window class name** via `GetForegroundWindow()` + `GetClassName()`:
   - `ConsoleWindowClass` -- classic conhost (cmd, PowerShell)
   - `CASCADIA_HOSTING_WINDOW_CLASS` -- Windows Terminal

2. **Process name fallback** via `GetWindowThreadProcessId()` + process name lookup:
   - `warp.exe`, `alacritty.exe`, `kitty.exe`, `wezterm-gui.exe`
   - Stored in a `HashSet<string>` for easy extension

### Clipboard Fallback Flow

1. Save current clipboard content (`Clipboard.GetDataObject()`)
2. Set text to clipboard
3. Simulate `Ctrl+V` via `SendInput` (keydown Ctrl, keydown V, keyup V, keyup Ctrl)
4. `await Task.Delay(50)` for target app to process paste
5. Restore previous clipboard content

### Method Signature Change

`TypeText` becomes async: `Task TypeTextAsync(string text)` -- only actually async when clipboard fallback is used. The SendInput path remains synchronous.

### Threading

Called on the WPF Dispatcher thread (STA) which is required for clipboard access. The `Task.Delay` is awaited, so `OnTranscriptReceived` becomes async.

### Affected Files

- `KeyboardInjector.cs` -- window class detection, process name detection, clipboard fallback, `TypeTextAsync()` method
- `MainWindow.xaml.cs` -- `OnTranscriptReceived` updated to await `TypeTextAsync`
