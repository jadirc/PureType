# MainWindow Refactoring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extract TrayIconManager and RecordingController from MainWindow.xaml.cs (~860 lines) to reduce it to ~350 lines while improving testability.

**Architecture:** Two new classes communicate with MainWindow via events (same pattern as DeepgramService/AudioCaptureService). TrayIconManager owns the NotifyIcon and context menu. RecordingController owns recording lifecycle, audio routing, VAD, and transcript handling. MainWindow remains the orchestrator for connection, settings, and UI updates.

**Tech Stack:** .NET 8, WPF, WinForms (NotifyIcon), System.Drawing (icon rendering)

---

### Task 1: Create TrayIconManager

**Files:**
- Create: `src/VoiceDictation/Helpers/TrayIconManager.cs`

**Step 1: Create TrayIconManager with full implementation**

Extract these sections from MainWindow.xaml.cs into the new class:
- Fields: `_trayIcon`, `_trayStatusLabel`, `_trayConnectItem`, `_trayMuteItem`, `_baseTrayIcon` (lines 19-23)
- `SetupTrayIcon()` logic (lines 626-693) → becomes constructor body
- `UpdateTrayMenu()` (lines 714-749) → becomes `Update(bool connected, bool recording, bool muted)`
- `CreateStatusIcon()` (lines 754-784) → private static method
- `DestroyIcon` P/Invoke (lines 751-752) → moves with CreateStatusIcon

The menu click handlers fire events instead of calling MainWindow methods directly:
- "Connect/Disconnect" → `ConnectRequested` / `DisconnectRequested` (check `_connected` state passed via `Update`)
- "Mute" → `MuteToggleRequested`
- "Settings" → `SettingsRequested`
- "Open" → `ShowRequested`
- "Exit" → `ExitRequested`
- Left-click on icon → `ShowRequested`

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceDictation.Helpers;

