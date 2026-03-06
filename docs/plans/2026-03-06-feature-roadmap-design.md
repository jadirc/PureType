# VoiceDictation Feature Roadmap — Design Document

Date: 2026-03-06

## Context

VoiceDictation is a Windows WPF desktop app (.NET 8) that streams microphone audio to Deepgram for real-time transcription and types recognized text into the active window. The app currently supports toggle and push-to-talk modes, configurable hotkeys, multi-language transcription (DE/EN/Auto), audio feedback tones, system tray integration, and terminal-aware keyboard injection.

This document defines the feature roadmap for evolving VoiceDictation from a focused dictation tool into a comprehensive voice productivity platform — including local offline transcription, AI text processing, voice commands, and advanced integrations.

## Research Sources

- Market analysis of Dragon, Voicy, WisprFlow, Aqua Voice, SuperWhisper, OpenWhispr, Handy, Scribe
- Deepgram Nova-3 and Flux announcements (2025/2026)
- [whisper-key-local](https://github.com/PinW/whisper-key-local) — open-source local STT with voice commands, GPU support, VAD

## Phase 1: Quick Wins (Deepgram Improvements)

Low effort, immediate impact — mostly API parameter changes and small UI additions.

### 1.1 Nova-3 Model Upgrade
- Switch Deepgram model from `nova-2` to `nova-3`
- Better accuracy (median WER 6.84%), improved multilingual support
- Change: single query parameter in `DeepgramService.ConnectAsync()`

### 1.2 Interim Results (Live Preview)
- Enable `interim_results=true` in Deepgram WebSocket params
- Show partial transcriptions in the transcript preview as the user speaks
- Visually distinguish interim (grey/italic) from final (normal) text
- Replace interim text with final when received

### 1.3 Keyword Boosting
- UI field or config file for custom keywords/phrases
- Pass as `keywords` parameter to Deepgram API
- Use case: technical jargon, proper names, abbreviations

### 1.4 Microphone Selection
- Enumerate available audio input devices via NAudio
- Add ComboBox to UI for device selection
- Persist selected device in settings.txt
- Fall back to default device if selected device unavailable

### 1.5 Autostart with Windows
- Checkbox in UI to toggle autostart
- Implementation: create/remove shortcut in `shell:startup` folder
- Or: Registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

### 1.6 VU Meter
- Visual microphone level indicator in the UI
- Calculate RMS from PCM audio chunks in `AudioCaptureService`
- Display as horizontal bar or circular indicator near the status dot
- Update at ~10 Hz for smooth animation

## Phase 2: Local Whisper Engine

The biggest feature — offline dictation without cloud dependency.

### 2.1 Whisper.net Integration
- Use [Whisper.net](https://github.com/sandrohanea/whisper.net) — C# bindings for whisper.cpp
- NuGet package: `Whisper.net` + runtime packages (`Whisper.net.Runtime.Cpu`, `.Cuda`, `.CoreML`)
- Create `WhisperService` mirroring `DeepgramService` interface (same events: `TranscriptReceived`, `ErrorOccurred`)
- Record audio to buffer, transcribe on stop (batch mode initially)

### 2.2 Model Selection
- Support models: tiny, base, small, medium, large-v3
- UI dropdown for model selection
- Display model size and expected performance characteristics
- Persist selection in settings

### 2.3 GPU Acceleration
- CUDA support via `Whisper.net.Runtime.Cuda`
- DirectML as fallback for AMD/Intel GPUs
- Auto-detect available GPU on startup
- UI indicator showing CPU vs GPU mode

### 2.4 Hybrid Mode
- Provider dropdown: "Deepgram (Cloud)" / "Whisper (Lokal)"
- Both providers implement shared interface/events
- MainWindow switches provider without changing recording logic
- Deepgram requires API key; Whisper requires downloaded model

### 2.5 Model Download Manager
- Download models from HuggingFace on first use
- Progress bar in UI during download
- Store models in `%LOCALAPPDATA%\VoiceDictation\models\`
- Verify file integrity (SHA256)

### 2.6 Voice Activity Detection (VAD)
- Detect speech vs. silence in audio stream
- Auto-stop recording after configurable silence timeout
- Prevent whisper hallucinations on silent audio
- Use energy-based VAD or Silero VAD .NET port

## Phase 3: Text Intelligence

Transform raw dictation into polished prose.

### 3.1 Voice Commands (Inline)
- Recognize command phrases within dictation stream
- Built-in commands:
  - "Neuer Absatz" / "New paragraph" → inject newline(s)
  - "Neuer Satz" / "New sentence" → inject period + space
  - "Punkt" / "Komma" / "Fragezeichen" → inject punctuation
  - "Löschen" / "Delete" → send Backspace to remove last word
  - "Rückgängig" / "Undo" → send Ctrl+Z
- Language-aware: German and English command sets
- Configurable: enable/disable, add custom commands

### 3.2 LLM Post-Processing
- Optional local LLM pass over transcribed text before injection
- Use cases: remove filler words, fix grammar, improve style, reformat
- Integration options:
  - Ollama (local, REST API)
  - LM Studio (local, OpenAI-compatible API)
  - llama.cpp via C# bindings
- Configurable prompt templates per use case
- UI toggle to enable/disable
- Show processing indicator while LLM works

### 3.3 Text Replacements / Snippets
- User-defined replacement rules in config
- Format: `trigger → replacement` (e.g., "MFG" → "Mit freundlichen Grüßen")
- Applied after transcription, before injection
- Support for multi-line replacements

### 3.4 Custom Vocabulary (Local Whisper)
- Initial prompt / hotwords for Whisper to bias recognition
- Useful for domain-specific terms
- Separate from Deepgram keyword boosting (Phase 1.3)

### 3.5 Punctuation Tuning
- Settings for punctuation behavior (e.g., always add period at end)
- Capitalize first word after sentence-ending punctuation
- Strip or normalize spacing around punctuation

## Phase 4: Voice Commands (Dedicated Mode)

Full voice command system inspired by whisper-key-local.

### 4.1 Command Mode
- Dedicated hotkey to enter command mode (separate from dictation)
- Visual indicator (different status color) when in command mode
- Audio feedback distinct from dictation start/stop

### 4.2 Hotkey Commands
- Trigger phrase → keyboard shortcut
- Example: "Rückgängig" → Ctrl+Z, "Alles markieren" → Ctrl+A
- Uses existing `KeyboardInjector` infrastructure

### 4.3 Type Commands
- Trigger phrase → pre-defined text
- Example: "Meine Email" → "user@example.com"
- Delivered via same injection method as transcription

### 4.4 Shell Commands
- Trigger phrase → execute shell command
- Example: "Öffne Rechner" → `calc.exe`
- Async execution, non-blocking

### 4.5 Configuration Format
- YAML or JSON file in settings directory
- Structure per whisper-key-local pattern:
  ```yaml
  commands:
    - trigger: "rückgängig"
      hotkey: "ctrl+z"
    - trigger: "meine email"
      type: "user@example.com"
    - trigger: "öffne notepad"
      run: "notepad.exe"
  ```

### 4.6 Matching Logic
- Case-insensitive, punctuation-ignored
- Substring matching (saying "bitte öffne notepad" matches "öffne notepad")
- Longest match first to prevent ambiguity
- Optional: fuzzy/semantic matching in future iteration

## Phase 5: UX and Productivity

Make the app a daily companion.

### 5.1 Floating Mini-Widget
- Compact always-on-top overlay (status dot + VU meter only)
- Toggle between full window and mini mode
- Draggable, remembers position
- Click to expand to full UI

### 5.2 Transcript History
- Store all transcriptions with timestamp in local SQLite database
- Searchable history view (separate window)
- Copy/re-inject past transcriptions
- Configurable retention period

### 5.3 Clipboard Mode
- Option to copy text to clipboard instead of typing it
- Useful when target app doesn't support SendInput well
- Toggle in UI or via voice command

### 5.4 App-Specific Profiles
- Different settings per target application
- Detect foreground app → apply profile
- Settings per profile: language, LLM processing, text replacements
- Manage profiles in settings UI

### 5.5 Dictation Pause/Resume
- Pause recording without disconnecting from Deepgram
- Resume without reconnection delay
- Useful for thinking breaks during longer dictation

### 5.6 Multi-Monitor Awareness
- Mini-widget follows active window across monitors
- Or: pin to specific monitor edge

### 5.7 Entity Redaction
- Deepgram's real-time PII redaction (up to 50 entity types)
- Toggle per session or profile
- Useful for sensitive dictation (medical, legal)

## Phase 6: Extended Integration

Long-term vision features.

### 6.1 Server Mode
- Expose transcription as local network service
- Other devices send audio, receive text
- REST or WebSocket API
- Authentication for LAN access

### 6.2 Meeting Mode
- Long-running transcription session
- Speaker diarization (who said what)
- Export to document format (Markdown, TXT)
- Timestamps per utterance

### 6.3 Agent Integration
- Voice input for CLI agents (Claude Code, etc.)
- Pipe transcribed text to stdin of target process
- Or: named pipe / IPC for structured communication

### 6.4 Plugin System
- Define extension points for custom processing
- Plugin interface: receive transcript, return modified text
- Load plugins from DLLs in plugins directory

### 6.5 Additional Cloud Providers
- OpenAI Whisper API
- Groq Whisper API (fast, low cost)
- Azure Speech Services
- Provider interface matching existing DeepgramService pattern

## Technical Constraints

- **Platform:** Windows-only (WPF), .NET 8
- **Architecture:** Code-behind orchestration (no DI/MVVM framework)
- **Unsafe blocks:** Required for Win32 interop, already enabled
- **Audio format:** 16kHz, 16-bit, mono PCM (Deepgram and Whisper compatible)
- **Settings:** Plain-text key=value file — may need migration to JSON/YAML for complex configs (Phase 4+)

## Migration Notes

- Phase 1–3 can use the existing settings.txt format
- Phase 4 (Voice Commands) introduces a separate YAML/JSON commands file
- Phase 5.2 (History) introduces SQLite — new NuGet dependency
- Phase 5.4 (Profiles) may require settings format upgrade to JSON

## Approach per Phase

Each phase should be implemented incrementally — one feature at a time, tested and committed before moving to the next. Features within a phase are ordered by dependency (e.g., 2.1 before 2.4) but can be re-ordered if priorities shift.
