# LLM Auto-Correction Pipeline Design

**Date:** 2026-04-09
**Status:** Approved
**Motivation:** Provide automatic grammar, punctuation and style correction for every dictation session without requiring the user to manually trigger LLM processing via prompt keys.

## Context

PureType currently offers two levels of text processing:

1. **Local, instant** — Replacements, auto-capitalize, code formatter, and the upcoming TextCleanupService (filler removal, dedup, whitespace).
2. **Manual LLM** — User presses a prompt key during recording to trigger heavy post-processing (rewriting, translating, summarizing) at session end.

There is no middle ground: lightweight local cleanup cannot fix grammar or improve style, and manual LLM processing requires conscious action every time. Users who always want clean, polished output must remember to press a prompt key on every single recording.

## Design

### Always-On LLM Stage (Batch at Session End)

A new pipeline stage that automatically sends the entire session transcript to an LLM for correction when recording stops. No prompt key needed — it fires on every session when enabled.

### Pipeline Position

```
During recording (live):
  Whisper → ReplacementService → AutoCapitalize → CodeFormatter → [TextCleanup]
  → Transcript window (display only — NO typing into target window)

Recording stops:
  Collected session text → LLM Auto-Correction → Output (Type/Paste/Copy)
```

When auto-correction is enabled, the `RecordingController` suppresses live text output (same mechanism as existing prompt-key suppression via `_selectedPrompt != null`). The user sees raw transcript chunks in PureType's transcript panel for visual feedback, but text only reaches the target window after LLM processing.

### Interaction with Prompt-Key System

**Prompt key takes precedence.** If the user presses a prompt key during recording, the manual prompt-key LLM processing runs instead of auto-correction. No double API call. The logic: the user explicitly chose a heavy transformation — auto-correction would be redundant.

Decision flow at session end:
```
if prompt_key_active:
    → existing ProcessWithLlmAsync(text, selectedPrompt)
elif auto_correction_enabled:
    → new AutoCorrectAsync(text)
else:
    → text already typed live, nothing to do
```

## Auto-Correction Provider

### Separate Configuration with Fallback

The auto-correction stage has its own provider settings (BaseUrl, ApiKey, Model), independent of the prompt-key LLM provider. This allows the user to use a cheap/fast model (e.g. GPT-4o-mini) for auto-correction while reserving a powerful model (e.g. Claude) for manual prompt-key tasks.

If the auto-correction provider is not configured, it falls back to the existing `LlmSettings` provider.

### Resolution Order

```
1. AutoCorrection.BaseUrl / ApiKey / Model  (if all three set)
2. Llm.BaseUrl / Llm.ApiKey / Llm.Model     (fallback)
3. Feature disabled                          (if neither configured)
```

API key resolution uses the existing `EndpointKeys` dictionary pattern: per-endpoint keys stored by normalized URL.

### Client Selection

Reuses the existing `ILlmClient` abstraction with `OpenAiLlmClient` and `AnthropicLlmClient`. Client selection follows the same URL-based pattern: URL contains `anthropic.com` → `AnthropicLlmClient`, otherwise → `OpenAiLlmClient` (OpenAI-compatible).

## System Prompt

### Hardcoded Base + Optional Style Instructions

The auto-correction system prompt is composed of two parts:

1. **Hardcoded base prompt** (not user-editable):
   ```
   Fix grammar, punctuation and spelling errors in the following dictated text.
   Keep the original meaning, language and content unchanged.
   Do not add, remove or rephrase content.
   Reply with ONLY the corrected text — no preamble, no explanation.
   ```

2. **User-configurable style instructions** (optional free-text field in Settings):
   Appended to the base prompt. Examples: "Use formal tone", "Keep sentences short and direct", "Prefer active voice".

The final system prompt sent to the LLM:
```
{hardcoded_base_prompt}

{user_style_instructions}   ← only if non-empty
```

The input text is wrapped in `<input>` tags (same pattern as existing LLM processing) to prevent prompt injection from dictated content.

## Settings

### Data Model

New record in `SettingsService.cs`:

```csharp
public record AutoCorrectionSettings
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";
    public string StyleInstructions { get; init; } = "";
}
```

New property in `AppSettings`:

```csharp
public AutoCorrectionSettings AutoCorrection { get; init; } = new();
```

### UI (Settings Window)

New section in the Settings window, after the existing "AI Post-Processing" section:

```
── Auto-Correction ──────────────────────────────
☑ Auto-correct transcription (LLM)

  Provider (leave empty to use AI Post-Processing provider):
  API Endpoint:  [                              ]
  API Key:       [                              ]
  Model:         [                              ]

  Additional style instructions (optional):
  [                                              ]
  [  e.g. "Use formal tone"                     ]
```

The provider fields are collapsed when the toggle is off (same pattern as existing `LlmSettingsPanel`).

## Overlay Indicator