internal class TrayIconManager : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.ToolStripLabel _statusLabel;
    private readonly System.Windows.Forms.ToolStripMenuItem _connectItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _muteItem;
    private readonly Icon _baseIcon;
    private bool _connected;

    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? MuteToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public TrayIconManager(Icon baseIcon)
    {
        _baseIcon = baseIcon;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _baseIcon,
            Text = "Voice Dictation",
            Visible = true
        };

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                ShowRequested?.Invoke();
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();

        _statusLabel = new System.Windows.Forms.ToolStripLabel("Not connected")
        {
            ForeColor = Color.FromArgb(0xF3, 0x8B, 0xA8)
        };
        menu.Items.Add(_statusLabel);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _connectItem = new System.Windows.Forms.ToolStripMenuItem("Connect", null, (_, _) =>
        {
            if (_connected)
                DisconnectRequested?.Invoke();
            else
                ConnectRequested?.Invoke();
        });
        menu.Items.Add(_connectItem);

        _muteItem = new System.Windows.Forms.ToolStripMenuItem("Mute", null, (_, _) =>
        {
            MuteToggleRequested?.Invoke();
        })
        {
            CheckOnClick = false
        };
        menu.Items.Add(_muteItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("Open", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _trayIcon.ContextMenuStrip = menu;
    }

    public void Update(bool connected, bool recording, bool muted)
    {
        _connected = connected;
        _muteItem.Checked = muted;

        if (muted)
        {
            _statusLabel.Text = "Muted";
            _statusLabel.ForeColor = Color.FromArgb(0xF9, 0xE2, 0xAF);
        }
        else if (recording)
        {
            _statusLabel.Text = "Recording";
            _statusLabel.ForeColor = Color.FromArgb(0xF3, 0x8B, 0xA8);
        }
        else if (connected)
        {
            _statusLabel.Text = "Connected";
            _statusLabel.ForeColor = Color.FromArgb(0x40, 0xA0, 0x2B);
        }
        else
        {
            _statusLabel.Text = "Not connected";
            _statusLabel.ForeColor = Color.FromArgb(0xF3, 0x8B, 0xA8);
        }

        _connectItem.Text = connected ? "Disconnect" : "Connect";

        var dotColor = connected
            ? Color.FromArgb(0x40, 0xA0, 0x2B)
            : Color.FromArgb(0xE6, 0x40, 0x53);
        _trayIcon.Icon = CreateStatusIcon(_baseIcon, dotColor, !connected);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon CreateStatusIcon(Icon baseIcon, Color color, bool showCross)
    {
        using var bmp = baseIcon.ToBitmap();
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int dotSize = 10;
        int x = bmp.Width - dotSize;
        int y = bmp.Height - dotSize;

        using var outlineBrush = new SolidBrush(Color.White);
        g.FillEllipse(outlineBrush, x - 1, y - 1, dotSize + 2, dotSize + 2);

        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, x, y, dotSize, dotSize);

        if (showCross)
        {
            using var pen = new Pen(Color.White, 2f);
            g.DrawLine(pen, x + 1, y + dotSize - 1, x + dotSize - 1, y + 1);
        }

        var handle = bmp.GetHicon();
        var icon = Icon.FromHandle(handle);
        var result = (Icon)icon.Clone();
        DestroyIcon(handle);
        return result;
    }

    public void Dispose()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build`
Expected: Build succeeded (new file compiles, MainWindow not yet changed)

**Step 3: Commit**

```
refactor: extract TrayIconManager from MainWindow
```

---

### Task 2: Wire TrayIconManager into MainWindow

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

**Step 1: Replace tray fields and methods in MainWindow**

Remove from MainWindow:
- Fields: `_trayIcon`, `_trayStatusLabel`, `_trayConnectItem`, `_trayMuteItem`, `_baseTrayIcon` (lines 19-23)
- `SetupTrayIcon()` method (lines 626-693)
- `UpdateTrayMenu()` method (lines 714-749)
- `CreateStatusIcon()` method (lines 754-784)
- `DestroyIcon` P/Invoke (lines 751-752)

Add field:
```csharp
private readonly TrayIconManager _tray;
```

In constructor, replace `SetupTrayIcon()` call with:
```csharp
var iconStream = Application.GetResourceStream(
    new Uri("pack://application:,,,/Resources/mic.ico"))?.Stream;
var baseIcon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Application;

_tray = new TrayIconManager(baseIcon);
_tray.ConnectRequested += () => Dispatcher.InvokeAsync(async () => await ConnectAsync());
_tray.DisconnectRequested += () => Dispatcher.InvokeAsync(async () => await DisconnectAsync());
_tray.MuteToggleRequested += () => Dispatcher.Invoke(ToggleMute);
_tray.SettingsRequested += () => Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()));
_tray.ShowRequested += () => Dispatcher.Invoke(ShowFromTray);
_tray.ExitRequested += () =>
{
    _tray.Dispose();
    _keyboardHook.Dispose();
    Application.Current.Shutdown();
};
```

Replace all `UpdateTrayMenu()` calls with:
```csharp
_tray.Update(_connected, _recording, _muted);
```

Note: `_recording` is currently a MainWindow field. After Task 4 it will come from RecordingController. For now, keep using `_recording`.

In `ToggleMute()`, replace `_trayMuteItem.Checked = _muted` with nothing (TrayIconManager handles it in `Update`).

In `Window_Closing()`, replace `_trayIcon?.Dispose()` with `_tray.Dispose()`.

**Step 2: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: Build succeeded, all 22 tests pass

**Step 3: Commit**

```
refactor: wire TrayIconManager into MainWindow
```

---

### Task 3: Create RecordingController

**Files:**
- Create: `src/VoiceDictation/Services/RecordingController.cs`

**Step 1: Create RecordingController with full implementation**

Extract these sections from MainWindow.xaml.cs:
- State fields: `_recording`, `_recordingSource`, `RecordingSource` enum, `_vad`, `_sessionChunks`, `_aiPostProcessRequested`, `_interimText`
- `StartRecording()` logic (lines 484-508)
- `StopRecording()` logic (lines 510-542)
- `OnToggleHotkey()` logic (lines 436-455)
- `OnPttKeyDown()` / `OnPttKeyUp()` logic (lines 459-480)
- `OnAudioData()` (lines 546-552)
- `OnTranscriptReceived()` (lines 556-588)
- `AppendTranscript()` text truncation logic (lines 590-603)

Key differences from MainWindow version:
- No direct UI access — fire events instead of setting TextBlock.Text
- No `Dispatcher` calls — caller (MainWindow) handles marshalling
- `StartRecording`/`StopRecording` fire `StatusChanged` event instead of calling `SetStatus`
- `StopRecording` fires `LlmProcessingRequested` instead of calling `ProcessWithLlmAsync`
- `OnTranscriptReceived` fires `TranscriptUpdated`/`InterimTextUpdated` events
- Audio level forwarding: subscribe to `_audio.AudioLevelChanged`, re-fire via `AudioLevelChanged` event

```csharp
using System.Windows.Media;
using Serilog;
using VoiceDictation.Helpers;

