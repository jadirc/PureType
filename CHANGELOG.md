# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## 1.0.0 (2026-03-06)


### Bug Fixes

* correct GitHub username in README URLs ([df762f5](https://github.com/jadirc/VoiceDictation/commit/df762f5541c8297a2e2802d5c0901656c02cc1cb))
* integrate release build into Release Please workflow ([8417e8b](https://github.com/jadirc/VoiceDictation/commit/8417e8bed2b71372f0ec93084918fbd6c7dbc830))

## 1.0.0 (2026-03-06)


### Bug Fixes

* correct GitHub username in README URLs ([df762f5](https://github.com/jadirc/VoiceDictation/commit/df762f5541c8297a2e2802d5c0901656c02cc1cb))

## [0.1.0] - 2026-03-05

### Added

- Real-time voice-to-text transcription using Deepgram Nova-2 via WebSocket
- Simulated keystroke injection into any focused window (Unicode SendInput)
- Terminal-aware mode: automatic clipboard paste for terminal windows (Windows Terminal, PowerShell, cmd, Warp, Alacritty)
- Toggle recording mode with configurable hotkey (default: F9)
- Push-to-Talk mode with configurable hotkey (default: Right Ctrl)
- Configurable keyboard shortcuts with support for modifier keys and Win+key chords
- Multi-language support: German, English, and automatic language detection
- Five signal tone presets for audio feedback on recording start/stop
- System tray integration with minimize-to-tray
- Auto-connect on startup when API key is saved
- Dark UI theme inspired by Catppuccin Mocha
- Built-in log viewer for debugging
- Settings persistence to %LOCALAPPDATA%\VoiceDictation\settings.txt
