# Text Cleanup Pipeline Design

**Date:** 2026-04-09
**Status:** Approved
**Motivation:** Bridge the gap between raw Whisper transcription and full LLM enhancement. Provide a lightweight, local, always-fast text cleanup that removes filler words, duplicate words, and normalizes whitespace — without requiring an LLM API call.

## Context

Competitor analysis (Voicely.de) revealed that automatic text cleanup is a key differentiator: users expect dictated text to arrive clean — no "ähm", no stuttered duplicates, no messy spacing. PureType currently offers raw transcription (fast, free) or full LLM post-processing (powerful, but requires API key, costs money, adds latency). There is nothing in between.

## Design

### Two-Stage Enhancement Pipeline

```
Stage 1 (local, instant):  TextCleanupService — filler removal, dedup, whitespace
Stage 2 (optional, LLM):   Existing LLM post-processing — style, tone, rewriting
```

Stage 1 is opt-in (default off). Stage 2 remains unchanged (triggered by hotkey during recording).

### Pipeline Position

In `RecordingController.OnTranscriptReceived`, after all existing transformations:

```
Whisper → ReplacementService → AutoCapitalize → CodeFormatter → TextCleanupService → Output
```

When LLM mode is active (`_selectedPrompt != null`), the cleanup still runs on chunks collected in `_sessionChunks`, so the LLM receives pre-cleaned input.

## TextCleanupService

### API

```csharp
public class TextCleanupService
{
    public bool Enabled { get; set; }
    public void SetLanguage(string langCode);
    public string Process(string text);
}
```

### Processing Steps (in order)

1. **Remove filler words** — language-specific, uses loaded JSON data
2. **Remove duplicate words** — language-independent (with per-language allowlist)
3. **Normalize whitespace** — language-independent

### Filler Word Categories

The JSON data distinguishes two categories:

- **Unconditional fillers** (`fillerWords`): Always removed. These are never meaningful words in dictation context (e.g., "ähm", "äh", "hm").
- **Conditional fillers** (`conditionalFillers`): Removed only in specific positions.
  - `ANY` — always removed (words that are almost never meaningful in dictation: "quasi", "sozusagen", "halt")
  - `START_OF_SENTENCE` — removed only at sentence start (e.g., "also", "na ja")

Matching uses word boundaries to avoid partial matches (e.g., "halt" should not affect "halten").

### Duplicate Word Detection

If two identical words appear consecutively (case-insensitive), the duplicate is removed. Per-language allowlist prevents removing intentional duplicates (e.g., "sehr sehr gut" in German).

### Whitespace Normalization

- Collapse multiple spaces to single space
- Remove space before punctuation (`. , ! ? : ;`)
- Trim leading/trailing whitespace

## Language Data Files

Embedded resources at `Resources/TextCleanup/{langCode}.json`:

### German (`de.json`)

```json
{
  "fillerWords": ["ähm", "äh", "hm", "mhm", "hmm"],
  "conditionalFillers": {
    "also": "START_OF_SENTENCE",
    "halt": "ANY",
    "quasi": "ANY",
    "sozusagen": "ANY",
    "irgendwie": "ANY",
    "na ja": "START_OF_SENTENCE",
    "sag ich mal": "ANY",
    "ich meine": "START_OF_SENTENCE",
    "wie gesagt": "START_OF_SENTENCE"
  },
  "allowedDuplicates": ["sehr", "ganz", "weit"]
}
```

### English (`en.json`)

```json
{
  "fillerWords": ["um", "uh", "hmm", "hm", "mhm"],
  "conditionalFillers": {
    "like": "START_OF_SENTENCE",
    "you know": "ANY",
    "i mean": "START_OF_SENTENCE",
    "basically": "ANY",
    "actually": "START_OF_SENTENCE",
    "kind of": "ANY",
    "sort of": "ANY"
  },
  "allowedDuplicates": ["very", "really"]
}
```

### Adding a New Language

1. Create `Resources/TextCleanup/{langCode}.json` with the same schema
2. Mark it as embedded resource in `.csproj`
3. No code changes required

If no JSON file exists for the active language, only language-independent rules run (duplicates, whitespace).

## Settings

### Data Model

New property in `AudioSettings`:

```csharp
public bool TextCleanup { get; init; } = false;
```

### UI

Toggle checkbox in Settings window, in the Audio section near the existing "Auto-capitalize first letter" toggle:

```
☐ Text cleanup (remove filler words, duplicates)
```

No additional configuration UI for word lists.

## Language Synchronization

When the user switches language via the Language Quick-Switch Hotkey:
1. `WhisperService.SetLanguageAsync(lang)` is called (existing)
2. `TextCleanupService.SetLanguage(lang)` is called (new) — loads the matching JSON file

The language code comes from the same setting that WhisperService uses.

## File Changes

### New Files

| File | Purpose |
|------|---------|
| `src/PureType/Services/TextCleanupService.cs` | Pipeline logic |
| `src/PureType/Resources/TextCleanup/de.json` | German filler words & rules |
| `src/PureType/Resources/TextCleanup/en.json` | English filler words & rules |
| `tests/PureType.Tests/Services/TextCleanupServiceTests.cs` | Unit tests |

### Modified Files

| File | Change |
|------|--------|
| `src/PureType/Services/SettingsService.cs` | Add `TextCleanup` to `AudioSettings` |
| `src/PureType/Services/RecordingController.cs` | Wire `TextCleanupService` into pipeline |
| `src/PureType/MainWindow.xaml.cs` | Instantiate service, sync language |
| `src/PureType/SettingsWindow.xaml` | Add toggle checkbox |
| `src/PureType/SettingsWindow.xaml.cs` | Bind toggle to setting |
| `src/PureType/PureType.csproj` | Embed JSON resources |

## Test Cases

```csharp
// German filler removal
"ähm ich habe das gemacht" → "ich habe das gemacht"

// Conditional filler at start of sentence
"Also, das ist klar." → "Das ist klar."

// Conditional filler mid-sentence preserved
"wir müssen also weitermachen" → "wir müssen also weitermachen"

// Duplicate removal
"ich ich habe" → "ich habe"

// Allowed duplicate preserved
"das war sehr sehr gut" → "das war sehr sehr gut"

// Whitespace normalization
"Hallo  Welt ." → "Hallo Welt."

// English fillers
"um I think basically we should" → "I think we should"

// Unknown language: only language-independent rules
"je suis suis content" → "je suis content"

// Disabled: passthrough
[TextCleanup = false] "ähm ich ich habe" → "ähm ich ich habe"
```

## Non-Goals

- Punctuation correction (Whisper already provides punctuation)
- Grammar correction (reserved for LLM stage)
- Style/tone adjustment (reserved for LLM stage)
- User-editable word lists in UI (power users can override embedded JSON files)
