# Settings Dialog Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extract all configurable settings from MainWindow into a dedicated SettingsWindow dialog with grouped sections, Save/Cancel flow, and a gear button in MainWindow.

**Architecture:** SettingsWindow is a modal WPF dialog that receives the current `AppSettings` + provider tag, lets the user edit settings, and returns updated `AppSettings` on Save. Shared dark-theme styles live in `Themes/DarkTheme.xaml` as a merged `ResourceDictionary`. MainWindow reads runtime values from `_settings` fields instead of UI controls.

**Tech Stack:** WPF (.NET 8), XAML ResourceDictionary, Segoe MDL2 Assets (gear icon)

---

## What stays in MainWindow

- Status indicator + VU meter
- Connect button + Gear button (side by side)
- Transcript panel
- Provider combo (Deepgram / Whisper)
- Microphone combo

## What moves to SettingsWindow

Grouped with visual section headers and horizontal rules:

- **TRANSCRIPTION** — API Key (Deepgram only), Whisper Model + Download (Whisper only), Keywords (Deepgram only), Language
- **SHORTCUTS** — Toggle Shortcut, PTT Shortcut
- **AUDIO** — Signal Tone, VAD Auto-stop
- **AI POST-PROCESSING** — Enabled checkbox, Trigger Key, Endpoint, API Key, Model, System Prompt (collapsed when disabled)
- **GENERAL** — Start with Windows, Start minimized, Text Replacements button
- **Footer** — Save / Cancel buttons

---

### Task 1: Extract shared dark-theme styles to ResourceDictionary

**Files:**
- Create: `src/VoiceDictation/Themes/DarkTheme.xaml`
- Modify: `src/VoiceDictation/MainWindow.xaml` (remove `<Window.Resources>` styles)
- Modify: `src/VoiceDictation/App.xaml` (merge DarkTheme.xaml)

**Step 1: Create `Themes/DarkTheme.xaml`**

Move all styles from `MainWindow.xaml` `<Window.Resources>` (lines 16-193) into a new `ResourceDictionary`. These are: `DarkComboBoxToggleButton` ControlTemplate, ComboBox style, ComboBoxItem style, TextBox style, PasswordBox style, CheckBox style.

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Paste all styles from MainWindow.xaml Window.Resources here (lines 17-193) -->

</ResourceDictionary>
```

**Step 2: Merge in App.xaml**

Add `MergedDictionaries` to `App.xaml` `Application.Resources`:

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Themes/DarkTheme.xaml"/>
        </ResourceDictionary.MergedDictionaries>

        <!-- Keep existing Button style here -->
    </ResourceDictionary>
</Application.Resources>
```

**Step 3: Remove `<Window.Resources>` from MainWindow.xaml**

Delete the entire `<Window.Resources>...</Window.Resources>` block (lines 16-194).

**Step 4: Build**

Run: `dotnet build`
Expected: Build succeeds — styles now resolve from App-level merged dictionary.

**Step 5: Commit**

```bash
git add src/VoiceDictation/Themes/DarkTheme.xaml src/VoiceDictation/MainWindow.xaml src/VoiceDictation/App.xaml
git commit -m "refactor: extract dark theme styles to shared ResourceDictionary"
```

---

### Task 2: Create SettingsWindow XAML

**Files:**
- Create: `src/VoiceDictation/SettingsWindow.xaml`

**Step 1: Create the XAML file**

The window uses same dark theme (`Background="#1E1E2E"`), `WindowStartupLocation="CenterOwner"`, modal dialog pattern. Content is a `ScrollViewer` with grouped `StackPanel` sections separated by section header `TextBlock`s with style `Foreground="#6C7086"`.

Key elements (all `x:Name`s listed — these are needed by code-behind):

