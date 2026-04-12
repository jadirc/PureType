# Timing Toast Display & Statistics Tracking

## Summary

Show transcription (STT) and AI correction processing times in toast notifications and track them in the stats system for historical analysis.

## Toast Behavior

### With Auto-Correction (AI mode)

| Event | Toast Message | Dot Color | Auto-Close |
|---|---|---|---|
| Recording starts | `● Recording (AI)` | red | no |
| Whisper done, AI starts | `Whisper: 1.2s — AI correcting…` | yellow | no |
| AI done | `Done — STT 1.2s + AI 0.7s = 1.9s` | green | yes |

### Without Auto-Correction

| Event | Toast Message | Dot Color | Auto-Close |
|---|---|---|---|
| Recording starts | `● Recording` | red | no |
| Recording stops + Whisper done | `Whisper: 1.2s` | green | yes |

### With LLM Prompt (manual AI mode)

Same as AI mode but with prompt name: `Whisper: 1.2s — AI (PromptName)…` → `Done — STT 1.2s + AI 0.7s = 1.9s`

## Data Flow

```
WhisperService                    RecordingController              MainWindow
  ProcessAsync ──Stopwatch──►     TranscriptionTimed event  ──►   Toast + Stats
                                  stores _lastSttDuration          measures AI duration itself
```

1. `WhisperService.SendFinalizeAsync()` wraps `ProcessAsync` in a `Stopwatch` and fires `TranscriptionTimed(TimeSpan)` after processing completes.
2. `RecordingController` subscribes to `TranscriptionTimed`, stores `_lastSttDuration`, and exposes it through extended events to MainWindow.
3. `MainWindow` wraps the LLM call in `ProcessAutoCorrectAsync`/`ProcessWithLlmAsync` with its own `Stopwatch` for AI duration.
4. Both durations are passed to toast messages and `StatsService.RecordSession`.

## Interface Change

`ITranscriptionProvider` gets a new event with a default no-op implementation:

```csharp
event Action<TimeSpan>? TranscriptionTimed { add { } remove { } }
```

Only `WhisperService` implements it. `DeepgramService` uses the default no-op (streaming model has no discrete processing phase).

## Stats Model Extension

`DayStats` gains two fields:

```csharp
public int SttMilliseconds { get; set; }
public int AiMilliseconds { get; set; }
```

`StatsService.RecordSession` signature becomes:

```csharp
void RecordSession(int wordCount, int durationSeconds, int sttMs = 0, int aiMs = 0)
```

Backwards compatible — existing `stats.json` files without the new fields deserialize with 0 defaults.

## StatsWindow Changes

Today and Total sections each gain a line showing average processing times per session:

```
Ø STT: 1.4s · Ø AI: 0.8s
```

History grid gains two new columns: `Ø STT` and `Ø AI` (average milliseconds per session for that day, formatted as seconds with one decimal).

Days with 0 STT/AI milliseconds show `—` instead of `0.0s`.

## Files Changed

| File | Change |
|---|---|
| `WhisperService.cs` | Stopwatch around ProcessAsync, fire TranscriptionTimed event |
| `ITranscriptionProvider.cs` | Add `TranscriptionTimed` event with default no-op |
| `RecordingController.cs` | Subscribe TranscriptionTimed, store `_lastSttDuration`, expose via extended events |
| `MainWindow.xaml.cs` | Stopwatch around LLM calls, timing in toasts, pass timing to StatsService |
| `StatsService.cs` | Extend DayStats and RecordSession with SttMilliseconds/AiMilliseconds |
| `StatsWindow.xaml.cs` | Display average STT/AI times |
| `StatsWindow.xaml` | Add timing display elements and grid columns |
