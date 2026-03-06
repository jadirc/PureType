using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace VoiceDictation.Services;

/// <summary>
/// Verbindet sich mit der Deepgram Streaming-API via WebSocket
/// und liefert erkannte Transkripte per Event.
/// </summary>
public class DeepgramService : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private System.Timers.Timer? _keepAliveTimer;
    private readonly string _apiKey;
    private readonly string _language;

    public event Action<string>? TranscriptReceived;
    public event Action<string>? ErrorOccurred;
    public event Action? Disconnected;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public DeepgramService(string apiKey, string language = "de")
    {
        _apiKey = apiKey;
        _language = language;
    }

    public async Task ConnectAsync()
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {_apiKey}");

        var uri = new Uri(
            $"wss://api.deepgram.com/v1/listen" +
            $"?encoding=linear16" +
            $"&sample_rate=16000" +
            $"&channels=1" +
            $"&model=nova-3" +
            $"&language={_language}" +
            $"&smart_format=true" +
            $"&interim_results=false" +
            $"&punctuate=true");

        await _ws.ConnectAsync(uri, _cts.Token);

        // KeepAlive alle 8 Sekunden senden (Deepgram Idle-Timeout verhindern)
        _keepAliveTimer = new System.Timers.Timer(8000);
        _keepAliveTimer.Elapsed += async (_, _) => await SendKeepAliveAsync();
        _keepAliveTimer.Start();

        // Empfangs-Loop im Hintergrund starten
        _ = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>
    /// Sendet rohe PCM-16 Audiodaten (16kHz, Mono) an Deepgram.
    /// </summary>
    public async Task SendAudioAsync(byte[] audioData)
    {
        if (_ws?.State != WebSocketState.Open) return;
        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(audioData),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                _cts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Sende-Fehler: {ex.Message}");
        }
    }

    private static readonly byte[] KeepAlivePayload =
        Encoding.UTF8.GetBytes("{\"type\":\"KeepAlive\"}");

    private static readonly byte[] FinalizePayload =
        Encoding.UTF8.GetBytes("{\"type\":\"Finalize\"}");

    /// <summary>
    /// Signalisiert Deepgram, gepufferte Audiodaten sofort zu transkribieren.
    /// Wichtig für Push-to-Talk: beim Loslassen aufrufen.
    /// </summary>
    public async Task SendFinalizeAsync()
    {
        if (_ws?.State != WebSocketState.Open) return;
        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(FinalizePayload),
                WebSocketMessageType.Text,
                endOfMessage: true,
                _cts?.Token ?? CancellationToken.None);
        }
        catch { /* ignorieren */ }
    }

    private async Task SendKeepAliveAsync()
    {
        if (_ws?.State != WebSocketState.Open) return;
        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(KeepAlivePayload),
                WebSocketMessageType.Text,
                endOfMessage: true,
                _cts?.Token ?? CancellationToken.None);
        }
        catch { /* ignorieren – ReceiveLoop erkennt Disconnect */ }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[16384];
        var sb = new StringBuilder();

        try
        {
            while (_ws?.State == WebSocketState.Open && !(_cts?.IsCancellationRequested ?? true))
            {
                var result = await _ws.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    _cts?.Token ?? CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    ParseAndEmitTranscript(sb.ToString());
                    sb.Clear();
                }
            }
        }
        catch (OperationCanceledException) { /* normal beim Stoppen */ }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    private void ParseAndEmitTranscript(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Deepgram Antwortformat: channel.alternatives[0].transcript
            if (!root.TryGetProperty("channel", out var channel)) return;
            if (!channel.TryGetProperty("alternatives", out var alts)) return;
            if (alts.GetArrayLength() == 0) return;

            var transcript = alts[0].GetProperty("transcript").GetString();
            if (!string.IsNullOrWhiteSpace(transcript))
                TranscriptReceived?.Invoke(transcript.Trim());
        }
        catch { /* ungültiges JSON ignorieren */ }
    }

    public async ValueTask DisposeAsync()
    {
        _keepAliveTimer?.Stop();
        _keepAliveTimer?.Dispose();
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None);
            }
            catch { /* ignorieren beim Dispose */ }
        }
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
