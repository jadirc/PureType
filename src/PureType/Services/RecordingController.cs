using System.Windows.Media;
using Serilog;
using PureType.Helpers;

namespace PureType.Services;

/// <summary>
/// Manages the recording lifecycle: start/stop, audio routing, VAD,
/// transcript collection, and keyboard injection.
/// Communicates with UI via events — no direct UI access.
/// </summary>
public class RecordingController
{
    private readonly AudioCaptureService _audio;
    private readonly KeyboardHookService _keyboardHook;
    private readonly ReplacementService _replacements;

    // ── State ──────────────────────────────────────────────────────────
    private bool _recording;
    private enum RecordingSource { None, Toggle, Ptt }
    private RecordingSource _recordingSource = RecordingSource.None;
    private VadService? _vad;
    private readonly List<string> _sessionChunks = new();
    private readonly List<(DateTime Timestamp, string Text)> _transcriptLog = new();
    private bool _aiPostProcessRequested;
    private string _interimText = "";

    // ── Provider ───────────────────────────────────────────────────────
    private ITranscriptionProvider? _provider;
    private bool _connected;

    // ── Settings ───────────────────────────────────────────────────────
    private bool _vadEnabled;
    private bool _llmEnabled;
    private bool _clipboardMode;

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
    /// <summary>Request LLM post-processing with the given text.</summary>
    public event Action<string>? LlmProcessingRequested;
    /// <summary>Request clipboard copy (clipboard mode).</summary>
    public event Action<string>? ClipboardRequested;
    /// <summary>(message, dotColor, autoClose) for toast notifications.</summary>
    public event Action<string, Color, bool>? ToastRequested;

    // ── Public API ─────────────────────────────────────────────────────
    public bool IsRecording => _recording;
    public bool IsMuted { get; set; }
    public IReadOnlyList<(DateTime Timestamp, string Text)> TranscriptLog => _transcriptLog;

    public RecordingController(
        AudioCaptureService audio,
        KeyboardHookService keyboardHook,
        ReplacementService replacements)
    {
        _audio = audio;
        _keyboardHook = keyboardHook;
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
        _clipboardMode = settings.Audio.ClipboardMode;
    }

    /// <summary>
    /// Sets the active transcription provider and connection state.
    /// Subscribes/unsubscribes TranscriptReceived as needed.
    /// </summary>
    public void SetProvider(ITranscriptionProvider? provider, bool connected)
    {
        if (_provider is not null)
            _provider.TranscriptReceived -= OnTranscriptReceived;

        _provider = provider;
        _connected = connected;

        if (_provider is not null)
            _provider.TranscriptReceived += OnTranscriptReceived;
    }

    public void HandleToggle(bool aiKeyHeld)
    {
        if (_recording)
        {
            if (_recordingSource != RecordingSource.Toggle) return;
            if (!_aiPostProcessRequested && _llmEnabled)
                _aiPostProcessRequested = aiKeyHeld;
            _recordingSource = RecordingSource.None;
            StopRecording();
        }
        else
        {
            _aiPostProcessRequested = aiKeyHeld && _llmEnabled;
            _recordingSource = RecordingSource.Toggle;
            StartRecording();
        }
    }

    public void HandlePttDown(bool aiKeyHeld)
    {
        if (!_connected || _recording) return;
        _aiPostProcessRequested = aiKeyHeld && _llmEnabled;
        _recordingSource = RecordingSource.Ptt;
        StartRecording();
    }

    public void HandlePttUp()
    {
        if (_recordingSource != RecordingSource.Ptt) return;
        if (!_aiPostProcessRequested && _llmEnabled)
            _aiPostProcessRequested = _keyboardHook.IsAiKeyHeld();
        _recordingSource = RecordingSource.None;
        StopRecording();
    }

    // ── Start / Stop ───────────────────────────────────────────────────

    private void StartRecording()
    {
        if (_recording || !_connected) return;
        _sessionChunks.Clear();
        _recording = true;
        SoundFeedback.PlayStart();
        _audio.Start();

        var aiLabel = _aiPostProcessRequested ? " + AI" : "";
        StatusChanged?.Invoke($"\u25CF Recording{aiLabel}", Red);
        ToastRequested?.Invoke(
            _aiPostProcessRequested ? "Recording + AI" : "Recording",
            Red, false);

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

        _interimText = "";
        InterimTextUpdated?.Invoke("");
        RecordingStopped?.Invoke();

        // Flush provider buffer so the last transcript arrives immediately
        if (_provider is not null)
            await _provider.SendFinalizeAsync();

        // Brief yield so pending TranscriptReceived callbacks can populate _sessionChunks
        await Task.Delay(50);

        SoundFeedback.PlayStop();
        if (!_aiPostProcessRequested)
            ToastRequested?.Invoke("Recording stopped", Green, true);

        if (_aiPostProcessRequested && _sessionChunks.Count > 0)
        {
            _aiPostProcessRequested = false;
            var fullText = string.Join("", _sessionChunks);
            LlmProcessingRequested?.Invoke(fullText);
        }

        if (_connected)
            StatusChanged?.Invoke("Connected \u2013 ready", Green);
        RecordingStateChanged?.Invoke();
    }

    // ── Audio → Provider ───────────────────────────────────────────────

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
            TranscriptUpdated?.Invoke(processed);
            _sessionChunks.Add(processed);
            _transcriptLog.Add((DateTime.Now, processed));

            // When AI post-processing is pending, don't type yet — LLM will type the result
            if (!_aiPostProcessRequested)
            {
                try
                {
                    if (_clipboardMode)
                        ClipboardRequested?.Invoke(processed);
                    else
                        await KeyboardInjector.TypeTextAsync(processed);
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
