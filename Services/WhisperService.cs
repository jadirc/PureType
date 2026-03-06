using System.IO;
using Serilog;
using Whisper.net;

namespace VoiceDictation.Services;

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
    private readonly string _language;
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

        await Task.Run(() =>
        {
            _factory = WhisperFactory.FromPath(modelPath);

            var builder = _factory.CreateBuilder()
                .WithLanguage(_language == "auto" ? "auto" : _language);

            _processor = builder.Build();
        });

        _connected = true;
        Log.Information("Whisper engine loaded: Model={Model}, Language={Language}", _modelName, _language);
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

        try
        {
            // Convert PCM-16 (16kHz, mono, 16-bit signed) to float32 samples
            int sampleCount = pcmData.Length / 2;
            var floatSamples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                floatSamples[i] = sample / 32768f;
            }

            // Whisper.net ProcessAsync expects a WAV-formatted stream.
            // Build a minimal WAV in memory from our float32 PCM samples,
            // encoded as 32-bit IEEE float, 16 kHz, mono.
            using var wavStream = BuildWavStream(floatSamples, sampleRate: 16000, channels: 1);

            var result = new System.Text.StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(wavStream))
            {
                result.Append(segment.Text);
            }

            var text = result.ToString().Trim();
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
    /// Builds a WAV-formatted MemoryStream from float32 samples (IEEE float PCM).
    /// </summary>
    private static MemoryStream BuildWavStream(float[] samples, int sampleRate, int channels)
    {
        int bitsPerSample = 32;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);
        int dataSize = samples.Length * sizeof(float);

        var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);        // file size - 8
        writer.Write("WAVE"u8);

        // fmt  sub-chunk (IEEE float = format tag 3)
        writer.Write("fmt "u8);
        writer.Write(16);                   // sub-chunk size
        writer.Write((short)3);             // audio format: IEEE float
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data sub-chunk
        writer.Write("data"u8);
        writer.Write(dataSize);

        var floatBytes = new byte[dataSize];
        Buffer.BlockCopy(samples, 0, floatBytes, 0, dataSize);
        writer.Write(floatBytes);

        writer.Flush();
        ms.Position = 0;
        return ms;
    }

    public async ValueTask DisposeAsync()
    {
        _connected = false;
        _processor?.Dispose();
        _factory?.Dispose();
        _audioBuffer.Dispose();
        Disconnected?.Invoke();
        await Task.CompletedTask;
    }
}
