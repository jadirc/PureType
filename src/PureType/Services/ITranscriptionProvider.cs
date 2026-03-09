namespace PureType.Services;

/// <summary>
/// Common interface for transcription providers (Deepgram cloud, Whisper local).
/// </summary>
public interface ITranscriptionProvider : IAsyncDisposable
{
    event Action<string, bool>? TranscriptReceived;
    event Action<string>? ErrorOccurred;
    event Action? Disconnected;

    bool IsConnected { get; }

    Task ConnectAsync();
    Task SendAudioAsync(byte[] audioData);
    Task SendFinalizeAsync();

    /// <summary>
    /// Changes the transcription language at runtime if supported.
    /// Default: no-op (provider requires reconnect).
    /// </summary>
    Task SetLanguageAsync(string language) => Task.CompletedTask;
}