**TRANSCRIPTION section:**
- `ApiKeyPanel` (StackPanel, Visibility depends on provider)
- `ApiKeyBox` (PasswordBox)
- `WhisperModelPanel` (StackPanel, Visibility depends on provider)
- `WhisperModelCombo` (ComboBox)
- `DownloadModelButton` (Button)
- `DownloadProgress` (ProgressBar)
- `KeywordsPanel` (StackPanel, Visibility depends on provider)
- `KeywordsBox` (TextBox)
- `LanguageCombo` (ComboBox with de/en/auto items)

**SHORTCUTS section:**
- `ToggleShortcutBox` (TextBox, IsReadOnly, same recorder pattern)
- `PttShortcutBox` (TextBox, IsReadOnly, same recorder pattern)

**AUDIO section:**
- `ToneCombo` (ComboBox with None/Gentle/Click/Bell/Deep/Double-Pip)
- `VadCheck` (CheckBox)

**AI POST-PROCESSING section:**
- `LlmEnabledCheck` (CheckBox)
- `LlmSettingsPanel` (StackPanel, collapsed when unchecked)
- `AiTriggerKeyCombo` (ComboBox)
- `LlmBaseUrlCombo` (ComboBox, IsEditable)
- `LlmApiKeyBox` (PasswordBox)
- `LlmModelCombo` (ComboBox, IsEditable)
- `FetchModelsButton` (Button)
- `LlmPromptBox` (TextBox, multiline)

**GENERAL section:**
- `AutostartCheck` (CheckBox)
- `StartMinimizedCheck` (CheckBox)
- Text Replacements button

**Footer:**
- Save button (`IsDefault="True"`, blue `#89B4FA`)
- Cancel button (`IsCancel="True"`, transparent)

Section headers use this pattern:
```xml
<TextBlock Text="── TRANSCRIPTION ──" Foreground="#6C7086"
           FontSize="11" FontWeight="SemiBold" Margin="0,4,0,8"/>
```

**Step 2: Build**

Run: `dotnet build`
Expected: Build succeeds (no code-behind yet, but XAML should compile with empty partial class).

---

### Task 3: Create SettingsWindow code-behind

**Files:**
- Create: `src/VoiceDictation/SettingsWindow.xaml.cs`

**Step 1: Write the code-behind**

Constructor signature:
```csharp
public SettingsWindow(AppSettings settings, string providerTag,
                      KeyboardHookService keyboardHook,
                      ReplacementService replacements)
```

Key responsibilities:
- **Constructor**: Populate all controls from `settings`, set provider-dependent visibility, populate Whisper models, subscribe to `keyboardHook.RecordingWinPlusModifier`.
- **`ResultSettings` property** (`AppSettings`): Built from current control values when Save is clicked.
- **`LlmEnabledCheck_Changed`**: Toggle `LlmSettingsPanel` visibility.
- **Shortcut recorder**: Copy `ShortcutBox_GotFocus`, `ShortcutBox_LostFocus`, `ShortcutBox_PreviewKeyDown` logic from MainWindow. These only record text — no live hook registration. The `RecordingWinPlusModifier` handler captures Win+modifier combos and populates the focused shortcut box.
- **`DownloadModelButton_Click`**: Same download logic as MainWindow.
- **`FetchModelsButton_Click`**: Same fetch-models logic (uses static `FetchModelsAsync` extracted from MainWindow or duplicated).
- **`Save_Click`**: Build `ResultSettings` from all controls, set `DialogResult = true`.
- **`OnClosing`**: Unsubscribe from `keyboardHook.RecordingWinPlusModifier`, reset `SuppressWinKey`.
- **Text Replacements button**: Opens `ReplacementsWindow` (receives `ReplacementService`).

Important: The shortcut recorder stores parsed `Key` / `ModifierKeys` in local fields (like MainWindow does), but does NOT call `_keyboardHook.SetToggleShortcut()` — that happens in MainWindow after Save.

