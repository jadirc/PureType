# Settings Dialog Design

## Goal

Extract all configurable settings from MainWindow into a dedicated SettingsWindow dialog. Add a gear-icon button to MainWindow to open it. Clean up the main window layout.

## What stays in MainWindow

- Status indicator + VU meter
- Connect button + Gear button (side by side)
- Transcript panel
- Provider combo (Deepgram / Whisper)
- Microphone combo

## What moves to SettingsWindow

All remaining settings, grouped with visual section headers:

### TRANSCRIPTION
- API Key (Deepgram only)
- Whisper Model + Download button (Whisper only)
- Keywords / Boost (Deepgram only)
- Language

### SHORTCUTS
- Toggle Shortcut
- PTT Shortcut

### AUDIO
- Signal Tone
- VAD Auto-stop

### AI POST-PROCESSING
- Enabled checkbox
- Trigger Key, API Endpoint, API Key, Model, System Prompt (collapsed when disabled)

### GENERAL
- Start with Windows
- Start minimized
- Text Replacements button

### Footer
- Save / Cancel buttons

## Data flow

1. MainWindow opens SettingsWindow with current `AppSettings` + current provider tag.
2. User edits settings in the dialog.
3. **Cancel**: dialog closes, nothing changes.
4. **Save**: dialog sets `DialogResult = true`, exposes new `AppSettings` via a property.
5. MainWindow applies the returned settings (register shortcuts, init tone, etc.) and persists via `SettingsService.Save()`.

## Shared styles

Extract Dark-Theme styles (ComboBox, TextBox, PasswordBox, CheckBox) from MainWindow.xaml into a shared `ResourceDictionary` at `Themes/DarkTheme.xaml`. Both MainWindow and SettingsWindow reference it via `MergedDictionaries`.

## Provider-dependent visibility

SettingsWindow receives the current provider tag ("deepgram" or "whisper") as a constructor parameter and shows/hides the relevant panels accordingly. If the provider changes in MainWindow while SettingsWindow is open, it is not updated live (user must reopen).
