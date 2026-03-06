# Quick Wins & Feature Roadmap Design

## Overview

Four phases of improvements to VoiceDictation: two quick-win UX enhancements, a custom replacements system, and LLM-based post-processing.

## Phase 1a: Enhanced Tray Context Menu

**Current state:** Tray menu has only "Open" and "Exit".

**New menu layout:**

```
 Status: Connected        (non-clickable, green/red text)
 ────────────────────
 Connect / Disconnect     (label toggles with state)
 Mute                     (checkmark when active)
 ────────────────────
 Open
 Exit
```

**Mute behavior:**

- Audio chunks are not forwarded to the provider; microphone stays active (VU meter continues).
- Status text shows "Muted" in yellow.
- Persists across recording sessions until user disables it.
- No dedicated shortcut; tray-only for now.

**Changes:** `MainWindow.xaml.cs` — extend `SetupTrayIcon()` and add mute state field.

## Phase 1b: Toast Overlay

**Window:** `ToastWindow.xaml` + `.cs` — borderless, topmost, non-focusable WPF window.

**Position:** Bottom-right of primary monitor, 16px margin from `SystemParameters.WorkArea` edges.

**Appearance:**

- Dark background (#1E1E2E, ~90% opacity), rounded corners, ~200x40px.
- Colored dot + text: "Recording started" (red) / "Recording stopped" (green).
- Catppuccin Mocha palette, consistent with main window.

**Behavior:**

- Appears on `StartRecording()` and `StopRecording()`.
- Fades out after 1.5 seconds via `DoubleAnimation` on Opacity.
- New toast replaces any still-visible toast immediately.
- Non-interactive, does not steal focus.

**Static entry point:** `ToastWindow.Show(string message, bool isRecording)`.

## Phase 2: Custom Replacements

### File format

Location: `%LOCALAPPDATA%\VoiceDictation\replacements.txt`

```
# Comments start with #
neue Zeile -> \n
Punkt -> .
Komma -> ,
mfg -> Mit freundlichen Grüßen
```

- Delimiter: ` -> ` or ` → `.
- `\n` becomes Enter keypress, `\t` becomes Tab.
- Case-insensitive matching.
- Applied in file order.

### ReplacementService

- Loads file on startup and watches for changes via `FileSystemWatcher`.
- `string Apply(string text)` — sequential string replacement.

### UI Editor (ReplacementsWindow)

- Opened via button in main window.
- List of rules with trigger + replacement columns.
- Add / Remove / Edit buttons.
- "Open file" button for power users.
- Saves back to `replacements.txt`.

### Pipeline integration

```
Transcript chunk (final) -> ReplacementService.Apply() -> KeyboardInjector.TypeTextAsync()
```

## Phase 3: LLM Post-Processing

### Configuration

New collapsible UI section "AI Post-Processing":

- Checkbox: Enable AI post-processing.
- Provider dropdown: OpenAI-compatible / Anthropic Claude.
- API Key field.
- Base URL field (pre-filled `https://api.openai.com/v1` for OpenAI-compatible).
- Model name text field.
- System prompt text field (sensible default provided).

### Flow

```
Recording stops
  -> Collect all final chunks from the session
  -> Send collected text to LLM
  -> Run LLM response through ReplacementService
  -> Output handling: TBD in Phase 3 (clipboard, preview window, or other)
```

### Implementation

- No SDK dependencies; plain `HttpClient` calls to REST APIs.
- `ILlmClient` interface with `OpenAiLlmClient` and `AnthropicLlmClient` implementations.
- Settings persisted in existing `settings.txt` (key=value style).

### Open question

How to replace the already-typed raw text with LLM output. To be resolved when Phase 3 implementation begins.

## New files summary

| Phase | New files |
|-------|-----------|
| 1a | — (changes to MainWindow.xaml.cs) |
| 1b | `ToastWindow.xaml`, `ToastWindow.xaml.cs` |
| 2 | `Services/ReplacementService.cs`, `ReplacementsWindow.xaml`, `ReplacementsWindow.xaml.cs` |
| 3 | `Services/ILlmClient.cs`, `Services/OpenAiLlmClient.cs`, `Services/AnthropicLlmClient.cs` + MainWindow UI |
