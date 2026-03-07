# MainWindow Refactoring Design

## Goal

Extract two large concerns from MainWindow.xaml.cs (~860 lines) into separate classes, reducing it to ~350 lines while improving testability.

## 1. TrayIconManager

**File:** `Helpers/TrayIconManager.cs` (~180 lines)

### Responsibility

Creates and manages the `NotifyIcon`, context menu, status label, and icon rendering with status dot overlay.

### Constructor

```csharp
TrayIconManager(System.Drawing.Icon baseIcon)
```

### Public API

- `void Update(bool connected, bool recording, bool muted)` — updates label text/color, connect/disconnect text, icon dot
- `void Dispose()` — disposes NotifyIcon

### Events (MainWindow subscribes)

- `event Action? ConnectRequested`
- `event Action? DisconnectRequested`
- `event Action? MuteToggleRequested`
- `event Action? SettingsRequested`
- `event Action? ShowRequested`
- `event Action? ExitRequested`

### What moves in

- `SetupTrayIcon()` logic (menu construction, click handlers)
- `UpdateTrayMenu()` (label text, color, connect/disconnect text, icon update)
- `CreateStatusIcon()` + `DestroyIcon` P/Invoke
- All tray-related fields: `_trayIcon`, `_trayStatusLabel`, `_trayConnectItem`, `_trayMuteItem`, `_baseTrayIcon`

### What stays in MainWindow

- `ShowFromTray()` — needs `Show()`, `WindowState`, `Activate()` (Window methods)
- `OnStateChanged` — needs `Visibility`, `ShowInTaskbar`
- `ToggleMute()` — muted state lives in MainWindow, calls `_tray.Update(...)`

## 2. RecordingController

**File:** `Services/RecordingController.cs` (~200 lines)

### Responsibility

Manages recording lifecycle: start/stop, toggle/PTT logic, audio routing, VAD, session chunks, AI trigger state.

### Constructor

```csharp
RecordingController(AudioCaptureService audio, KeyboardHookService keyboardHook, ReplacementService replacements)
```

### Public API

- `void Configure(AppSettings settings)` — applies VAD/LLM settings
- `void SetProvider(ITranscriptionProvider? provider)` — called on connect/disconnect
- `void HandleToggle(bool aiKeyHeld)` — called from MainWindow hotkey handler
- `void HandlePttDown(bool aiKeyHeld)` / `HandlePttUp()`
- `bool IsRecording { get; }`
- `bool IsMuted { get; set; }` — audio routing guard

### Events (MainWindow subscribes)

- `event Action<string, Color>? StatusChanged` — e.g. ("Recording", Red)
- `event Action<string, bool>? ToastRequested` — (message, isError)
- `event Action<string>? TranscriptUpdated` — final text for display
- `event Action<string>? InterimTextUpdated` — interim text
- `event Action? RecordingStateChanged` — for tray update
- `event Action<double>? AudioLevelChanged` — VU meter forwarding
- `event Action<string>? LlmProcessingRequested` — fires instead of calling ProcessWithLlmAsync directly

### What moves in

- `StartRecording()`, `StopRecording()` core logic
- `OnToggleHotkey()`, `OnPttKeyDown()`, `OnPttKeyUp()` logic
- `OnAudioData()` — audio routing + VAD
- `OnTranscriptReceived()` — replacement + chunk collection + KeyboardInjector
- `AppendTranscript()` — text truncation logic (returns string via event instead of setting UI directly)
- State fields: `_recording`, `_recordingSource`, `_vad`, `_sessionChunks`, `_aiPostProcessRequested`, `_interimText`, `RecordingSource` enum

### What stays in MainWindow

- `ProcessWithLlmAsync()` — uses `_settings.Llm.*`
- `ConnectAsync()` / `DisconnectAsync()` — orchestration, calls `controller.SetProvider()`
- UI updates via event subscriptions (1-2 lines each)

## 3. Resulting MainWindow Structure (~350 lines)

**Fields:** `_provider`, `_connected`, `_settings`, `_settingsService`, `_isLoading`, `_tray`, `_controller`, shortcut keys, colors

**Methods:**
- Constructor — init, create TrayIconManager + RecordingController, subscribe events
- Settings: `LoadSettings()`, `SaveSettings()`, `ApplySettings()`, `ApplyAiTriggerKey()`
- Connection: `ConnectAsync()`, `DisconnectAsync()`, `RegisterHotkeys()`
- UI handlers: button clicks, combo changes (thin)
- Microphone: `PopulateMicrophones()`, `SelectMicrophoneByName()`
- LLM: `ProcessWithLlmAsync()`
- Event handlers for controller/tray events (1-2 lines each, UI updates)
- Window: `SetStatus()`, `Window_Closing()`, `OnStateChanged()`, `ShowFromTray()`

## Communication Pattern

All communication uses events — consistent with existing patterns in the codebase (DeepgramService, AudioCaptureService). No interfaces needed.

```
KeyboardHookService ──events──> RecordingController ──events──> MainWindow (UI updates)
                                       │
                                       ├── AudioCaptureService
                                       ├── ITranscriptionProvider
                                       ├── ReplacementService
                                       └── KeyboardInjector

TrayIconManager ──events──> MainWindow (connect/disconnect/settings/show/exit)
MainWindow ──Update()──> TrayIconManager (state changes)
```

## Constraints

- Pure refactoring — no behavior changes
- No new dependencies or packages
- Existing tests must continue to pass
