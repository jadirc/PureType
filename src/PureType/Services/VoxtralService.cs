using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Serilog;

namespace PureType.Services;

public class VoxtralService : ITranscriptionProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private string _language;
    private readonly HttpClient _http = new();
    private readonly MemoryStream _audioBuffer = new();
    private bool _connected;

    public event Action<string, bool>? TranscriptReceived;
    public event Action<string>? ErrorOccurred;
    public event Action? Disconnected;
    public event Action? SilenceSkipped;
    public event Action<TimeSpan>? TranscriptionTimed;

    public bool IsConnected => _connected;

    public VoxtralService(string apiKey, string model, string language = "de")
    {
        _apiKey = apiKey;
        _model = model;
        _language = string.IsNullOrEmpty(language) ? "auto" : language;
    }

    public Task ConnectAsync()
    {
        _connected = true;
        Log.Information("VoxtralService ready: Model={Model}, Language={Language}", _model, _language);
        return Task.CompletedTask;
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
        if (!_connected) return;

        byte[] pcmData;
        lock (_audioBuffer)
        {
            pcmData = _audioBuffer.ToArray();
            _audioBuffer.SetLength(0);
        }

        if (pcmData.Length < 3200) // less than 100ms
            return;

        Log.Debug("Voxtral: processing {Bytes} bytes ({Seconds:F1}s) of audio",
            pcmData.Length, pcmData.Length / 2.0 / 16000);

        if (!HasSpeech(pcmData))
        {
            Log.Debug("Voxtral: skipping likely silence");
            SilenceSkipped?.Invoke();
            return;
        }

        var wav = BuildWav(pcmData);

        try
        {
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(wav);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(fileContent, "file", "audio.wav");
            content.Add(new StringContent(_model), "model");
            if (_language != "auto")
                content.Add(new StringContent(_language), "language");

            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.mistral.ai/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = content;

            var response = await _http.SendAsync(request, cts.Token);
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var msg = $"Voxtral API error {(int)response.StatusCode}: {json}";
                Log.Error(msg);
                ErrorOccurred?.Invoke(msg);
                return;
            }

            var text = ParseTranscript(json);
            Log.Debug("Voxtral result ({Elapsed}ms): \"{Text}\"", sw.ElapsedMilliseconds, text);
            TranscriptionTimed?.Invoke(sw.Elapsed);

            if (!string.IsNullOrWhiteSpace(text))
                TranscriptReceived?.Invoke(text.Trim(), true);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Voxtral transcription timed out after 30s");
            ErrorOccurred?.Invoke("Voxtral timed out — try again");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Voxtral transcription failed");
            ErrorOccurred?.Invoke($"Voxtral error: {ex.Message}");
        }
    }

    public Task SetLanguageAsync(string language)
    {
        _language = string.IsNullOrEmpty(language) ? "auto" : language;
        Log.Information("Voxtral language changed to {Language}", _language);
        return Task.CompletedTask;
    }

    internal static string ParseTranscript(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("text").GetString() ?? "";
    }

    internal static byte[] BuildWav(byte[] pcmData)
    {
        var wav = new byte[44 + pcmData.Length];
        var ms = new MemoryStream(wav);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + pcmData.Length);     // file size - 8
        bw.Write("WAVE"u8);

        // fmt chunk
        bw.Write("fmt "u8);
        bw.Write(16);                      // chunk size
        bw.Write((short)1);                // audio format: PCM
        bw.Write((short)1);                // channels: mono
        bw.Write(16000);                   // sample rate
        bw.Write(16000 * 1 * 16 / 8);     // byte rate
        bw.Write((short)(1 * 16 / 8));    // block align
        bw.Write((short)16);              // bits per sample

        // data chunk
        bw.Write("data"u8);
        bw.Write(pcmData.Length);
        bw.Write(pcmData);

        return wav;
    }

    internal static bool HasSpeech(byte[] pcmData)
    {
        const float SpeechRmsThreshold = 0.035f;
        const int chunkSamples = 1600; // 100ms at 16kHz
        const int minSpeechChunks = 2;

        int sampleCount = pcmData.Length / 2;
        int speechChunkCount = 0;

        for (int offset = 0; offset < sampleCount; offset += chunkSamples)
        {
            int end = Math.Min(offset + chunkSamples, sampleCount);
            float chunkSumSq = 0;
            for (int k = offset; k < end; k++)
            {
                short s = (short)(pcmData[k * 2] | (pcmData[k * 2 + 1] << 8));
                float sample = s / 32768f;
                chunkSumSq += sample * sample;
            }
            float chunkRms = (float)Math.Sqrt(chunkSumSq / (end - offset));
            if (chunkRms >= SpeechRmsThreshold)
                speechChunkCount++;
            if (speechChunkCount >= minSpeechChunks)
                return true;
        }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        _http.Dispose();
        _audioBuffer.Dispose();
        Disconnected?.Invoke();
        return ValueTask.CompletedTask;
    }
}
