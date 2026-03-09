using System.IO;
using Serilog;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace PureType.Services;

/// <summary>
/// Local transcription using Whisper.net (whisper.cpp).
/// Buffers audio during recording, transcribes on finalize.
/// </summary>
public class WhisperService : ITranscriptionProvider
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private readonly MemoryStream _audioBuffer = new();
    private readonly string _modelName;
    private string _language;
    private bool _connected;

    public event Action<string, bool>? TranscriptReceived;
    public event Action<string>? ErrorOccurred;
    public event Action? Disconnected;

    public bool IsConnected => _connected;

    public WhisperService(string modelName, string language = "de")
    {
        _modelName = modelName;
        _language = string.IsNullOrEmpty(language) ? "auto" : language;
    }

    public async Task ConnectAsync()
    {
        var modelPath = WhisperModelManager.GetModelPath(_modelName);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper model not found: {modelPath}. Please download it first.");

        // Prefer CUDA GPU acceleration, fall back to CPU
        RuntimeOptions.RuntimeLibraryOrder =
        [
            RuntimeLibrary.Cuda,
            RuntimeLibrary.Cpu
        ];

        await Task.Run(() =>
        {
            _factory = WhisperFactory.FromPath(modelPath);

            var builder = _factory.CreateBuilder()
                .WithLanguage(_language == "auto" ? "auto" : _language);

            _processor = builder.Build();
        });

        _connected = true;
        var runtimeInfo = WhisperFactory.GetRuntimeInfo();
        var loadedLib = RuntimeOptions.LoadedLibrary;
        Log.Information("Whisper engine loaded: Model={Model}, Language={Language}, LoadedLibrary={Lib}, Runtime={Runtime}",
            _modelName, _language, loadedLib, runtimeInfo);
    }

    public Task SendAudioAsync(byte[] audioData)
    {
        if (!_connected) return Task.CompletedTask;
        lock (_audioBuffer)
        {
            _audioBuffer.Write(audioData, 0, audioData.Length);
        }
        return Task.CompletedTask;
    }

    public async Task SendFinalizeAsync()
    {
        if (!_connected || _processor is null) return;

        byte[] pcmData;
        lock (_audioBuffer)
        {
            pcmData = _audioBuffer.ToArray();
            _audioBuffer.SetLength(0);
        }

        if (pcmData.Length < 3200) // less than 100ms of audio at 16kHz/16bit
            return;

        Log.Debug("Whisper: processing {Bytes} bytes ({Seconds:F1}s) of audio",
            pcmData.Length, pcmData.Length / 2.0 / 16000);

        try
        {
            // Convert PCM-16 (16-bit signed LE) to float32 samples.
            // Use float[] overload instead of WAV stream — Whisper.net 1.9.0 has a bug
            // where ProcessAsync(Stream) produces empty results in CUDA mode.
            int sampleCount = pcmData.Length / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                samples[i] = s / 32768f;
            }

            // Log audio statistics to verify signal is present
            float maxAmp = 0, sumSq = 0;
            for (int j = 0; j < sampleCount; j++)
            {
                float abs = Math.Abs(samples[j]);
                if (abs > maxAmp) maxAmp = abs;
                sumSq += samples[j] * samples[j];
            }
            float rms = (float)Math.Sqrt(sumSq / sampleCount);
            Log.Debug("Whisper audio stats: peak={Peak:F4}, rms={Rms:F4}, samples={Samples}",
                maxAmp, rms, sampleCount);

            var result = new System.Text.StringBuilder();
            int segCount = 0;
            await foreach (var segment in _processor.ProcessAsync(samples))
            {
                segCount++;
                Log.Debug("Whisper segment {N}: Start={Start}, End={End}, Text=\"{Text}\", Prob={Prob:F3}",
                    segCount, segment.Start, segment.End, segment.Text, segment.Probability);
                result.Append(segment.Text);
            }

            var text = result.ToString().Trim();
            Log.Debug("Whisper result ({Segments} segments): \"{Text}\"", segCount, text);
            if (!string.IsNullOrWhiteSpace(text))
                TranscriptReceived?.Invoke(text, true); // always final in batch mode
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Whisper transcription failed");
            ErrorOccurred?.Invoke($"Whisper error: {ex.Message}");
        }
    }

    /// <summary>
    /// Changes the transcription language and rebuilds the Whisper processor.
    /// Does NOT require a full reconnect — the model stays loaded.
    /// Must NOT be called while transcription is in progress (caller guards with IsRecording check).
    /// </summary>
    public async Task SetLanguageAsync(string language)
    {
        _language = string.IsNullOrEmpty(language) ? "auto" : language;

        if (_factory is null) return;

        await Task.Run(() =>
        {
            _processor?.Dispose();
            var builder = _factory.CreateBuilder()
                .WithLanguage(_language == "auto" ? "auto" : _language);
            _processor = builder.Build();
        });

        Log.Information("Whisper language changed to {Language}", _language);
    }

    public async ValueTask DisposeAsync()
    {
        _connected = false;

        // Do NOT call Dispose on processor/factory when CUDA is active.
        // Native CUDA cleanup can trigger AccessViolationException (uncatchable in .NET 8)
        // if the GPU context is in a bad state (e.g. OOM with large models).
        // The OS will free all GPU resources when the process exits.
        var isCuda = RuntimeOptions.LoadedLibrary == RuntimeLibrary.Cuda;
        if (!isCuda)
        {
            _processor?.Dispose();
            _factory?.Dispose();
        }
        else
        {
            // Suppress GC finalizers to prevent native CUDA cleanup crash
            if (_processor != null) GC.SuppressFinalize(_processor);
            if (_factory != null) GC.SuppressFinalize(_factory);
            Log.Debug("Skipping Whisper dispose (CUDA) — resources freed on process exit");
        }

        _processor = null;
        _factory = null;
        _audioBuffer.Dispose();
        Disconnected?.Invoke();
        await Task.CompletedTask;
    }
}