namespace VoiceDictation.Services;

internal class RecordingController
{
    private readonly AudioCaptureService _audio;
    private readonly KeyboardHookService _keyboardHook;
    private readonly ReplacementService _replacements;

    private ITranscriptionProvider? _provider;
    private bool _recording;
    private enum RecordingSource { None, Toggle, Ptt }
    private RecordingSource _recordingSource = RecordingSource.None;
    private VadService? _vad;
    private readonly List<string> _sessionChunks = new();
    private bool _aiPostProcessRequested;
    private string _transcript = "";

    private bool _connected;
    private bool _vadEnabled;
    private bool _llmEnabled;

    public bool IsRecording => _recording;
    public bool IsMuted { get; set; }

    // Events
    public event Action<string, Color>? StatusChanged;
    public event Action<string, Color, bool>? ToastRequested; // (message, color, autoClose)
    public event Action<string>? TranscriptUpdated;
    public event Action<string>? InterimTextUpdated;
    public event Action? RecordingStateChanged;
    public event Action<double>? AudioLevelChanged;
    public event Action<string>? LlmProcessingRequested;
    public event Action? RecordingStopped; // signals VU meter reset etc.

    private static readonly Color Red = Color.FromRgb(0xF3, 0x8B, 0xA8);
    private static readonly Color Green = Color.FromRgb(0xA6, 0xE3, 0xA1);

    public RecordingController(AudioCaptureService audio, KeyboardHookService keyboardHook, ReplacementService replacements)
    {
        _audio = audio;
        _keyboardHook = keyboardHook;
        _replacements = replacements;

        _audio.AudioDataAvailable += OnAudioData;
        _audio.AudioLevelChanged += level => AudioLevelChanged?.Invoke(level);
    }

    public void Configure(AppSettings settings)
    {
        _vadEnabled = settings.Audio.Vad;
        _llmEnabled = settings.Llm.Enabled;
    }

    public void SetProvider(ITranscriptionProvider? provider, bool connected)
    {
        if (_provider != null)
            _provider.TranscriptReceived -= OnTranscriptReceived;

        _provider = provider;
        _connected = connected;

        if (_provider != null)
            _provider.TranscriptReceived += OnTranscriptReceived;
    }

    public void HandleToggle(bool aiKeyHeld)
    {
        if (_recording)
        {
            if (_recordingSource != RecordingSource.Toggle) return;
            if (!_aiPostProcessRequested && _llmEnabled)
                _aiPostProcessRequested = aiKeyHeld;
            _recordingSource = RecordingSource.None;
            StopRecording();
        }
        else
        {
            _aiPostProcessRequested = aiKeyHeld && _llmEnabled;
            _recordingSource = RecordingSource.Toggle;
            StartRecording();
        }
    }

    public void HandlePttDown(bool aiKeyHeld)
    {
        if (!_connected || _recording) return;
        _aiPostProcessRequested = aiKeyHeld && _llmEnabled;
        _recordingSource = RecordingSource.Ptt;
        StartRecording();
    }

    public void HandlePttUp()
    {
        if (_recordingSource != RecordingSource.Ptt) return;
        if (!_aiPostProcessRequested && _llmEnabled)
            _aiPostProcessRequested = _keyboardHook.IsAiKeyHeld();
        _recordingSource = RecordingSource.None;
        StopRecording();
    }

