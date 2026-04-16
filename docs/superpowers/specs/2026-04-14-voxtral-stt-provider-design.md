# Voxtral STT Provider Integration

## Summary

Add Mistral Voxtral as a third speech-to-text provider alongside Deepgram (cloud streaming) and Whisper (local batch). Voxtral operates as a cloud batch provider: audio is buffered during recording and uploaded to the Mistral REST API on finalize.

## Architecture

### VoxtralService

New class `VoxtralService : ITranscriptionProvider` in `src/PureType/Services/`.

**Lifecycle:**

1. **ConnectAsync** — Validates that a Mistral API key is available (resolved from `LlmSettings.EndpointKeys` for `api.mistral.ai`). Sets `IsConnected = true`. No persistent network connection needed.
2. **SendAudioAsync** — Writes PCM chunks into a `MemoryStream` buffer (same pattern as WhisperService).
3. **SendFinalizeAsync** — Packages the buffer as WAV (44-byte header + PCM-16 data at 16kHz mono), sends via `HttpClient` as `multipart/form-data` POST to `https://api.mistral.ai/v1/audio/transcriptions`. Parses JSON response, fires `TranscriptReceived(text, isFinal: true)`. Measures duration via `Stopwatch`, fires `TranscriptionTimed`.
4. **SetLanguageAsync** — Stores the new language for the next request. No reconnect needed.
5. **DisposeAsync** — Disposes `HttpClient` and buffer.

**Constructor:** `VoxtralService(string apiKey, string model, string language)`

**Silence detection:** Same RMS-based per-chunk analysis as WhisperService (threshold 0.035, minimum 2 speech chunks). Fires a `SilenceSkipped` event (MainWindow hooks into it to show "No speech detected" toast, same pattern as WhisperService). Prevents unnecessary API calls.

**Timeout:** 30-second `CancellationTokenSource` on the HTTP request, matching WhisperService's timeout pattern.

**WAV packaging:** A static helper method writes a 44-byte RIFF/WAV header (PCM-16, 16kHz, mono) followed by raw PCM data into a `MemoryStream`. No external libraries needed.

### API Key Resolution

The Mistral API key is resolved from the existing LLM configuration:

1. Search `LlmSettings.EndpointKeys` for a key whose URL contains `api.mistral.ai`.
2. If not found: show MessageBox "Please configure a Mistral API key in AI Post-Processing settings first."

No separate API key field in transcription settings.

### Error Handling

- **HTTP errors** (timeout, 401, 429, 500): Fire `ErrorOccurred` with a human-readable message. No automatic reconnect needed — each finalize is an independent request.
- **No API key:** MessageBox at connect time pointing user to AI settings.
- **Empty buffer / silence:** RMS-based detection skips the API call, shows toast.

## Settings

### TranscriptionSettings Changes

New field:

```csharp
public string VoxtralModel { get; init; } = "mistral-small-latest";
```

The existing `Provider` string gains `"voxtral"` as a third valid value (alongside `"deepgram"` and `"whisper"`).

### Available Models

| Model | Description |
|---|---|
| `mistral-small-latest` | Fast transcription, good quality |
| `mistral-medium-latest` | Higher quality, slower |

## UI Changes

### MainWindow.xaml

ProviderCombo gets a third entry:

```xml
<ComboBoxItem Content="Voxtral (Cloud)" Tag="voxtral"/>
```

### SettingsWindow.xaml

- Third ComboBoxItem in ProviderCombo: `"Voxtral (Cloud)"` with tag `"voxtral"`.
- New `VoxtralModelPanel` (StackPanel): label "VOXTRAL MODEL" + ComboBox with the two models and descriptive tooltips.

### SettingsWindow.xaml.cs

`SetProviderVisibility` changes from binary if/else to a three-way switch:

- `deepgram` — ApiKeyPanel visible; WhisperModelPanel, WhisperTuningPanel, VoxtralModelPanel collapsed.
- `whisper` — WhisperModelPanel, WhisperTuningPanel visible; ApiKeyPanel, VoxtralModelPanel collapsed.
- `voxtral` — VoxtralModelPanel visible; ApiKeyPanel, WhisperModelPanel, WhisperTuningPanel collapsed.

### MainWindow.xaml.cs

`ConnectAsync` expands from if/else to if/else-if/else:

- `voxtral` branch: resolve Mistral key from EndpointKeys, validate, create `VoxtralService(apiKey, model, language)`.
- Status label: `"Voxtral (Cloud)"`.

## Files to Create

- `src/PureType/Services/VoxtralService.cs`

## Files to Modify

- `src/PureType/Services/SettingsService.cs` — Add `VoxtralModel` to `TranscriptionSettings`
- `src/PureType/MainWindow.xaml` — Add ComboBoxItem
- `src/PureType/MainWindow.xaml.cs` — Add voxtral branch in `ConnectAsync`, API key resolution
- `src/PureType/SettingsWindow.xaml` — Add ComboBoxItem + VoxtralModelPanel
- `src/PureType/SettingsWindow.xaml.cs` — Extend `SetProviderVisibility`, save VoxtralModel, populate model combo
