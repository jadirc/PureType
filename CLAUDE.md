# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build
dotnet run
```

No solution file exists; build the single `.csproj` directly. No test project exists.

## What This Is

A Windows-only WPF desktop app (.NET 8) that captures microphone audio, streams it to the **Deepgram** real-time transcription API via WebSocket, and types the recognized text into the currently focused window using simulated keyboard input (Win32 `SendInput`).

The UI language is German.

## Architecture

**MainWindow** (`MainWindow.xaml.cs`) is the central orchestrator — it owns the services, manages connection/recording state, and handles hotkey events. There is no DI container or MVVM framework; everything is wired directly in code-behind.

### Services

- **DeepgramService** — Opens a `ClientWebSocket` to `wss://api.deepgram.com/v1/listen` (nova-2 model, PCM 16kHz mono). Receives JSON responses and parses `channel.alternatives[0].transcript`. Exposes `TranscriptReceived`, `ErrorOccurred`, `Disconnected` events.
- **AudioCaptureService** — Uses NAudio `WaveInEvent` to capture microphone at 16kHz/16bit/mono. Fires `AudioDataAvailable` with raw PCM chunks.
- **KeyboardInjector** (static) — Converts text to Unicode `SendInput` calls, injecting keystrokes into the active window.

### Helpers (Win32 interop)

- **GlobalHotkey** — Registers a system-wide hotkey via `RegisterHotKey`/WndProc. Used for F9 toggle.
- **LowLevelKeyboardHook** — Installs a `WH_KEYBOARD_LL` hook for key-down/key-up detection without stealing focus. Used for Right Ctrl push-to-talk.

### Input Modes

1. **Toggle mode** (default) — F9 starts/stops recording.
2. **Push-to-Talk mode** — Hold Right Ctrl to record, release to stop.

### Settings

API key is persisted to `%LOCALAPPDATA%\VoiceDictation\settings.txt`.

## Key Dependencies

- **NAudio 2.2.1** — Audio capture
- `AllowUnsafeBlocks` is enabled in the csproj (required by Win32 interop structs)
