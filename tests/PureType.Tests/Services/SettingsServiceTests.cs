using PureType.Services;

namespace PureType.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public void MigrateFromTxt_parses_all_fields()
    {
        var lines = new[]
        {
            "my-deepgram-key",              // line 0 = API key
            "toggle=Ctrl+Shift+D",
            "ptt=Win+R-Ctrl",
            "ai_trigger_key=ctrl",
            "language=en",
            "provider=whisper",
            "keywords=hello,world",
            "whisper_model=base",
            "microphone=My USB Mic",
            "tone=Sanft",                   // legacy German name -> Gentle
            "vad=True",
            "llm_enabled=True",
            "llm_apikey=sk-abc123",
            "llm_baseurl=https://example.com/v1",
            "llm_model=gpt-4",
            @"llm_prompt=Fix grammar.\nKeep meaning.",
            "left=100",
            "top=200",
            "width=800",
            "height=600",
            "start_minimized=True",
        };

        var result = SettingsService.MigrateFromTxt(lines);

        // Transcription
        Assert.Equal("my-deepgram-key", result.Transcription.ApiKey);
        Assert.Equal("en", result.Transcription.Language);
        Assert.Equal("whisper", result.Transcription.Provider);
        Assert.Equal("hello,world", result.Transcription.Keywords);
        Assert.Equal("base", result.Transcription.WhisperModel);

        // Shortcuts
        Assert.Equal("Ctrl+Shift+D", result.Shortcuts.Toggle);
        Assert.Equal("Win+R-Ctrl", result.Shortcuts.Ptt);
        // Audio
        Assert.Equal("My USB Mic", result.Audio.Microphone);
        Assert.Equal("Gentle", result.Audio.Tone);   // migrated from "Sanft"
        Assert.True(result.Audio.Vad);

        // LLM
        Assert.True(result.Llm.Enabled);
        Assert.Equal("sk-abc123", result.Llm.ApiKey);
        Assert.Equal("https://example.com/v1", result.Llm.BaseUrl);
        Assert.Equal("gpt-4", result.Llm.Model);
        Assert.Equal("Fix grammar.\nKeep meaning.", result.Llm.Prompts[0].Prompt); // \n converted
        Assert.Equal("Migrated", result.Llm.Prompts[0].Name);
        Assert.Equal("Shift", result.Llm.Prompts[0].Key);

        // Window
        Assert.Equal(100d, result.Window.Left);
        Assert.Equal(200d, result.Window.Top);
        Assert.Equal(800d, result.Window.Width);
        Assert.Equal(600d, result.Window.Height);
        Assert.True(result.Window.StartMinimized);
    }

    [Fact]
    public void MigrateFromTxt_empty_file_returns_defaults()
    {
        var result = SettingsService.MigrateFromTxt([]);

        var defaults = new AppSettings();

        Assert.Equal(defaults.Shortcuts.Toggle, result.Shortcuts.Toggle);
        Assert.Equal(defaults.Shortcuts.Ptt, result.Shortcuts.Ptt);
        Assert.Equal(defaults.Transcription.ApiKey, result.Transcription.ApiKey);
        Assert.Equal(defaults.Transcription.Language, result.Transcription.Language);
        Assert.Equal(defaults.Transcription.Provider, result.Transcription.Provider);
        Assert.Equal(defaults.Audio.Tone, result.Audio.Tone);
        Assert.False(result.Audio.Vad);
        Assert.False(result.Llm.Enabled);
        Assert.Null(result.Window.Left);
        Assert.Null(result.Window.Top);
    }

    [Fact]
    public void MigrateFromTxt_legacy_anthropic_provider_sets_baseurl()
    {
        var lines = new[]
        {
            "",                             // empty API key
            "llm_provider=anthropic",
        };

        var result = SettingsService.MigrateFromTxt(lines);

        Assert.Equal("https://api.anthropic.com/v1", result.Llm.BaseUrl);
    }

    [Fact]
    public void Default_settings_have_expected_values()
    {
        var settings = new AppSettings();

        // Shortcuts
        Assert.Equal("Ctrl+Alt+X", settings.Shortcuts.Toggle);
        Assert.Equal("Win+L-Ctrl", settings.Shortcuts.Ptt);
        // Transcription
        Assert.Equal("de", settings.Transcription.Language);
        Assert.Equal("deepgram", settings.Transcription.Provider);
        Assert.Equal("", settings.Transcription.ApiKey);
        Assert.Equal("", settings.Transcription.Keywords);
        Assert.Equal("tiny", settings.Transcription.WhisperModel);

        // Audio
        Assert.Equal("Gentle", settings.Audio.Tone);
        Assert.Equal("", settings.Audio.Microphone);
        Assert.False(settings.Audio.Vad);

        // LLM
        Assert.False(settings.Llm.Enabled);
        Assert.Equal("", settings.Llm.ApiKey);
        Assert.Equal("", settings.Llm.BaseUrl);
        Assert.Equal("", settings.Llm.Model);
        Assert.Empty(settings.Llm.Prompts);

        // Window
        Assert.Null(settings.Window.Left);
        Assert.Null(settings.Window.Top);
        Assert.Null(settings.Window.Width);
        Assert.Null(settings.Window.Height);
        Assert.False(settings.Window.StartMinimized);
    }

    [Fact]
    public void Save_and_Load_roundtrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"PureType_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var jsonPath = Path.Combine(tempDir, "settings.json");
            var svc = new SettingsService(jsonPath);

            var settings = new AppSettings
            {
                Shortcuts = new ShortcutSettings { Toggle = "Ctrl+Shift+Z", Ptt = "Win+R-Alt" },
                Transcription = new TranscriptionSettings
                {
                    Language = "en",
                    Provider = "whisper",
                    ApiKey = "test-key",
                    Keywords = "hello,world",
                    WhisperModel = "base"
                },
                Audio = new AudioSettings { Microphone = "USB Mic", Tone = "Classic", Vad = true },
                Llm = new LlmSettings
                {
                    Enabled = true,
                    ApiKey = "sk-test",
                    BaseUrl = "https://api.example.com/v1",
                    Model = "gpt-4",
                    Prompts = new List<NamedPrompt> { new() { Name = "Test", Key = "T", Prompt = "Fix grammar." } }
                },
                Window = new WindowSettings { Left = 100, Top = 200, Width = 800, Height = 600, StartMinimized = true },
            };

            svc.Save(settings);
            var loaded = svc.Load();

            Assert.Equal(settings.Shortcuts.Toggle, loaded.Shortcuts.Toggle);
            Assert.Equal(settings.Shortcuts.Ptt, loaded.Shortcuts.Ptt);
            Assert.Equal(settings.Transcription.Language, loaded.Transcription.Language);
            Assert.Equal(settings.Transcription.Provider, loaded.Transcription.Provider);
            Assert.Equal(settings.Transcription.ApiKey, loaded.Transcription.ApiKey);
            Assert.Equal(settings.Audio.Microphone, loaded.Audio.Microphone);
            Assert.Equal(settings.Audio.Tone, loaded.Audio.Tone);
            Assert.True(loaded.Audio.Vad);
            Assert.True(loaded.Llm.Enabled);
            Assert.Equal(settings.Llm.ApiKey, loaded.Llm.ApiKey);
            Assert.Single(loaded.Llm.Prompts);
            Assert.Equal("Test", loaded.Llm.Prompts[0].Name);
            Assert.Equal("T", loaded.Llm.Prompts[0].Key);
            Assert.Equal("Fix grammar.", loaded.Llm.Prompts[0].Prompt);
            Assert.Equal(settings.Window.Left, loaded.Window.Left);
            Assert.Equal(settings.Window.Top, loaded.Window.Top);
            Assert.True(loaded.Window.StartMinimized);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_defaults_when_no_file_exists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"PureType_Test_{Guid.NewGuid():N}");
        var jsonPath = Path.Combine(tempDir, "settings.json");
        var svc = new SettingsService(jsonPath);

        var result = svc.Load();

        Assert.Equal("Ctrl+Alt+X", result.Shortcuts.Toggle);
        Assert.Equal("deepgram", result.Transcription.Provider);
        Assert.Equal("de", result.Transcription.Language);
        Assert.False(result.Llm.Enabled);
    }

    [Fact]
    public void IsFirstRun_true_when_no_files_exist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"PureType_Test_{Guid.NewGuid():N}");
        var jsonPath = Path.Combine(tempDir, "settings.json");
        var svc = new SettingsService(jsonPath);

        Assert.True(svc.IsFirstRun);
    }

    [Fact]
    public void ShowOverlay_defaults_to_true()
    {
        var settings = new AppSettings();
        Assert.True(settings.Window.ShowOverlay);
    }

    [Fact]
    public void ShowOverlay_roundtrips_through_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var svc = new SettingsService(path);
            var original = new AppSettings
            {
                Window = new WindowSettings { ShowOverlay = false }
            };
            svc.Save(original);
            var loaded = svc.Load();
            Assert.False(loaded.Window.ShowOverlay);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AutoCapitalize_defaults_to_true()
    {
        var settings = new AppSettings();
        Assert.True(settings.Audio.AutoCapitalize);
    }

    [Fact]
    public void AutoCapitalize_roundtrips_through_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var svc = new SettingsService(path);
            var original = new AppSettings
            {
                Audio = new AudioSettings { AutoCapitalize = false }
            };
            svc.Save(original);
            var loaded = svc.Load();
            Assert.False(loaded.Audio.AutoCapitalize);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsFirstRun_false_when_json_exists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"PureType_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var jsonPath = Path.Combine(tempDir, "settings.json");
            File.WriteAllText(jsonPath, "{}");
            var svc = new SettingsService(jsonPath);

            Assert.False(svc.IsFirstRun);
        }
        finally { Directory.Delete(tempDir, true); }
    }
}