Helper methods to copy/share from MainWindow:
- `FormatShortcut(ModifierKeys, Key)` → extract to a `static` method or `internal` utility
- `ParseShortcut(string, Key)` → same
- `SelectComboByTag(ComboBox, string)` → same
- `FetchModelsAsync(string, string)` → already static, extract to a shared location or keep in MainWindow as `internal static`

**Step 2: Build**

Run: `dotnet build`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add src/VoiceDictation/SettingsWindow.xaml src/VoiceDictation/SettingsWindow.xaml.cs
git commit -m "feat: add SettingsWindow with grouped sections and Save/Cancel"
```

---

### Task 4: Update MainWindow — remove settings UI, add gear button

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml`
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

**Step 1: Simplify MainWindow.xaml**

Replace the grid layout. New structure:

```
Row 0: Status indicator + VU meter
Row 1: spacing (8px)
Row 2: Connect button + Gear button (side by side in a Grid with 2 columns)
Row 3: spacing (8px)
Row 4: Provider combo
Row 5: spacing (8px)
Row 6: Microphone combo
Row 7: spacing (8px)
Row 8: Transcript panel (fills remaining space, Height="*")
```

The gear button uses `Segoe MDL2 Assets` font, glyph `&#xE713;` (Settings gear). Place it next to the Connect button:

```xml
<Grid Grid.Row="2">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <Button Grid.Column="0" x:Name="ConnectButton" Content="Connect"
            Click="ConnectButton_Click"
            Background="#89B4FA" Foreground="#1E1E2E"
            FontWeight="SemiBold" FontSize="14"
            BorderThickness="0" Padding="0,11" Cursor="Hand"/>
    <Button Grid.Column="1" x:Name="SettingsButton" Content="&#xE713;"
            Click="SettingsButton_Click"
            FontFamily="Segoe MDL2 Assets" FontSize="16"
            Background="#313244" Foreground="#CDD6F4"
            BorderThickness="0" Padding="12,11" Margin="8,0,0,0"
            Cursor="Hand" ToolTip="Settings"/>
</Grid>
```

Remove everything in the old settings `ScrollViewer` (Row 6 in old layout): Provider sub-panels (WhisperModelPanel, ApiKeyPanel, KeywordsPanel), Language, shortcuts, tone, checkboxes, LLM panel. Keep only Provider combo and Microphone combo as standalone sections.

