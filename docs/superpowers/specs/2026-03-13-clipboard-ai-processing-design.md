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
public string ClipboardAi { get; init; } = "";
```

Note: Uses `init` (not `set`) to match the existing record pattern in `ShortcutSettings`.

### 2. Hook Lifecycle Change

Currently the keyboard hook is installed on connect and uninstalled on disconnect. Clipboard AI must work independently of transcription connection state — it only requires an LLM.

**Change:** The keyboard hook must be installed at app startup and remain active regardless of connection state. The `ClipboardAiPressed` event fires at any time. The existing toggle/PTT/mute/language-switch events continue to be handled only when connected (their handlers already guard on connection state).

This means:
- `KeyboardHookService.Install()` moves from `ConnectAsync` to initialization
- `KeyboardHookService.Uninstall()` moves from `DisconnectAsync` to app shutdown
- Toggle/PTT handlers in `RecordingController` already check recording state, so no guard changes needed there

### 3. PromptPickerWindow

New `PromptPickerWindow.xaml` / `.cs` — a lightweight, borderless popup:

- **Search field** at top (TextBox, auto-focused)
- **ListBox** below with Named Prompts filtered by search text
- **Keyboard control:** Arrow keys navigate, Enter confirms, Escape closes
- **Shift detection:** `bool ShiftHeld` property set on confirmation (checked via `Keyboard.Modifiers`)
- **Auto-close** on focus loss
- **Style:** No titlebar, no resize, borderless, centered near cursor position (like VS Code command palette)
- **Returns:** Selected `NamedPrompt` (or `null` on cancel)
- **Empty state:** If no prompts are configured, shows a message "No prompts configured" and waits for Escape

The window contains no LLM logic — it is a pure prompt selector.

### 4. Processing Flow

When `ClipboardAiPressed` fires in MainWindow:

1. `Dispatcher.Invoke` to ensure UI thread (hook fires from background thread)
2. Read `Clipboard.GetText()` — if empty, show toast "Clipboard is empty" and abort
3. Check LLM is enabled + configured (API key, model) — if not, show toast "LLM not configured" and abort
4. Open `PromptPickerWindow` with the configured prompts list (`ShowDialog()`)
5. User selects a prompt (or cancels → abort)
6. Toast "Processing with {prompt.Name}..."
7. Call `ProcessWithLlmAsync(clipboardText, prompt.Prompt, shiftHeld)` — see output routing below
8. After window closes, `await Task.Delay(200)` before typing to allow OS to restore focus to the original window
9. Result feedback toast at end

### 5. Output Routing — ProcessWithLlmAsync Extension

The existing `ProcessWithLlmAsync` is `async Task` and always types the result. To support the clipboard-only path:

**Add a `bool clipboardOnly = false` parameter** to `ProcessWithLlmAsync`. The existing call site in the `LlmProcessingRequested` handler passes the default (`false`). The new clipboard AI handler passes `picker.ShiftHeld`.

Inside `ProcessWithLlmAsync`:
- If `clipboardOnly == true`: `Clipboard.SetText(result)` + toast "Copied to clipboard"
- If `clipboardOnly == false`: existing `KeyboardInjector.TypeTextAsync(result)` path

Text replacements (`_replacements.Apply`) are applied in both output modes — the LLM result is always post-processed consistently.

### 6. Settings UI

**SettingsWindow.xaml** gets one additional shortcut row:

- Label: "Clipboard AI"
- TextBox with same shortcut recording mechanism as Toggle/PTT/Mute/LanguageSwitch
- Clear button

**SettingsWindow.xaml.cs** specific updates:

- Add `ClipboardAiShortcutBox` to the collision-check array in `AssignShortcut`
- Add `ClipboardAiShortcutBox` to the Win+modifier check in `OnRecordingWinPlusModifier`
- Add `ClipboardAi` to `ShortcutSettings` construction in `Save_Click`
- Wire `GotFocus`/`LostFocus`/`PreviewKeyDown` events for the new shortcut box

**MainWindow.xaml.cs** specific updates:

- Add `_clipboardAiKey`/`_clipboardAiModifiers` private fields
- Parse shortcut in `LoadSettings` with the same `if (!string.IsNullOrEmpty(...))` guard
- Apply to `KeyboardHookService` in `ApplySettings` (with `else` branch to reset to `Key.None`)
- Subscribe to `ClipboardAiPressed` event

No other settings changes needed — LLM configuration and prompts are already managed.

## Files Changed

| File | Change |
|------|--------|
| `SettingsService.cs` | Add `ShortcutSettings.ClipboardAi` property (`init`) |
| `KeyboardHookService.cs` | Add 5th channel: properties, event, detection logic |
| `MainWindow.xaml.cs` | Hook lifecycle change (install at startup); event handler: clipboard read, picker, LLM call, output; extend `ProcessWithLlmAsync` with `clipboardOnly` param; update `LoadSettings`/`ApplySettings` |
| `SettingsWindow.xaml` | Add "Clipboard AI" shortcut row |
| `SettingsWindow.xaml.cs` | Wire shortcut recording; update `AssignShortcut`, `OnRecordingWinPlusModifier`, `Save_Click` |
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
- **LLM not configured:** Toast "LLM not configured" and abort
- **No prompts configured:** Picker shows "No prompts configured" message, waits for Escape
- **LLM error/timeout:** Existing error handling in `ProcessWithLlmAsync` applies (toast with error)
- **Clipboard contains non-text (images, files):** `GetText()` returns empty string — handled by empty check
- **User is recording:** Shortcut still works (clipboard AI is independent of recording state)
- **App not connected to transcription:** Shortcut still works (hook is always installed, LLM is independent)
- **Focus restore after picker closes:** 200ms delay before `TypeTextAsync` to allow OS to return focus to original window
- **Thread safety:** `Clipboard.GetText()` must run on UI thread — handler uses `Dispatcher.Invoke`
