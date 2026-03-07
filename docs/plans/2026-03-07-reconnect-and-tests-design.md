# Auto-Reconnect & Unit Tests Design

## Overview

Two improvements: automatic WebSocket reconnection for DeepgramService, and unit test coverage for untested services.

## 1. Auto-Reconnect for DeepgramService

### Scope

Deepgram only — WhisperService is local and has no network disconnect scenario.

### Location

Inside `DeepgramService` itself. MainWindow stays simple; DeepgramService handles retry internally.

### New Event

```csharp
public event Action<int, int>? Reconnecting; // (attempt, maxAttempts)
```

Fired before each retry attempt so the UI can show status like "Reconnecting (3/10)…".

### Retry Logic (in ReceiveLoopAsync)

When the receive loop ends unexpectedly (WebSocket close or exception) and `_cts` is **not** cancelled (i.e., user did not request disconnect):

1. Set `_reconnecting = true` flag to prevent parallel retry loops
2. Dispose old `ClientWebSocket`
3. Loop up to **10 attempts**:
   - Fire `Reconnecting(attempt, 10)`
   - Wait with exponential backoff: `min(2^(attempt-1), 30)` seconds → 1s, 2s, 4s, 8s, 16s, 30s, 30s, 30s, 30s, 30s (~2.5 min total)
   - Create new `ClientWebSocket`, attempt `ConnectAsync`
   - On success: start new receive loop, reset flag, return
   - On failure: continue to next attempt
4. After 10 failures: fire `Disconnected`, set `_reconnecting = false`

### SendAudioAsync During Reconnect

No change needed — existing guard `if (_ws?.State != WebSocketState.Open) return;` silently drops audio chunks while reconnecting. No buffering.

### KeepAlive Timer

Stays running — its guard also checks `WebSocketState.Open`, so it's harmless during reconnect.

### UI Side (MainWindow)

- Subscribe: `_provider.Reconnecting += OnReconnecting;` (cast to `DeepgramService` or use pattern match)
- Handler: `SetStatus($"Reconnecting ({attempt}/{max})…", Yellow)` + update tray
- No change to `ITranscriptionProvider` interface

## 2. Unit Tests

### Existing Infrastructure

- xUnit test project at `tests/VoiceDictation.Tests/`
- Already in `.slnx` solution
- `InternalsVisibleTo("VoiceDictation.Tests")` in main csproj
- 4 existing tests for `SettingsService.MigrateFromTxt`

### New Test Classes

**`ReplacementServiceTests`**
- `Apply_replaces_case_insensitive`
- `Apply_handles_arrow_delimiter`
- `Apply_handles_unicode_arrow`
- `Apply_converts_backslash_n_to_newline`
- `Apply_returns_original_when_no_rules`
- `Apply_returns_original_when_empty_text`
- `Apply_applies_rules_in_order`

Test setup: create temp file with rules, construct `ReplacementService(tempPath)`, test `Apply()`.

**`VadServiceTests`**
- `SilenceDetected_fires_after_timeout`
- `SilenceDetected_does_not_fire_during_speech`
- `SilenceDetected_resets_timer_on_speech`
- `Reset_restarts_silence_timer`

Test data: synthetic PCM byte arrays — silence (zeroes), loud (high amplitude 16-bit samples).

**`UiHelperTests`**
- `FormatShortcut_single_modifier`
- `FormatShortcut_multiple_modifiers`
- `FormatShortcut_left_right_keys`
- `ParseShortcut_roundtrip`
- `ParseShortcut_uses_default_on_invalid`

Pure functions, no I/O needed.

**`SettingsServiceTests` (extend)**
- `Save_and_Load_roundtrip`
- `Load_returns_defaults_when_no_file_exists`

### No New Dependencies

No mocking framework needed — all test targets are pure logic or use temp files.
