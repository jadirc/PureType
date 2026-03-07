# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build
dotnet test
dotnet run --project src/PureType
```

A `.slnx` file exists at the repo root. `dotnet build` and `dotnet test` operate on it automatically.

## What This Is

A Windows-only WPF desktop app (.NET 8) for real-time voice dictation. Supports two transcription engines: Deepgram (cloud, WebSocket) and Whisper.net (local, NVIDIA CUDA required). Recognized text is typed into the focused window via simulated keyboard input (Win32 `SendInput`).

All code and UI are in English.

## Architecture

**MainWindow** (`MainWindow.xaml.cs`) is the central orchestrator — it owns the services, manages connection/recording state, and handles hotkey events. There is no DI container or MVVM framework; everything is wired directly in code-behind.

### Services

- **ITranscriptionProvider** — Common interface for transcription engines.
- **DeepgramService** — Opens a `ClientWebSocket` to `wss://api.deepgram.com/v1/listen` (nova-2 model, PCM 16kHz mono). Receives JSON responses and parses `channel.alternatives[0].transcript`. Exposes `TranscriptReceived`, `ErrorOccurred`, `Disconnected` events.
- **WhisperService** — Local offline transcription via Whisper.net with GGML models and optional CUDA GPU acceleration.
- **WhisperModelManager** — Downloads and caches GGML models.
- **AudioCaptureService** — Uses NAudio `WaveInEvent` to capture microphone at 16kHz/16bit/mono. Fires `AudioDataAvailable` with raw PCM chunks.
- **VadService** — Voice activity detection, auto-stops recording after silence.
- **KeyboardInjector** (static) — Converts text to Unicode `SendInput` calls, injecting keystrokes into the active window.

### Helpers (Win32 interop)

- **KeyboardHookService** — Unified low-level keyboard hook (`WH_KEYBOARD_LL`) for toggle shortcut detection, push-to-talk key tracking, and Win key handling.

### Input Modes

Both modes are always active simultaneously:
1. **Toggle** — Press shortcut to start/stop recording.
2. **Push-to-Talk** — Hold shortcut to record, release to stop.

### Settings

All settings are persisted as JSON to `%LOCALAPPDATA%\PureType\settings.json`. The `SettingsService` handles load/save and auto-migrates from the legacy `settings.txt` format on first run.

## Key Dependencies

- **NAudio 2.2.1** — Audio capture
- **Whisper.net 1.9.0** — Local transcription (+ CUDA runtime)
- `AllowUnsafeBlocks` is enabled in the csproj (required by Win32 interop structs)
