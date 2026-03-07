# JSON Settings Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the custom `settings.txt` with a structured `settings.json` using grouped sections and `System.Text.Json`.

**Architecture:** A new `SettingsService` class owns a `Settings` POCO model with nested records for each group (shortcuts, transcription, audio, llm, window). `MainWindow` reads/writes the model instead of doing inline parsing. On first load, the old `settings.txt` is auto-migrated and deleted.

**Tech Stack:** .NET 8, System.Text.Json, xUnit

---

### Task 1: Create the Settings model and SettingsService

**Files:**
- Create: `src/VoiceDictation/Services/SettingsService.cs`

**Step 1: Create the Settings model and service**

The model uses nested records matching the agreed JSON structure. The service handles Load, Save, and migration from old txt format.

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceDictation.Services;

public record ShortcutSettings
{
    public string Toggle { get; set; } = "Ctrl+Alt+X";
    public string Ptt { get; set; } = "Win+L-Ctrl";
    public string AiTriggerKey { get; set; } = "shift";
}

public record TranscriptionSettings
{
    public string Language { get; set; } = "de";
    public string Provider { get; set; } = "deepgram";
    public string WhisperModel { get; set; } = "tiny";
    public string Keywords { get; set; } = "";
}

public record AudioSettings
{
    public string Microphone { get; set; } = "";
    public string Tone { get; set; } = "Gentle";
    public bool Vad { get; set; }
}

public record LlmSettings
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string Prompt { get; set; } = "";
}

public record WindowSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool StartMinimized { get; set; }
}

public record AppSettings
{
    public string ApiKey { get; set; } = "";
    public ShortcutSettings Shortcuts { get; set; } = new();
    public TranscriptionSettings Transcription { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public LlmSettings Llm { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
}

public static class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "VoiceDictation");

