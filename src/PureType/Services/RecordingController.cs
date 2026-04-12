using System.Windows.Media;
using Serilog;

namespace PureType.Services;

/// <summary>
/// Manages the recording lifecycle: start/stop, audio routing, VAD,
/// transcript collection, and keyboard injection.
/// Communicates with UI via events — no direct UI access.
/// </summary>
public class RecordingController
{
    private readonly AudioCaptureService _audio;
    private readonly ReplacementService _replacements;

    // ── State ──────────────────────────────────────────────────────────
    private bool _recording;
    private enum RecordingSource { None, Toggle, Ptt }
    private RecordingSource _recordingSource = RecordingSource.None;
    private VadService? _vad;
    private readonly List<string> _sessionChunks = new();
    private readonly List<(DateTime Timestamp, string Text)> _transcriptLog = new();
    private NamedPrompt? _selectedPrompt;
    private string _interimText = "";
    private bool _capitalizeNext = true;
    private bool _autoCapitalize = true;
    private StatsService? _stats;
    private DateTime _recordingStartTime;

    // ── Provider ───────────────────────────────────────────────────────
    private ITranscriptionProvider? _provider;
    private bool _connected;

    // ── Settings ───────────────────────────────────────────────────────
    private bool _vadEnabled;
    private bool _llmEnabled;
    private bool _autoCorrectionEnabled;
    private string _inputMode = "Type";
    private List<NamedPrompt> _prompts = new();

    // ── Colors (resolved from theme resources) ─────────────────────────
    private static Color Red    => ThemeColor("RedBrush");
    private static Color Green  => ThemeColor("GreenBrush");