    private void StartRecording()
    {
        if (_recording || !_connected) return;
        _sessionChunks.Clear();
        _recording = true;
        SoundFeedback.PlayStart();
        _audio.Start();
        var aiLabel = _aiPostProcessRequested ? " + AI" : "";
        StatusChanged?.Invoke($"\u25CF Recording{aiLabel}", Red);
        ToastRequested?.Invoke(
            _aiPostProcessRequested ? "Recording + AI" : "Recording",
            Red, false);

        // Separator between sessions
        if (!string.IsNullOrEmpty(_transcript) && _transcript != "Transcript will appear here \u2026")
            _transcript += "\n\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\n";

        TranscriptUpdated?.Invoke(_transcript);

        if (_vadEnabled && _recordingSource == RecordingSource.Toggle)
        {
            _vad = new VadService();
            _vad.SilenceDetected += () => StopRecording();
            _vad.Reset();
        }
        RecordingStateChanged?.Invoke();
    }

    public async void StopRecording()
    {
        if (!_recording) return;
        _audio.Stop();
        _recording = false;
        _vad = null;

        InterimTextUpdated?.Invoke("");
        RecordingStopped?.Invoke();

        if (_provider is not null)
            await _provider.SendFinalizeAsync();

        // Small yield to let pending TranscriptReceived callbacks run
        await Task.Delay(50);

        SoundFeedback.PlayStop();
        if (!_aiPostProcessRequested)
            ToastRequested?.Invoke("Recording stopped", Green, true);

        if (_aiPostProcessRequested && _sessionChunks.Count > 0)
        {
            _aiPostProcessRequested = false;
            var fullText = string.Join("", _sessionChunks);
            LlmProcessingRequested?.Invoke(fullText);
        }

        if (_connected)
            StatusChanged?.Invoke("Connected \u2013 ready", Green);
        RecordingStateChanged?.Invoke();
    }

    private async void OnAudioData(byte[] chunk)
    {
        if (_provider is null || !_recording) return;
        if (!IsMuted)
            await _provider.SendAudioAsync(chunk);
        _vad?.ProcessAudio(chunk);
    }

    private void OnTranscriptReceived(string text, bool isFinal)
    {
        if (isFinal)
        {
            InterimTextUpdated?.Invoke("");
            var processed = _replacements.Apply(text);
            AppendTranscript(processed);
            _sessionChunks.Add(processed);

            if (!_aiPostProcessRequested)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await KeyboardInjector.TypeTextAsync(processed);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Text injection error");
                    }
                });
            }
        }
        else
        {
            InterimTextUpdated?.Invoke(text);
        }
    }

    private void AppendTranscript(string text)
    {
        var current = _transcript;
        if (current == "Transcript will appear here \u2026")
            current = "";

        var newText = (current + text).TrimStart();
        if (newText.Length > 500)
            newText = newText[^500..];

        _transcript = newText;
        TranscriptUpdated?.Invoke(newText);
    }
}
```

**Important notes for the implementer:**
- The `Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle)` yield from MainWindow's StopRecording is replaced with `await Task.Delay(50)` since RecordingController doesn't have access to a Dispatcher. This gives pending callbacks time to fire.
- `OnTranscriptReceived` wraps `KeyboardInjector.TypeTextAsync` in `Task.Run` since the event might fire from a non-UI thread and KeyboardInjector needs to run independently.
- The `ToastRequested` event carries `(message, color, autoClose)` — the `autoClose` parameter maps to: `false` when recording starts (toast stays), `true` when recording stops.
- `VadService.SilenceDetected` calls `StopRecording()` directly — no Dispatcher needed since the controller doesn't touch UI.

**Step 2: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```
refactor: extract RecordingController from MainWindow
```

---

### Task 4: Wire RecordingController into MainWindow

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

**Step 1: Replace recording fields and methods in MainWindow**

Remove from MainWindow:
- Fields: `_recording`, `RecordingSource` enum, `_recordingSource`, `_vad`, `_sessionChunks`, `_aiPostProcessRequested`, `_interimText` (lines 38-47)
- `StartRecording()` (lines 484-508)
- `StopRecording()` (lines 510-542)
- `OnToggleHotkey()` (lines 436-455)
- `OnPttKeyDown()` / `OnPttKeyUp()` (lines 459-480)
- `OnAudioData()` (lines 546-552)
- `OnTranscriptReceived()` (lines 556-588)
- `AppendTranscript()` (lines 590-603)

Remove from constructor:
- `_audio.AudioDataAvailable += OnAudioData;` (line 83)
- `_audio.AudioLevelChanged += OnAudioLevel;` (line 84)

Add field:
```csharp
private readonly RecordingController _controller;
```

In constructor, after creating `_audio` and `_keyboardHook`, create the controller:
```csharp
_controller = new RecordingController(_audio, _keyboardHook, _replacements);
_controller.StatusChanged += (text, color) => Dispatcher.Invoke(() => SetStatus(text, new SolidColorBrush(color)));
_controller.ToastRequested += (msg, color, autoClose) => Dispatcher.Invoke(() =>
    ToastWindow.ShowToast(msg, color, autoClose: !autoClose));  // Note: autoClose inversion
