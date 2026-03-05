using NAudio.Wave;

namespace VoiceDictation.Services;

/// <summary>
/// Nimmt Mikrofon-Audio mit NAudio auf (16kHz, 16bit, Mono)
/// und liefert PCM-Chunks per Event.
/// </summary>
public class AudioCaptureService : IDisposable
{
    private WaveInEvent? _waveIn;
    private bool _isRunning;

    /// <summary>Wird ausgelöst wenn neue Audiodaten verfügbar sind.</summary>
    public event Action<byte[]>? AudioDataAvailable;

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (_isRunning) return;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(sampleRate: 16000, bits: 16, channels: 1),
            BufferMilliseconds = 100
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        _isRunning = true;
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
        _isRunning = false;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var chunk = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, chunk, e.BytesRecorded);
        AudioDataAvailable?.Invoke(chunk);
    }

    public void Dispose() => Stop();
}