Remove the Transcript collapsible behavior (no longer needed since settings don't compete for space).

**Step 2: Update MainWindow.xaml.cs**

Changes needed:

1. **Add `SettingsButton_Click` handler:**
```csharp
private void SettingsButton_Click(object sender, RoutedEventArgs e)
{
    var providerItem = ProviderCombo.SelectedItem as ComboBoxItem;
    var providerTag = (string)(providerItem?.Tag ?? "deepgram");

    var dialog = new SettingsWindow(_settings, providerTag, _keyboardHook, _replacements);
    dialog.Owner = this;

    if (dialog.ShowDialog() == true)
    {
        _settings = dialog.ResultSettings;
        ApplySettings();
        _settingsService.Save(_settings);
    }
}
```

2. **Add `ApplySettings()` method** that applies `_settings` to runtime state:
   - Parse and register shortcuts (`_toggleKey`, `_toggleModifiers`, `_pttKey`, `_pttModifiers` + hook updates if connected)
   - Apply AI trigger key
   - Init `SoundFeedback` with new tone
   - Update VAD, autostart registry, window settings
   - No need to update UI controls (they're in the dialog now)

3. **Update `ConnectAsync()`** to read from `_settings` instead of UI controls:
   - `_settings.Transcription.Language` instead of `LanguageCombo.SelectedItem`
   - `_settings.Transcription.ApiKey` instead of `ApiKeyBox.Password`
   - `_settings.Transcription.Keywords` instead of `KeywordsBox.Text`
   - `_settings.Transcription.WhisperModel` instead of `WhisperModelCombo.SelectedItem`

4. **Update `StartRecording()`**: Read `_settings.Audio.Vad` instead of `VadCheck.IsChecked`.

5. **Update toggle/PTT handlers**: Read `_settings.Llm.Enabled` instead of `LlmEnabledCheck.IsChecked`.

6. **Update `ProcessWithLlmAsync()`**: Read LLM settings from `_settings.Llm.*` instead of UI controls.

7. **Update `SaveSettings()`**: Simplify — only needs to capture Provider, Microphone, and Window position from UI controls. Everything else comes from `_settings` fields (already up to date from dialog).

8. **Remove now-unused event handlers:**
   - `ApiKeyBox_PasswordChanged`
   - `LanguageCombo_SelectionChanged`
   - `ToneCombo_SelectionChanged`
   - `KeywordsBox_LostFocus`
   - `VadCheck_Changed`
   - `AutostartCheck_Changed`
   - `StartMinimizedCheck_Changed`
   - `LlmEnabledCheck_Changed`
   - `LlmBaseUrlCombo_SelectionChanged`
   - `LlmApiKeyBox_PasswordChanged`
   - `LlmSettingChanged`
   - `AiTriggerKeyCombo_SelectionChanged`
   - `ShortcutBox_GotFocus`, `ShortcutBox_LostFocus`, `ShortcutBox_PreviewKeyDown`
   - `OnRecordingWinPlusModifier`
   - `ReplacementsButton_Click`
   - `TranscriptHeader_Click`, `_transcriptCollapsed`

9. **Remove `DownloadModelButton_Click`, `FetchModelsButton_Click`, `FetchModelsAsync`** from MainWindow (moved to SettingsWindow). If `FetchModelsAsync` is shared, make it `internal static` in a utility class or keep it in SettingsWindow.

10. **Simplify `LoadSettings()`**: Only applies Provider, Microphone, Window position/size, StartMinimized from `_settings`. Shortcuts and other settings are parsed but not loaded into UI controls (they don't exist in MainWindow anymore).

11. **Update `ProviderCombo_SelectionChanged`**: Remove toggling of WhisperModelPanel/ApiKeyPanel/KeywordsPanel visibility (those panels are gone). Just save settings.

**Step 3: Build and run**

Run: `dotnet build`
Expected: Build succeeds with no warnings about missing event handlers.

Run: `dotnet run --project src/VoiceDictation`
Expected: MainWindow shows compact layout. Gear button opens SettingsWindow. Save/Cancel work correctly.

**Step 4: Commit**

```bash
git add src/VoiceDictation/MainWindow.xaml src/VoiceDictation/MainWindow.xaml.cs
git commit -m "refactor: move settings to SettingsWindow, add gear button to MainWindow"
```

---

### Task 5: Final cleanup and verification

**Files:**
- Possibly: `src/VoiceDictation/MainWindow.xaml.cs` (remove dead code)
- Possibly: `tests/VoiceDictation.Tests/Services/SettingsServiceTests.cs` (verify still passing)

**Step 1: Run tests**

Run: `dotnet test`
Expected: All existing tests pass (SettingsService tests are independent of UI).

**Step 2: Manual smoke test**

1. Launch app → compact MainWindow with gear button
2. Click gear → SettingsWindow opens centered on MainWindow
3. Change a shortcut → press Save → verify shortcut works
4. Change signal tone → press Save → verify tone plays on next recording
5. Open settings, change something, press Cancel → verify nothing changed
6. Verify provider-dependent panels show correctly (switch provider in MainWindow, reopen settings)
7. Verify Text Replacements button opens ReplacementsWindow from within SettingsWindow
8. Verify AI settings collapse/expand with checkbox
9. Close and reopen app → verify all settings persisted

**Step 3: Remove any dead code**

Check for unused fields, methods, or `using` statements in MainWindow.xaml.cs. Remove `_shortcutBoxPreviousText` and other fields that only served the moved UI.

**Step 4: Commit**

```bash
git add -A
git commit -m "chore: final cleanup after settings dialog extraction"
```