_controller.TranscriptUpdated += text => Dispatcher.Invoke(() =>
{
    TranscriptText.Text = text;
    TranscriptScroll.ScrollToBottom();
});
_controller.InterimTextUpdated += text => Dispatcher.Invoke(() =>
{
    InterimText.Text = text;
    if (!string.IsNullOrEmpty(text)) TranscriptScroll.ScrollToBottom();
});
_controller.RecordingStateChanged += () => Dispatcher.Invoke(() =>
    _tray.Update(_connected, _controller.IsRecording, _muted));
_controller.AudioLevelChanged += level => Dispatcher.BeginInvoke(() =>
{
    var parent = (System.Windows.Controls.Border)VuMeterBar.Parent;
    VuMeterBar.Width = level * parent.ActualWidth;
});
_controller.RecordingStopped += () => Dispatcher.Invoke(() => VuMeterBar.Width = 0);
_controller.LlmProcessingRequested += text => Dispatcher.Invoke(() => _ = ProcessWithLlmAsync(text));
```

Update hotkey handlers to delegate to controller:
```csharp
_keyboardHook.TogglePressed += aiKeyHeld => Dispatcher.Invoke(() => _controller.HandleToggle(aiKeyHeld));
_keyboardHook.PttKeyDown += aiKeyHeld => Dispatcher.Invoke(() => _controller.HandlePttDown(aiKeyHeld));
_keyboardHook.PttKeyUp += () => Dispatcher.Invoke(() => _controller.HandlePttUp());
```

In `ConnectAsync()`, after `await _provider.ConnectAsync()`:
- Remove `_provider.TranscriptReceived += OnTranscriptReceived;` (controller handles this)
- Keep `_provider.ErrorOccurred += OnError;` and `_provider.Disconnected += OnDisconnected;`
- Add: `_controller.SetProvider(_provider, true);`
- Add: `_controller.Configure(_settings);`

In `DisconnectAsync()`:
- Replace `StopRecording()` with `_controller.StopRecording();`
- Add: `_controller.SetProvider(null, false);`

In `ApplySettings()`, add:
```csharp
_controller.Configure(_settings);
```

In `ToggleMute()`:
- Replace `_muted` state update logic to also set: `_controller.IsMuted = _muted;`
- Replace `UpdateTrayMenu()` with `_tray.Update(_connected, _controller.IsRecording, _muted);`

Replace all remaining `UpdateTrayMenu()` calls with `_tray.Update(_connected, _controller.IsRecording, _muted)`.

Remove `OnAudioLevel()` method (handled by controller event subscription).

**Step 2: Verify ToastWindow.ShowToast signature**

Check the `ToastWindow.ShowToast` method to confirm the `autoClose` parameter semantics. The controller sends `autoClose: true` for "stop" toasts and `autoClose: false` for "start" toasts. Verify the MainWindow event handler maps this correctly.

**Step 3: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: Build succeeded, all 22 tests pass

**Step 4: Commit**

```
refactor: wire RecordingController into MainWindow
```

---

### Task 5: Final Verification and Cleanup

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs` (if cleanup needed)

**Step 1: Verify line count**

Run: `wc -l src/VoiceDictation/MainWindow.xaml.cs`
Expected: ~350 lines (down from ~860)

**Step 2: Run full build and tests**

Run: `dotnet build && dotnet test`
Expected: Build succeeded, all 22 tests pass

**Step 3: Remove any unused usings or dead code**

Check for:
- Unused `using` statements in MainWindow
- Any orphaned private methods that were not deleted
- Any `_recording` field references that should now be `_controller.IsRecording`

**Step 4: Commit if cleanup was needed**

```
refactor: clean up MainWindow after extraction
```
