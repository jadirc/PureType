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
    private bool _initialized;
    private int _deviceNumber;

    /// <summary>Wird ausgelöst wenn neue Audiodaten verfügbar sind.</summary>
    public event Action<byte[]>? AudioDataAvailable;

    /// <summary>Wird mit dem aktuellen Audio-Pegel (0.0–1.0) ausgelöst.</summary>
    public event Action<double>? AudioLevelChanged;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gibt eine Liste aller verfügbaren Aufnahmegeräte zurück.
    /// </summary>
    public static List<(int Number, string Name)> GetDevices()
    {
        var devices = new List<(int Number, string Name)>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add((i, caps.ProductName));
        }
        return devices;
    }

    /// <summary>
    /// Setzt das Aufnahmegerät. Erfordert erneutes Initialize().
    /// </summary>
    public void SetDevice(int deviceNumber)
    {
        _deviceNumber = deviceNumber;
        if (_initialized)
        {
            Stop();
            _waveIn?.Dispose();
            _waveIn = null;
            _initialized = false;
        }
    }

    /// <summary>
    /// Erstellt das Audio-Device einmalig. Kann mehrfach Start/Stop aufrufen
    /// ohne das Device jedes Mal neu zu öffnen.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        _waveIn = new WaveInEvent
        {
            DeviceNumber = _deviceNumber,
            WaveFormat = new WaveFormat(rate: 16000, bits: 16, channels: 1),
            BufferMilliseconds = 100
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _initialized = true;
    }

    public void Start()
    {
        if (_isRunning) return;
        if (!_initialized) Initialize();
        _waveIn!.StartRecording();
        _isRunning = true;
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _waveIn?.StopRecording();
        _isRunning = false;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var chunk = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, chunk, e.BytesRecorded);
        AudioDataAvailable?.Invoke(chunk);

        // RMS-Pegel berechnen und melden
        double sumSquares = 0;
        int sampleCount = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }
        double rms = Math.Sqrt(sumSquares / sampleCount);
        double level = Math.Min(1.0, rms * 3.0);
        AudioLevelChanged?.Invoke(level);
    }

    public void Dispose()
    {
        Stop();
        if (_initialized)
        {
            _waveIn?.Dispose();
            _waveIn = null;
            _initialized = false;
        }
    }
}
