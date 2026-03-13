# Clipboard AI Processing

**Date:** 2026-03-13
**Status:** Draft

## Problem

AI post-processing currently only works on freshly dictated text. Users want to apply LLM prompts to arbitrary text already in the clipboard — for example, fixing grammar in copied text, translating, or reformatting — without having to dictate it first.

## Requirements

- Configurable global shortcut triggers the feature (default: unset)
- Reads current clipboard text content
- Shows a prompt picker popup for the user to select a Named Prompt
- Sends clipboard text + selected prompt to the configured LLM
- Result output depends on modifier state at confirmation:
  - **No modifier:** Type/paste result at cursor position (same as dictation)
  - **Shift held on Enter:** Write result to clipboard only (toast confirmation)
- Reuses existing Named Prompts (no separate prompt list)
- Reuses existing LLM client infrastructure (Anthropic/OpenAI)

## Design

### 1. Shortcut Channel

**KeyboardHookService.cs** gets a 5th channel following the established pattern:

- Properties: `ClipboardAiModifiers` (`ModifierKeys`), `ClipboardAiKey` (`Key`)
- Event: `ClipboardAiPressed`
- Detection in `HookCallback` matching the `LanguageSwitchPressed` pattern

**SettingsService.cs** — `ShortcutSettings` gains:

```csharp
public string ClipboardAi { get; set; } = "";
```

### 2. PromptPickerWindow

New `PromptPickerWindow.xaml` / `.cs` — a lightweight, borderless popup:

- **Search field** at top (TextBox, auto-focused)
- **ListBox** below with Named Prompts filtered by search text
- **Keyboard control:** Arrow keys navigate, Enter confirms, Escape closes
- **Shift detection:** `bool ShiftHeld` property set on confirmation (checked via `Keyboard.Modifiers`)
- **Auto-close** on focus loss
- **Style:** No titlebar, no resize, borderless, centered on screen — command-palette aesthetic
- **Returns:** Selected `NamedPrompt` (or `null` on cancel)

The window contains no LLM logic — it is a pure prompt selector.

### 3. Processing Flow

When `ClipboardAiPressed` fires in MainWindow:

1. Read `Clipboard.GetText()` — if empty, show toast "Clipboard is empty" and abort
2. Check LLM is enabled + configured (API key, model) — if not, show toast and abort
3. Open `PromptPickerWindow` with the configured prompts list
4. User selects a prompt (or cancels → abort)
5. Call existing `ProcessWithLlmAsync(clipboardText, prompt.Prompt)`
6. On result:
   - If `ShiftHeld == false`: Type/paste at cursor position (existing `KeyboardInjector` path)
   - If `ShiftHeld == true`: `Clipboard.SetText(result)` + toast "Copied to clipboard"
7. Toast feedback: "Processing with {prompt.Name}..." at start, result feedback at end

### 4. Settings UI

**SettingsWindow.xaml** gets one additional shortcut row:

- Label: "Clipboard AI"
- TextBox with same shortcut recording mechanism as Toggle/PTT/Mute/LanguageSwitch
- Clear button

No other settings changes needed — LLM configuration and prompts are already managed.

## Files Changed

| File | Change |
|------|--------|
| `SettingsService.cs` | Add `ShortcutSettings.ClipboardAi` property |
| `KeyboardHookService.cs` | Add 5th channel: properties, event, detection logic |
| `MainWindow.xaml.cs` | Event handler: clipboard read, picker, LLM call, output |
| `SettingsWindow.xaml` | Add "Clipboard AI" shortcut row |
| `SettingsWindow.xaml.cs` | Wire shortcut recording for new row |
| `PromptPickerWindow.xaml` | New file: XAML layout for prompt picker |
| `PromptPickerWindow.xaml.cs` | New file: filtering, keyboard nav, shift detection |

## Files NOT Changed

- LLM clients (`ILlmClient`, `AnthropicLlmClient`, `OpenAiLlmClient`)
- `RecordingController.cs`
- `CodeFormatter.cs`
- `ReplacementService.cs`
- `KeyboardInjector.cs` (used as-is)

## Edge Cases

- **Empty clipboard:** Toast and abort
- **LLM not configured:** Toast and abort
- **No prompts configured:** Picker shows empty state with message, no action possible
- **LLM error/timeout:** Existing error handling in `ProcessWithLlmAsync` applies (toast with error)
- **Clipboard contains non-text (images, files):** `GetText()` returns empty string — handled by empty check
- **User is recording:** Shortcut should still work (clipboard AI is independent of recording state)