When auto-correction is enabled, the `StatusOverlayWindow` shows this in its status text:

- **Idle:** `Connected – ready (AI)` — the `(AI)` suffix signals that auto-correction is active.
- **Recording:** `● Recording (AI)` — user knows output will be corrected.
- **Processing:** `● AI correcting…` — shown after recording stops while waiting for LLM response.

No new UI elements needed — the existing `StatusText` TextBlock and `StatusDot` Ellipse are sufficient. The `(AI)` suffix is appended by `RecordingController` when `AutoCorrection.Enabled` is true.

## Implementation in RecordingController

### New State

```csharp
private bool _autoCorrectionEnabled;
```

Set in `Configure(AppSettings settings)` from `settings.AutoCorrection.Enabled`.

### Modified Behavior

**`OnTranscriptReceived`:** When `_autoCorrectionEnabled` is true (and no prompt key active), suppress live text output — collect chunks in `_sessionChunks` but do not type/paste/copy. Transcript display in PureType's window continues as before.

The condition for suppressing output changes from:
```csharp
if (_selectedPrompt == null)
```
to:
```csharp
if (_selectedPrompt == null && !_autoCorrectionEnabled)
```

**`StopRecording`:** After existing prompt-key handling, add auto-correction path:

```csharp
if (_selectedPrompt != null && _sessionChunks.Count > 0)
{
    // existing prompt-key path (unchanged)
}
else if (_autoCorrectionEnabled && _sessionChunks.Count > 0)
{
    var fullText = string.Join("", _sessionChunks);
    AutoCorrectionRequested?.Invoke(fullText);
}
```

### New Event

```csharp
public event Action<string>? AutoCorrectionRequested;
```

Handled in `MainWindow.xaml.cs` — resolves provider (own config or fallback), creates `ILlmClient`, calls `ProcessAsync` with the hardcoded base prompt + style instructions.

## File Changes

### New Files

None — all logic integrates into existing files.

### Modified Files

| File | Change |
|------|--------|
| `src/PureType/Services/SettingsService.cs` | Add `AutoCorrectionSettings` record, add to `AppSettings`, handle in migration |
| `src/PureType/Services/RecordingController.cs` | Add `_autoCorrectionEnabled` flag, suppress live output, fire `AutoCorrectionRequested` |
| `src/PureType/MainWindow.xaml.cs` | Handle `AutoCorrectionRequested` event, resolve provider, call LLM |
| `src/PureType/SettingsWindow.xaml` | Add auto-correction section UI |
| `src/PureType/SettingsWindow.xaml.cs` | Bind toggle and fields to settings |
| `src/PureType/StatusOverlayWindow.xaml.cs` | No change needed — `UpdateState` already accepts `statusText` |
| `tests/PureType.Tests/Services/RecordingControllerTests.cs` | Test suppression logic and event firing |

## Test Cases

```csharp
// Auto-correction enabled: live output suppressed
[AutoCorrection.Enabled = true, no prompt key]
→ OnTranscriptReceived does NOT type/paste/copy
→ chunks collected in _sessionChunks

// Auto-correction enabled: AutoCorrectionRequested fires at session end
[AutoCorrection.Enabled = true, _sessionChunks = ["Hello ", "world"]]
→ StopRecording fires AutoCorrectionRequested("Hello world")

// Prompt key overrides auto-correction
[AutoCorrection.Enabled = true, _selectedPrompt = somePrompt]
→ StopRecording fires LlmProcessingRequested (existing path)
→ AutoCorrectionRequested does NOT fire

// Auto-correction disabled: live output as normal
[AutoCorrection.Enabled = false]
→ OnTranscriptReceived types/pastes/copies as before

// Provider fallback
[AutoCorrection.BaseUrl = "", Llm.BaseUrl = "https://api.openai.com/v1"]
→ uses Llm.BaseUrl/ApiKey/Model

// Own provider
[AutoCorrection.BaseUrl = "https://api.openai.com/v1", AutoCorrection.Model = "gpt-4o-mini"]
→ uses AutoCorrection config, ignores Llm config

// Style instructions appended
[StyleInstructions = "Use formal tone"]
→ system prompt = base_prompt + "\n\nUse formal tone"

// Empty recording: no API call
[_sessionChunks.Count = 0]
→ neither LlmProcessingRequested nor AutoCorrectionRequested fires
```

## Error Handling

If the LLM API call fails (network error, timeout, auth failure), the raw session text is output as-is (fallback to uncorrected transcription). A toast notification informs the user: `"AI correction failed — raw text used"`. This ensures dictated text is never lost due to an API issue.

## Non-Goals

- Streaming LLM output (batch only)
- Per-chunk correction (too expensive, poor quality without context)
- Auto-correction during prompt-key sessions (prompt key takes precedence)
- Visual diff between raw and corrected text
- Local/offline LLM support (e.g. Ollama) — uses same HTTP client pattern, works if endpoint is local
