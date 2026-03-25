using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace PureType.Services;

// ── Settings model (nested records with defaults) ────────────────────────

public record ShortcutSettings
{
    public string Toggle { get; init; } = "Ctrl+Alt+X";
    public string Ptt { get; init; } = "Win+L-Ctrl";
    public string Mute { get; init; } = "";
    public string LanguageSwitch { get; init; } = "";
    public string ClipboardAi { get; init; } = "";
}

public record TranscriptionSettings
{
    public string Language { get; init; } = "de";
    public string Provider { get; init; } = "deepgram";
    public string ApiKey { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string WhisperModel { get; init; } = "tiny";
    public WhisperTuningSettings WhisperTuning { get; init; } = new();
}

public record WhisperTuningSettings
{
    /// <summary>"greedy" or "beam"</summary>
    public string SamplingStrategy { get; init; } = "greedy";
    public int BeamSize { get; init; } = 5;
    public float EntropyThreshold { get; init; } = 3.0f;
}

public record AudioSettings
{
    public string Microphone { get; init; } = "";
    public string Tone { get; init; } = "Gentle";
    public bool Vad { get; init; }
    public int InputDelayMs { get; init; }
    /// <summary>
    /// How transcribed text is delivered: "Type" (SendInput), "Paste" (Clipboard + Ctrl+V), "Copy" (Clipboard only).
    /// </summary>
    public string InputMode { get; init; } = "Type";
    /// <summary>
    /// Capitalize the first letter after sentence-ending punctuation (. ? ! \n).
    /// </summary>
    public bool AutoCapitalize { get; init; } = true;
}

public record NamedPrompt
{
    public string Name { get; init; } = "";
    public string Key { get; init; } = "";
    public string Prompt { get; init; } = "";
}

public record LlmProfile
{
    public string BaseUrl { get; init; } = "";
    public string Model { get; init; } = "";

    /// <summary>
    /// Display name derived from Model and provider hostname, e.g. "gpt-4o-mini @ OpenAI".
    /// </summary>
    public string DisplayName
    {
        get
        {
            var provider = BaseUrl switch
            {
                var u when u.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase) => "OpenAI",
                var u when u.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase) => "Anthropic",
                var u when u.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase) => "OpenRouter",
                var u when u.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) => "Google Gemini",
                var u when Uri.TryCreate(u, UriKind.Absolute, out var uri) => uri.Host + (uri.Port is not (80 or 443) ? $":{uri.Port}" : ""),
                _ => "Unknown"
            };
            return string.IsNullOrWhiteSpace(Model) ? provider : $"{Model} @ {provider}";
        }
    }

    /// <summary>Identity key for deduplication: same BaseUrl + Model = same profile.</summary>
    public bool IsSameAs(LlmProfile other) =>
        string.Equals(BaseUrl?.TrimEnd('/'), other.BaseUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
        && string.Equals(Model, other.Model, StringComparison.OrdinalIgnoreCase);
}

public record LlmSettings
{
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string Model { get; init; } = "";
    public List<NamedPrompt> Prompts { get; init; } = new();
    public List<LlmProfile> RecentProfiles { get; init; } = new();
    /// <summary>API keys stored per endpoint URL (normalized, trimmed of trailing slash).</summary>
    public Dictionary<string, string> EndpointKeys { get; init; } = new();
}

public record WindowSettings
{
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public bool StartMinimized { get; init; }
    public double? SettingsWidth { get; init; }
    public double? SettingsHeight { get; init; }
    public string Theme { get; init; } = "Auto";
    public string LogLevel { get; init; } = "Information";
    public bool ShowOverlay { get; init; } = true;
    public double? OverlayLeft { get; init; }
    public double? OverlayTop { get; init; }
}

