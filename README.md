# Voice Dictation

A lightweight Windows desktop app that turns your voice into keystrokes — in real time. Speak into your microphone and the recognized text is typed directly into whichever window has focus.

Built with WPF (.NET 8). Supports two transcription engines: [Deepgram](https://deepgram.com/) Nova-2 (cloud, via WebSocket) and [Whisper.net](https://github.com/sandrohanea/whisper.net) (local, offline, with optional CUDA GPU acceleration).

![Windows](https://img.shields.io/badge/platform-Windows-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

---

<p align="center">
  <img src="docs/screenshot.png" alt="Voice Dictation screenshot" width="350">
</p>

## Features

- **Dual transcription engines**
  - **Deepgram** (cloud) — Real-time streaming via WebSocket with Nova-2 model, interim results with live preview
  - **Whisper** (local) — Offline transcription using [Whisper.net](https://github.com/sandrohanea/whisper.net) with GGML models, optional CUDA GPU acceleration
- **Types into any window** — Recognized text is injected as simulated keystrokes (Unicode `SendInput`), working in editors, browsers, chat apps, and terminals
- **Terminal-aware** — Automatically detects terminal windows (Windows Terminal, PowerShell, cmd, Warp, Alacritty, etc.) and uses clipboard paste instead of `SendInput`
- **Two input modes**
  - **Toggle** — Press a hotkey to start/stop recording
  - **Push-to-Talk** — Hold a key to record, release to stop
- **Voice activity detection (VAD)** — Automatically stops recording after a configurable silence duration
- **Configurable shortcuts** — Assign any key or key combination (including Win+key chords) for toggle and PTT
- **Microphone selection** — Choose from available input devices with a live VU meter
- **Keyword boosting** — Improve recognition accuracy for domain-specific terms (Deepgram)
- **Multi-language** — Supports German, English, and automatic language detection
- **Audio feedback** — Choose from 5 signal tone presets (or silence) for recording start/stop
- **System tray** — Minimizes to tray, runs unobtrusively in the background
- **Start minimized** — Optional launch directly to system tray
- **Auto-connect** — Reconnects automatically on startup if an API key is saved
- **Dark UI** — Catppuccin Mocha-inspired dark theme

## Important Notes

> **Local Whisper mode currently requires an NVIDIA GPU with CUDA support.** CPU-only inference is not yet supported. Make sure you have up-to-date [NVIDIA drivers](https://www.nvidia.com/drivers) with CUDA installed.

> **Deepgram (cloud mode)** requires a [Deepgram account](https://console.deepgram.com/). A free tier is available, but usage beyond the free quota will incur costs. See [Deepgram pricing](https://deepgram.com/pricing) for details.

## Prerequisites

- **Windows 10/11**
- [**.NET 8 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- **For Whisper (local):** NVIDIA GPU with CUDA drivers (required)
- **For Deepgram (cloud):** A [Deepgram API key](https://console.deepgram.com/) (free tier available, pay-as-you-go beyond that)
- Whisper GGML models are downloaded automatically on first use (~150 MB–1.5 GB depending on model size)

## Download

Grab the latest release from the [Releases page](https://github.com/jadirc/VoiceDictation/releases):

| Asset | Description |
|---|---|
| `VoiceDictation-vX.Y.Z-win-x64.zip` | Requires [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed |
| `VoiceDictation-vX.Y.Z-win-x64-portable.zip` | Standalone, no runtime needed (~70 MB) |

## Getting Started

```bash
git clone https://github.com/jadirc/VoiceDictation.git
cd VoiceDictation
dotnet run
```

On first launch, enter your Deepgram API key in the settings panel and click **Verbinden** (Connect).

## Usage

| Action | Default Shortcut |
|---|---|
| Start/stop recording (toggle mode) | `F9` |
| Record while held (push-to-talk mode) | `Right Ctrl` |

1. **Connect** — Enter your API key and click *Verbinden*
2. **Speak** — Press your hotkey and start talking. The transcript appears in the preview panel and is simultaneously typed into the focused window.
3. **Switch modes** — Use the *Modus* dropdown to switch between Toggle and Push-to-Talk
4. **Customize shortcuts** — Click the shortcut field and press your desired key combination

Settings (API key, language, mode, shortcuts, tone, window position) are persisted automatically to `%LOCALAPPDATA%\VoiceDictation\settings.txt`.

## Architecture

```
VoiceDictation/
├── MainWindow.xaml(.cs)          # UI & orchestration
├── Services/
│   ├── ITranscriptionProvider.cs # Common interface for transcription engines
│   ├── DeepgramService.cs        # WebSocket streaming to Deepgram Nova-2
│   ├── WhisperService.cs         # Local offline transcription via Whisper.net
│   ├── WhisperModelManager.cs    # GGML model downloading & caching
│   ├── AudioCaptureService.cs    # Microphone capture (NAudio, 16kHz/16bit/mono)
│   ├── VadService.cs             # Voice activity detection (auto-stop on silence)
│   ├── KeyboardInjector.cs       # Win32 SendInput / clipboard paste
│   ├── SoundFeedback.cs          # Synthesized WAV tone presets
│   └── LogWindowSink.cs          # Serilog sink for the log viewer
├── Helpers/
│   └── KeyboardHookService.cs    # Low-level keyboard hook (WH_KEYBOARD_LL)
├── LogWindow.xaml(.cs)           # Debug log viewer
└── Resources/
    └── mic.ico                   # Application icon
```

The app follows a simple code-behind architecture — no DI container or MVVM framework. `MainWindow` is the central orchestrator that wires together the services and manages state.

### Data Flow

```
                                  ┌─ DeepgramService (WebSocket, cloud)
Microphone → AudioCaptureService ─┤                                      → transcript text
                                  └─ WhisperService (local, offline)            ↓
                                                                   KeyboardInjector → active window
```

## Dependencies

| Package | Purpose |
|---|---|
| [NAudio](https://github.com/naudio/NAudio) 2.2.1 | Audio capture |
| [Whisper.net](https://github.com/sandrohanea/whisper.net) 1.9.0 | Local Whisper transcription |
| Whisper.net.Runtime 1.9.0 | Whisper native runtime (CPU) |
| Whisper.net.Runtime.Cuda.Windows 1.9.0 | Whisper CUDA GPU acceleration |
| [Serilog](https://serilog.net/) 4.2.0 | Structured logging |
| Serilog.Sinks.File 6.0.0 | File log output |

## Building

```bash
dotnet build
```

The project targets `net8.0-windows` and requires `UseWPF` and `UseWindowsForms` (for the tray icon). `AllowUnsafeBlocks` is enabled for Win32 interop structs.

## Logging

Logs are written to `%LOCALAPPDATA%\VoiceDictation\log.txt` (rolling daily, 7-day retention). A built-in log viewer is accessible via the **Log** button in the transcript panel.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push to the branch and open a Pull Request

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Deepgram](https://deepgram.com/) for the real-time cloud speech recognition API
- [Whisper.net](https://github.com/sandrohanea/whisper.net) for local offline transcription
- [NAudio](https://github.com/naudio/NAudio) for .NET audio capture
- UI theme inspired by [Catppuccin Mocha](https://github.com/catppuccin/catppuccin)