    private static Color ThemeColor(string key) =>
        ((SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;

    // ── Events ─────────────────────────────────────────────────────────
    /// <summary>(statusText, color)</summary>
    public event Action<string, Color>? StatusChanged;
    /// <summary>Full transcript text for display.</summary>
    public event Action<string>? TranscriptUpdated;
    /// <summary>Interim (partial) transcript text; empty string = clear.</summary>
    public event Action<string>? InterimTextUpdated;
    /// <summary>Recording state changed — tray icon needs update.</summary>
    public event Action? RecordingStateChanged;
    /// <summary>Audio level (0.0–1.0) for VU meter.</summary>
    public event Action<double>? AudioLevelChanged;
    /// <summary>Recording stopped — reset VU meter.</summary>
    public event Action? RecordingStopped;
    /// <summary>Request LLM post-processing with (text, prompt).</summary>
    public event Action<string, NamedPrompt>? LlmProcessingRequested;
    /// <summary>Request auto-correction LLM processing with collected session text.</summary>
    public event Action<string>? AutoCorrectionRequested;
    /// <summary>Request clipboard copy (clipboard mode).</summary>
    public event Action<string>? ClipboardRequested;
    /// <summary>(message, dotColor, autoClose) for toast notifications.</summary>
    public event Action<string, Color, bool>? ToastRequested;
    /// <summary>Fired after a recording session is tracked in stats.</summary>
    public event Action? StatsUpdated;

    // ── Public API ─────────────────────────────────────────────────────
    public bool IsRecording => _recording;
    public bool IsMuted { get; set; }
    public IReadOnlyList<(DateTime Timestamp, string Text)> TranscriptLog => _transcriptLog;
    public TimeSpan LastSttDuration { get; private set; }

    public RecordingController(
        AudioCaptureService audio,
        ReplacementService replacements)
    {
        _audio = audio;
        _replacements = replacements;

        _audio.AudioDataAvailable += OnAudioData;
        _audio.AudioLevelChanged += level => AudioLevelChanged?.Invoke(level);
    }

    /// <summary>
    /// Stores VAD and LLM enabled flags from settings.
    /// </summary>
    public void Configure(AppSettings settings)
    {
        _vadEnabled = settings.Audio.Vad;
        _llmEnabled = settings.Llm.Enabled;
        _inputMode = settings.Audio.InputMode;
        _autoCapitalize = settings.Audio.AutoCapitalize;
        _prompts = settings.Llm.Prompts;
        _autoCorrectionEnabled = settings.AutoCorrection.Enabled;
    }

    public void SetStatsService(StatsService stats)
    {
        _stats = stats;
    }

    /// <summary>
    /// Sets the active transcription provider and connection state.
    /// Subscribes/unsubscribes TranscriptReceived as needed.
    /// </summary>
    public void SetProvider(ITranscriptionProvider? provider, bool connected)
    {
        if (_provider is not null)
        {
            _provider.TranscriptReceived -= OnTranscriptReceived;
            _provider.TranscriptionTimed -= OnTranscriptionTimed;
        }

        _provider = provider;
        _connected = connected;

        if (_provider is not null)
        {
            _provider.TranscriptReceived += OnTranscriptReceived;
            _provider.TranscriptionTimed += OnTranscriptionTimed;
        }
    }

    public void HandleToggle()
    {
        if (_recording)
        {
            if (_recordingSource != RecordingSource.Toggle) return;
            _recordingSource = RecordingSource.None;
            StopRecording();
        }
        else
        {
            _selectedPrompt = null;
            _recordingSource = RecordingSource.Toggle;
            StartRecording();
        }
    }

    public void HandlePttDown()
    {
        if (!_connected || _recording) return;
        _selectedPrompt = null;
        _recordingSource = RecordingSource.Ptt;
        StartRecording();
    }

    public void HandlePttUp()
    {
        if (_recordingSource != RecordingSource.Ptt) return;
        _recordingSource = RecordingSource.None;
        StopRecording();
    }

    public void HandlePromptKeyPressed(int vkCode)
    {
        if (!_recording || !_llmEnabled) return;
        var prompt = _prompts.FirstOrDefault(p =>
            VKeyFromString(p.Key) == vkCode);
        if (prompt == null || prompt == _selectedPrompt) return;
        _selectedPrompt = prompt;

        StatusChanged?.Invoke($"\u25CF Recording + AI ({prompt.Name})", Red);
        ToastRequested?.Invoke($"Recording + AI ({prompt.Name})", Red, false);
    }

    /// <summary>Converts a key name string (e.g. "T", "F1", "Shift") to a Win32 virtual key code.</summary>
    internal static int VKeyFromString(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        if (Enum.TryParse<System.Windows.Input.Key>(key, ignoreCase: true, out var wpfKey))
            return System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey);
        return 0;
    }

    // ── Auto-capitalize ───────────────────────────────────────────────

    /// <summary>
    /// Capitalizes the first letter if <paramref name="capitalizeNext"/> is true.
    /// Returns the transformed text and whether the *next* chunk should be capitalized
    /// (true if text ends with sentence-ending punctuation).
    /// </summary>
    internal static (string text, bool capitalizeNext) ApplyAutoCapitalize(string text, bool capitalizeNext)
    {
        if (string.IsNullOrEmpty(text))
            return (text, capitalizeNext);

        if (capitalizeNext)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i]))
                {
                    text = string.Concat(text.AsSpan(0, i), text[i].ToString().ToUpperInvariant(), text.AsSpan(i + 1));
                    break;
                }
            }
        }

        var endsWithNewline = text.Length > 0 && text[^1] == '\n';
        var trimmed = text.TrimEnd();
        var lastChar = trimmed.Length > 0 ? trimmed[^1] : '\0';
        var nextCapitalize = endsWithNewline || lastChar is '.' or '?' or '!';

        return (text, nextCapitalize);
    }

    // ── Start / Stop ───────────────────────────────────────────────────

    private async void StartRecording()
    {
        if (_recording || !_connected) return;
        _sessionChunks.Clear();
        _capitalizeNext = true;
        _recordingStartTime = DateTime.UtcNow;
        LastSttDuration = TimeSpan.Zero;
        _recording = true;
        await SoundFeedback.PlayStartAsync();
        await Task.Delay(50); // allow speaker decay so mic doesn't pick up the tail
        _audio.Start();

        var recordingLabel = _autoCorrectionEnabled ? "\u25CF Recording (AI)" : "\u25CF Recording";
        StatusChanged?.Invoke(recordingLabel, Red);
        ToastRequested?.Invoke(recordingLabel, Red, false);

        // Notify UI to add separator between recording sessions
        TranscriptUpdated?.Invoke("\0separator");

        if (_vadEnabled && _recordingSource == RecordingSource.Toggle)
        {
            _vad = new VadService();
            _vad.SilenceDetected += () => StopRecording();
            _vad.Reset();
        }
        RecordingStateChanged?.Invoke();
    }

    public async void StopRecording()
    {
        if (!_recording) return;
        _audio.Stop();
        _recording = false;
        _vad = null;
        var recordingStopTime = DateTime.UtcNow;

        _interimText = "";
        InterimTextUpdated?.Invoke("");
        RecordingStopped?.Invoke();

        // Flush provider buffer so the last transcript arrives immediately
        if (_provider is not null)
            await _provider.SendFinalizeAsync();

        // Brief yield so pending TranscriptReceived callbacks can populate _sessionChunks
        await Task.Delay(50);

        SoundFeedback.PlayStop();
        if (_selectedPrompt == null && !_autoCorrectionEnabled)
        {
            var sttLabel = LastSttDuration > TimeSpan.Zero
                ? $"Whisper: {LastSttDuration.TotalSeconds:F1}s"
                : "Recording stopped";
            ToastRequested?.Invoke(sttLabel, Green, true);
        }

        if (_selectedPrompt != null && _sessionChunks.Count > 0)
        {
            var prompt = _selectedPrompt;
            _selectedPrompt = null;
            var fullText = string.Join("", _sessionChunks);
            LlmProcessingRequested?.Invoke(fullText, prompt);
        }
        else if (_autoCorrectionEnabled && _sessionChunks.Count > 0)
        {
            var fullText = string.Join("", _sessionChunks);
            AutoCorrectionRequested?.Invoke(fullText);
        }

        // Track stats
        if (_stats != null && _sessionChunks.Count > 0)
        {
            var allText = string.Join("", _sessionChunks);
            var wordCount = allText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            var duration = (int)(recordingStopTime - _recordingStartTime).TotalSeconds;
            var sttMs = (int)LastSttDuration.TotalMilliseconds;
            _stats.RecordSession(wordCount, duration, sttMs: sttMs);
            StatsUpdated?.Invoke();
        }

        if (_connected)
        {
            var readyLabel = _autoCorrectionEnabled ? "Connected \u2013 ready (AI)" : "Connected \u2013 ready";
            StatusChanged?.Invoke(readyLabel, Green);
        }
        RecordingStateChanged?.Invoke();
    }

    // ── Audio → Provider ───────────────────────────────────────────────

    private void OnTranscriptionTimed(TimeSpan duration)
    {
        LastSttDuration = duration;
    }

    private async void OnAudioData(byte[] chunk)
    {
        if (_provider is null || !_recording) return;
        if (!IsMuted)
            await _provider.SendAudioAsync(chunk);
        _vad?.ProcessAudio(chunk);
    }

    // ── Transcript Received ────────────────────────────────────────────

    private async void OnTranscriptReceived(string text, bool isFinal)
    {
        if (isFinal)
        {
            _interimText = "";
            InterimTextUpdated?.Invoke("");
            var processed = _replacements.Apply(text);
            if (_autoCapitalize)
            {
                (processed, _capitalizeNext) = ApplyAutoCapitalize(processed, _capitalizeNext);
            }
            processed = CodeFormatter.Apply(processed);
            TranscriptUpdated?.Invoke(processed);
            _sessionChunks.Add(processed);
            _transcriptLog.Add((DateTime.Now, processed));

            // When AI post-processing is pending, don't type yet — LLM will type the result
            if (_selectedPrompt == null && !_autoCorrectionEnabled)
            {
                try
                {
                    switch (_inputMode)
                    {
                        case "Copy":
                            ClipboardRequested?.Invoke(processed);
                            break;
                        case "Paste":
                            await KeyboardInjector.PasteTextAsync(processed);
                            break;
                        default: // "Type"
                            await KeyboardInjector.TypeTextAsync(processed);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Text injection error");
                }
            }
        }
        else
        {
            _interimText = text;
            InterimTextUpdated?.Invoke(text);
        }
    }
}