    private static readonly string JsonPath = Path.Combine(SettingsDir, "settings.json");
    private static readonly string LegacyTxtPath = Path.Combine(SettingsDir, "settings.txt");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppSettings Load()
    {
        if (File.Exists(JsonPath))
        {
            var json = File.ReadAllText(JsonPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }

        if (File.Exists(LegacyTxtPath))
        {
            var settings = MigrateFromTxt(File.ReadAllLines(LegacyTxtPath));
            Save(settings);
            File.Delete(LegacyTxtPath);
            return settings;
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(JsonPath, json);
    }

    internal static AppSettings MigrateFromTxt(string[] lines)
    {
        var s = new AppSettings();

        if (lines.Length > 0)
            s.ApiKey = lines[0].Trim();

        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "toggle":
                    s.Shortcuts.Toggle = value;
                    break;
                case "ptt":
                    s.Shortcuts.Ptt = value;
                    break;
                case "ai_trigger_key":
                    s.Shortcuts.AiTriggerKey = value;
                    break;
                case "language":
                    s.Transcription.Language = value;
                    break;
                case "provider":
                    s.Transcription.Provider = value;
                    break;
                case "whisper_model":
                    s.Transcription.WhisperModel = value;
                    break;
                case "keywords":
                    s.Transcription.Keywords = value;
                    break;
                case "microphone":
                    s.Audio.Microphone = value;
                    break;
                case "tone":
                    s.Audio.Tone = SoundFeedback.MigrateName(value);
                    break;
                case "vad":
                    s.Audio.Vad = value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    break;
                case "llm_enabled":
                    s.Llm.Enabled = value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    break;
                case "llm_apikey":
                    s.Llm.ApiKey = value;
                    break;
                case "llm_baseurl":
                    s.Llm.BaseUrl = value;
                    break;
                case "llm_provider":
                    // Legacy: migrate anthropic provider to base URL
                    if (value == "anthropic")
                        s.Llm.BaseUrl = "https://api.anthropic.com/v1";
                    break;
                case "llm_model":
                    s.Llm.Model = value;
                    break;
                case "llm_prompt":
                    s.Llm.Prompt = value.Replace("\\n", "\n");
                    break;
                case "left":
                    if (double.TryParse(value, out var left)) s.Window.Left = left;
                    break;
                case "top":
                    if (double.TryParse(value, out var top)) s.Window.Top = top;
                    break;
                case "width":
                    if (double.TryParse(value, out var w)) s.Window.Width = w;
                    break;
                case "height":
                    if (double.TryParse(value, out var h)) s.Window.Height = h;
                    break;
                case "start_minimized":
                    s.Window.StartMinimized = value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return s;
    }
}
```

**Step 2: Verify build**

```bash
dotnet build
```

Expected: 0 errors.

**Step 3: Commit**

```bash
git add src/VoiceDictation/Services/SettingsService.cs
git commit -m "feat: add SettingsService with JSON load/save and txt migration"
```

---

### Task 2: Add tests for SettingsService

**Files:**
- Create: `tests/VoiceDictation.Tests/Services/SettingsServiceTests.cs`

**Step 1: Write migration and round-trip tests**

```csharp
using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public void MigrateFromTxt_parses_all_fields()
    {
        var lines = new[]
        {
            "my-api-key",
            "toggle=Ctrl+Alt+D",
            "ptt=Win+L-Ctrl",
            "language=en",
            "tone=Sanft",
            "microphone=Test Mic",
            "keywords=hello, world",
            "provider=whisper",
            "whisper_model=base",
            "vad=True",
            "start_minimized=True",
            "llm_enabled=True",
            "llm_apikey=llm-key",
            "llm_baseurl=https://example.com",
            "llm_model=gpt-4",
            "llm_prompt=Fix this\\nplease",
            "ai_trigger_key=ctrl",
            "left=100",
            "top=200",
            "width=500",
            "height=600"
        };

        var s = SettingsService.MigrateFromTxt(lines);

        Assert.Equal("my-api-key", s.ApiKey);
        Assert.Equal("Ctrl+Alt+D", s.Shortcuts.Toggle);
        Assert.Equal("Win+L-Ctrl", s.Shortcuts.Ptt);
        Assert.Equal("ctrl", s.Shortcuts.AiTriggerKey);
        Assert.Equal("en", s.Transcription.Language);
        Assert.Equal("whisper", s.Transcription.Provider);
        Assert.Equal("base", s.Transcription.WhisperModel);
        Assert.Equal("hello, world", s.Transcription.Keywords);
        Assert.Equal("Test Mic", s.Audio.Microphone);
        Assert.Equal("Gentle", s.Audio.Tone); // Sanft migrated to Gentle
        Assert.True(s.Audio.Vad);
        Assert.True(s.Llm.Enabled);
        Assert.Equal("llm-key", s.Llm.ApiKey);
        Assert.Equal("https://example.com", s.Llm.BaseUrl);
        Assert.Equal("gpt-4", s.Llm.Model);
        Assert.Equal("Fix this\nplease", s.Llm.Prompt);
        Assert.Equal(100.0, s.Window.Left);
        Assert.Equal(200.0, s.Window.Top);
        Assert.Equal(500.0, s.Window.Width);
        Assert.Equal(600.0, s.Window.Height);
        Assert.True(s.Window.StartMinimized);
    }

    [Fact]
    public void MigrateFromTxt_empty_file_returns_defaults()
    {
        var s = SettingsService.MigrateFromTxt([]);

        Assert.Equal("", s.ApiKey);
        Assert.Equal("Gentle", s.Audio.Tone);
        Assert.Equal("de", s.Transcription.Language);
        Assert.False(s.Audio.Vad);
    }

    [Fact]
    public void MigrateFromTxt_legacy_anthropic_provider_sets_baseurl()
    {
        var lines = new[]
        {
            "key",
            "llm_provider=anthropic"
        };

        var s = SettingsService.MigrateFromTxt(lines);

        Assert.Equal("https://api.anthropic.com/v1", s.Llm.BaseUrl);
    }

    [Fact]
    public void Default_settings_have_expected_values()
    {
        var s = new AppSettings();

        Assert.Equal("Ctrl+Alt+X", s.Shortcuts.Toggle);
        Assert.Equal("Win+L-Ctrl", s.Shortcuts.Ptt);
        Assert.Equal("de", s.Transcription.Language);
        Assert.Equal("deepgram", s.Transcription.Provider);
        Assert.Equal("Gentle", s.Audio.Tone);
        Assert.False(s.Llm.Enabled);
        Assert.Null(s.Window.Left);
    }
}
```

**Step 2: Run tests**

```bash
dotnet test
```

Expected: all tests pass.

**Step 3: Commit**

```bash
git add tests/VoiceDictation.Tests/Services/SettingsServiceTests.cs
git commit -m "test: add SettingsService migration and defaults tests"
```

---

### Task 3: Wire MainWindow to SettingsService

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

This is the largest task. Replace the inline `LoadSettings()` and `SaveSettings()` methods with calls to `SettingsService`. The key changes:

1. Remove the `SettingsPath` field
2. Add an `_settings` field of type `AppSettings`
3. Rewrite `LoadSettings()` to read from `_settings` model
4. Rewrite `SaveSettings()` to populate `_settings` model and call `SettingsService.Save()`

**Step 1: Replace SettingsPath with _settings field**

Replace:
```csharp
private static readonly string SettingsPath =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 "VoiceDictation", "settings.txt");
```

With:
```csharp
private AppSettings _settings = new();
```

**Step 2: Rewrite LoadSettings()**

Replace the entire `LoadSettings()` method with:

```csharp
private void LoadSettings()
{
    _settings = SettingsService.Load();

    ApiKeyBox.Password = _settings.ApiKey;

    ParseToggleShortcut(_settings.Shortcuts.Toggle);
    ParsePttShortcut(_settings.Shortcuts.Ptt);
    SelectComboByTag(AiTriggerKeyCombo, _settings.Shortcuts.AiTriggerKey);

    SelectComboByTag(LanguageCombo, _settings.Transcription.Language);
    SelectComboByTag(ProviderCombo, _settings.Transcription.Provider);
    _savedWhisperModel = _settings.Transcription.WhisperModel;
    KeywordsBox.Text = _settings.Transcription.Keywords;

    _savedMicrophoneDevice = _settings.Audio.Microphone;
    SelectMicrophoneByName(_settings.Audio.Microphone);
    SelectComboByTag(ToneCombo, _settings.Audio.Tone);
    VadCheck.IsChecked = _settings.Audio.Vad;

    LlmEnabledCheck.IsChecked = _settings.Llm.Enabled;
    LlmSettingsPanel.Visibility = _settings.Llm.Enabled ? Visibility.Visible : Visibility.Collapsed;
    LlmApiKeyBox.Password = _settings.Llm.ApiKey;
    LlmBaseUrlCombo.Text = _settings.Llm.BaseUrl;
    LlmModelCombo.Text = _settings.Llm.Model;
    LlmPromptBox.Text = _settings.Llm.Prompt;

    bool hasPosition = false;
    if (_settings.Window.Left.HasValue) { Left = _settings.Window.Left.Value; hasPosition = true; }
    if (_settings.Window.Top.HasValue) { Top = _settings.Window.Top.Value; hasPosition = true; }
    if (_settings.Window.Width.HasValue && _settings.Window.Width.Value >= MinWidth) Width = _settings.Window.Width.Value;
    if (_settings.Window.Height.HasValue && _settings.Window.Height.Value >= MinHeight) Height = _settings.Window.Height.Value;
    StartMinimizedCheck.IsChecked = _settings.Window.StartMinimized;

    ToggleShortcutBox.Text = FormatShortcut(_toggleModifiers, _toggleKey);
    PttShortcutBox.Text = FormatShortcut(_pttModifiers, _pttKey);
    AutostartCheck.IsChecked = IsAutostartEnabled();

    if (!hasPosition)
        CenterOnScreen();
}
```

**Step 3: Rewrite SaveSettings()**

Replace the entire `SaveSettings()` method with:

```csharp
private void SaveSettings()
{
    var langItem = LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
    var micItem = MicrophoneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
    var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
    var whisperModelItem = WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;

    _settings.ApiKey = ApiKeyBox.Password.Trim();

    _settings.Shortcuts.Toggle = FormatShortcut(_toggleModifiers, _toggleKey);
    _settings.Shortcuts.Ptt = FormatShortcut(_pttModifiers, _pttKey);
    _settings.Shortcuts.AiTriggerKey = (string)((AiTriggerKeyCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "shift");

    _settings.Transcription.Language = (string)(langItem?.Tag ?? "de");
    _settings.Transcription.Provider = (string)(providerItem?.Tag ?? "deepgram");
    _settings.Transcription.WhisperModel = (string)(whisperModelItem?.Tag ?? "tiny");
    _settings.Transcription.Keywords = KeywordsBox.Text.Trim();

    _settings.Audio.Microphone = micItem?.Content?.ToString() ?? "";
    _settings.Audio.Tone = (string)((ToneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "Gentle");
    _settings.Audio.Vad = VadCheck.IsChecked == true;

    _settings.Llm.Enabled = LlmEnabledCheck.IsChecked == true;
    _settings.Llm.ApiKey = LlmApiKeyBox.Password.Trim();
    _settings.Llm.BaseUrl = LlmBaseUrlCombo.Text.Trim();
    _settings.Llm.Model = LlmModelCombo.Text.Trim();
    _settings.Llm.Prompt = LlmPromptBox.Text.Trim();

    _settings.Window.Left = Left;
    _settings.Window.Top = Top;
    _settings.Window.Width = Width;
    _settings.Window.Height = Height;
    _settings.Window.StartMinimized = StartMinimizedCheck.IsChecked == true;

    SettingsService.Save(_settings);
}
```

**Step 4: Verify build and tests**

```bash
dotnet build
dotnet test
```

Expected: 0 errors, all tests pass.

**Step 5: Commit**

```bash
git add src/VoiceDictation/MainWindow.xaml.cs
git commit -m "refactor: wire MainWindow to SettingsService for JSON settings"
```

---

### Task 4: Clean up and delete placeholder test

**Files:**
- Delete: `tests/VoiceDictation.Tests/Services/ReplacementServiceTests.cs` (placeholder)

**Step 1: Remove the placeholder test**

Delete `tests/VoiceDictation.Tests/Services/ReplacementServiceTests.cs` — it only contained `Assert.True(true)` and is superseded by the real `SettingsServiceTests`.

**Step 2: Verify tests still pass**

```bash
dotnet test
```

Expected: all SettingsService tests pass, no other test failures.

**Step 3: Commit**

```bash
git add -A
git commit -m "chore: remove placeholder test"
```

---

### Task 5: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update the Settings section**

Replace the existing Settings subsection:

```markdown
### Settings

All settings are persisted to `%LOCALAPPDATA%\VoiceDictation\settings.txt`.
```

With:

```markdown
### Settings

All settings are persisted as JSON to `%LOCALAPPDATA%\VoiceDictation\settings.json`. The `SettingsService` handles load/save and auto-migrates from the legacy `settings.txt` format on first run.
```

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for JSON settings"
```

---

### Task 6: Final verification

**Step 1: Full build and test**

```bash
dotnet build
dotnet test
```

Expected: 0 errors, all tests pass.

**Step 2: Manual smoke test**

```bash
dotnet run --project src/VoiceDictation
```

- App should start and load settings (from existing `settings.txt` if present, migrating to `.json`)
- Change a setting, close, reopen — setting should persist
- Check `%LOCALAPPDATA%\VoiceDictation\settings.json` exists and is valid JSON
- Check `settings.txt` no longer exists

**Step 3: Verify git status is clean**

```bash
git status
```

Expected: clean working tree.
