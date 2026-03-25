using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PureType.Services;

/// <summary>
/// Connects to the Deepgram Streaming API via WebSocket
/// and delivers recognized transcripts via events.
/// </summary>
public class DeepgramService : ITranscriptionProvider
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private System.Timers.Timer? _keepAliveTimer;
    private readonly string _apiKey;
    private readonly string _language;
    private readonly string[] _keywords;

    private const int MaxReconnectAttempts = 10;
    private const int MaxBackoffSeconds = 30;
    private int _reconnecting; // 0 = false, 1 = true
    private Task? _reconnectTask;

    public event Action<string, bool>? TranscriptReceived;
    public event Action<string>? ErrorOccurred;
    public event Action? Disconnected;
    public event Action<int, int>? Reconnecting; // (attempt, maxAttempts)
    public event Action? Reconnected;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public DeepgramService(string apiKey, string language = "de", string[]? keywords = null)
    {
        _apiKey = apiKey;
        _language = language;
        _keywords = keywords ?? Array.Empty<string>();
    }

    public async Task ConnectAsync()
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {_apiKey}");

        await _ws.ConnectAsync(BuildUri(), _cts.Token);

        // Send KeepAlive every 8 seconds (prevent Deepgram idle timeout)
        _keepAliveTimer = new System.Timers.Timer(8000);
        _keepAliveTimer.Elapsed += async (_, _) => await SendKeepAliveAsync();
        _keepAliveTimer.Start();

        // Start receive loop in background
        _ = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>
    /// Sends raw PCM-16 audio data (16kHz, Mono) to Deepgram.
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
            ErrorOccurred?.Invoke($"Send error: {ex.Message}");
        }
    }

    private static readonly byte[] KeepAlivePayload =
        Encoding.UTF8.GetBytes("{\"type\":\"KeepAlive\"}");

    private static readonly byte[] FinalizePayload =
        Encoding.UTF8.GetBytes("{\"type\":\"Finalize\"}");

    /// <summary>
    /// Signals Deepgram to immediately transcribe buffered audio data.
    /// Important for Push-to-Talk: call on key release.
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
        catch { /* ignore */ }
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
        catch { /* ignore — ReceiveLoop detects disconnect */ }
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
        catch (OperationCanceledException) { /* normal when stopping */ }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            if (_cts?.IsCancellationRequested ?? true)
            {
                Disconnected?.Invoke();
            }
            else
            {
                // Fire-and-forget reconnect; we cannot await in finally
                _reconnectTask = TryReconnectAsync();
            }
        }
    }

    private async Task TryReconnectAsync()
    {
        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0) return;

        try
        {
            _keepAliveTimer?.Stop();

            for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                if (_cts?.IsCancellationRequested ?? true) break;

                Reconnecting?.Invoke(attempt, MaxReconnectAttempts);

                var delaySec = Math.Min(1 << (attempt - 1), MaxBackoffSeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySec), _cts?.Token ?? CancellationToken.None);
                }
                catch (OperationCanceledException) { break; }

                try
                {
                    _ws?.Dispose();
                    _ws = new ClientWebSocket();
                    _ws.Options.SetRequestHeader("Authorization", $"Token {_apiKey}");
                    await _ws.ConnectAsync(BuildUri(), _cts?.Token ?? CancellationToken.None);

                    _ = Task.Run(ReceiveLoopAsync);
                    _keepAliveTimer?.Start();
                    Reconnected?.Invoke();
                    return;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Reconnect attempt {attempt} failed: {ex.Message}");
                }
            }

            Disconnected?.Invoke();
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    private Uri BuildUri()
    {
        var uriBuilder =
            $"wss://api.deepgram.com/v1/listen" +
            $"?encoding=linear16" +
            $"&sample_rate=16000" +
            $"&channels=1" +
            $"&model=nova-3" +
            $"&language={_language}" +
            $"&smart_format=true" +
            $"&interim_results=true" +
            $"&punctuate=true";

        foreach (var kw in _keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw))
                uriBuilder += $"&keywords={Uri.EscapeDataString(kw.Trim())}:5";
        }

        return new Uri(uriBuilder);
    }

    private void ParseAndEmitTranscript(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Deepgram response format: channel.alternatives[0].transcript
            if (!root.TryGetProperty("channel", out var channel)) return;
            if (!channel.TryGetProperty("alternatives", out var alts)) return;
            if (alts.GetArrayLength() == 0) return;

            var transcript = alts[0].GetProperty("transcript").GetString();
            var isFinal = root.TryGetProperty("is_final", out var isFinalProp) && isFinalProp.GetBoolean();
            if (!string.IsNullOrWhiteSpace(transcript))
                TranscriptReceived?.Invoke(transcript.Trim(), isFinal);
        }
        catch { /* ignore invalid JSON */ }
    }

    public async ValueTask DisposeAsync()
    {
        _keepAliveTimer?.Stop();
        _keepAliveTimer?.Dispose();
        _cts?.Cancel();
        if (_reconnectTask != null)
        {
            try { await _reconnectTask; }
            catch { /* reconnect was cancelled, that's fine */ }
        }
        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None);
            }
            catch { /* ignore during Dispose */ }
        }
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
