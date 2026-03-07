# Design: Settings Migration to JSON

## Goal

Replace the custom `settings.txt` (line-based `key=value`) with a structured `settings.json` using `System.Text.Json`. Group related settings for readability.

## File Location

- New: `%LOCALAPPDATA%\VoiceDictation\settings.json`
- On first load: auto-migrate from `settings.txt` if it exists, then delete the old file

## JSON Structure (grouped)

```json
{
  "apiKey": "sk-...",
  "shortcuts": {
    "toggle": "Ctrl+Alt+D",
    "ptt": "Ctrl+Alt+Space",
    "aiTriggerKey": "shift"
  },
  "transcription": {
    "language": "de",
    "provider": "deepgram",
    "whisperModel": "tiny",
    "keywords": "keyword1, keyword2"
  },
  "audio": {
    "microphone": "Microphone (Realtek)",
    "tone": "Gentle",
    "vad": true
  },
  "llm": {
    "enabled": false,
    "apiKey": "",
    "baseUrl": "https://api.anthropic.com/v1",
    "model": "claude-sonnet-4-20250514",
    "prompt": "Fix grammar..."
  },
  "window": {
    "left": 100.0,
    "top": 200.0,
    "width": 500.0,
    "height": 600.0,
    "startMinimized": false
  }
}
```

## Implementation Approach

- New `SettingsService` class with a `Settings` POCO model (nested records for groups)
- `System.Text.Json` with `JsonSerializerOptions { WriteIndented = true }` for human-readable output
- `Load()` reads JSON, `Save()` writes JSON
- `MigrateFromTxt()` parses old `settings.txt` format into the new model (including tone name migration)
- `MainWindow` delegates to `SettingsService` instead of inline Load/Save logic

## Migration Flow

1. App starts -> `SettingsService.Load()`
2. If `settings.json` exists -> deserialize and return
3. Else if `settings.txt` exists -> parse old format, save as `.json`, delete `.txt`
4. Else -> return defaults

## What Changes

| File | Change |
|------|--------|
| New: `Services/SettingsService.cs` | Settings model + Load/Save/Migrate logic |
| `MainWindow.xaml.cs` | Replace inline Load/SaveSettings with `SettingsService` calls |
| `SoundFeedback.cs` | `MigrateName()` stays (still used during txt migration) |

## Tech

- `System.Text.Json` (built into .NET 8, no new dependencies)
- Serializer options: `WriteIndented = true`, `PropertyNamingPolicy = CamelCase`