public record AppSettings
{
    public ShortcutSettings Shortcuts { get; init; } = new();
    public TranscriptionSettings Transcription { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public LlmSettings Llm { get; init; } = new();
    public WindowSettings Window { get; init; } = new();
}

// ── Service ──────────────────────────────────────────────────────────────

public class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "PureType");

    public static readonly string DefaultJsonPath = Path.Combine(SettingsDir, "settings.json");
    private static readonly string TxtPath = Path.Combine(SettingsDir, "settings.txt");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _jsonPath;
    private readonly bool _useDefaultPath;

    public bool IsFirstRun => !File.Exists(_jsonPath) && !(_useDefaultPath && File.Exists(TxtPath));

    public SettingsService() : this(DefaultJsonPath, useDefaultPath: true) { }

    public SettingsService(string jsonPath) : this(jsonPath, useDefaultPath: false) { }

    private SettingsService(string jsonPath, bool useDefaultPath)
    {
        _jsonPath = jsonPath;
        _useDefaultPath = useDefaultPath;
    }

    /// <summary>
    /// Loads settings from JSON, migrating from legacy TXT if needed, or returning defaults.
    /// </summary>
    public AppSettings Load()
    {
        // One-time migration from old app name
        if (_useDefaultPath)
        {
            var oldDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoiceDictation");
            if (Directory.Exists(oldDir) && !Directory.Exists(SettingsDir))
            {
                CopyDirectory(oldDir, SettingsDir);
            }
        }

        // 1. Try JSON
        if (File.Exists(_jsonPath))
        {
            try
            {
                var json = File.ReadAllText(_jsonPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load settings.json, using defaults");
                return new AppSettings();
            }
        }

        // 2. Try migrating from legacy TXT (only for default path)
        if (_useDefaultPath && File.Exists(TxtPath))
        {
            try
            {
                var lines = File.ReadAllLines(TxtPath);
                var settings = MigrateFromTxt(lines);
                Save(settings);
                File.Delete(TxtPath);
                return settings;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to migrate settings.txt, using defaults");
                return new AppSettings();
            }
        }

        // 3. Defaults
        return new AppSettings();
    }

    /// <summary>
    /// Persists settings as indented camelCase JSON.
    /// </summary>
    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_jsonPath);
        if (dir != null) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_jsonPath, json);
    }

    /// <summary>
    /// Parses the legacy line-based settings.txt format into AppSettings.
    /// Line 0 = API key (no key= prefix); remaining lines are key=value.
    /// </summary>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    internal static AppSettings MigrateFromTxt(string[] lines)
    {
        var shortcuts = new ShortcutSettings();
        var transcription = new TranscriptionSettings();
        var audio = new AudioSettings();
        var llm = new LlmSettings();
        var window = new WindowSettings();

        // Line 0 is the API key (raw, no key= prefix)
        if (lines.Length > 0)
            transcription = transcription with { ApiKey = lines[0].Trim() };

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0) continue;

            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();

            switch (key)
            {
                case "toggle":
                    shortcuts = shortcuts with { Toggle = value };
                    break;
                case "ptt":
                    shortcuts = shortcuts with { Ptt = value };
                    break;
                case "language":
                    transcription = transcription with { Language = value };
                    break;
                case "provider":
                    transcription = transcription with { Provider = value };
                    break;
                case "keywords":
                    transcription = transcription with { Keywords = value };
                    break;
                case "whisper_model":
                    transcription = transcription with { WhisperModel = value };
                    break;

                case "microphone":
                    audio = audio with { Microphone = value };
                    break;
                case "tone":
                    audio = audio with { Tone = SoundFeedback.MigrateName(value) };
                    break;
                case "vad":
                    audio = audio with { Vad = value.Equals("True", StringComparison.OrdinalIgnoreCase) };
                    break;

                case "llm_enabled":
                    llm = llm with { Enabled = value.Equals("True", StringComparison.OrdinalIgnoreCase) };
                    break;
                case "llm_apikey":
                    llm = llm with { ApiKey = value };
                    break;
                case "llm_baseurl":
                    llm = llm with { BaseUrl = value };
                    break;
                case "llm_provider":
                    // Legacy: migrate anthropic provider to base URL
                    if (value == "anthropic")
                        llm = llm with { BaseUrl = "https://api.anthropic.com/v1" };
                    break;
                case "llm_model":
                    llm = llm with { Model = value };
                    break;
                case "llm_prompt":
                    llm = llm with { Prompts = new List<NamedPrompt>
                    {
                        new() { Name = "Migrated", Key = "Shift", Prompt = value.Replace("\\n", "\n") }
                    }};
                    break;

                case "left":
                    if (double.TryParse(value, out var left))
                        window = window with { Left = left };
                    break;
                case "top":
                    if (double.TryParse(value, out var top))
                        window = window with { Top = top };
                    break;
                case "width":
                    if (double.TryParse(value, out var w))
                        window = window with { Width = w };
                    break;
                case "height":
                    if (double.TryParse(value, out var h))
                        window = window with { Height = h };
                    break;
                case "start_minimized":
                    window = window with { StartMinimized = value.Equals("True", StringComparison.OrdinalIgnoreCase) };
                    break;

                case "mode":
                    // Legacy setting, ignored — both modes always active
                    break;
            }
        }

        return new AppSettings
        {
            Shortcuts = shortcuts,
            Transcription = transcription,
            Audio = audio,
            Llm = llm,
            Window = window,
        };
    }
}
