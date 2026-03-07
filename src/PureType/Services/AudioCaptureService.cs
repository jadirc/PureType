using System.Timers;
using NAudio.Wave;

namespace PureType.Services;

/// <summary>
/// Captures microphone audio using NAudio (16kHz, 16bit, Mono)
/// and delivers PCM chunks via event.
/// </summary>
public class AudioCaptureService : IDisposable
{
    private WaveInEvent? _waveIn;
    private bool _isRunning;
    private bool _initialized;
    private int _deviceNumber;
    private System.Timers.Timer? _devicePollTimer;
    private int _lastDeviceCount;

    /// <summary>Fired when new audio data is available.</summary>
    public event Action<byte[]>? AudioDataAvailable;

    /// <summary>Fired with current audio level (0.0-1.0).</summary>
    public event Action<double>? AudioLevelChanged;

    /// <summary>Fired when the set of available recording devices changes.</summary>
    public event Action<List<(int Number, string Name)>>? DevicesChanged;

    /// <summary>Fired when a device error occurs (e.g. device unplugged while recording).</summary>
    public event Action<string>? DeviceError;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Returns a list of all available recording devices.
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
    /// Sets the recording device. Requires re-initialization.
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
    /// Initializes the audio device once. Supports multiple Start/Stop cycles
    /// without reopening the device.
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
        try
        {
            _waveIn!.StartRecording();
        }
        catch (InvalidOperationException)
        {
            // NAudio may still be in recording state internally — ignore
            return;
        }
        catch (NAudio.MmException ex)
        {
            DeviceError?.Invoke($"Audio device error: {ex.Message}");
            return;
        }
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

        // Calculate and report RMS level
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

    /// <summary>
    /// Starts polling for device changes every 2 seconds.
    /// </summary>
    public void StartDevicePolling()
    {
        if (_devicePollTimer != null) return;
        _lastDeviceCount = WaveInEvent.DeviceCount;
        _devicePollTimer = new System.Timers.Timer(2000);
        _devicePollTimer.Elapsed += OnDevicePollTimerElapsed;
        _devicePollTimer.AutoReset = true;
        _devicePollTimer.Start();
    }

    /// <summary>
    /// Stops polling for device changes.
    /// </summary>
    public void StopDevicePolling()
    {
        if (_devicePollTimer == null) return;
        _devicePollTimer.Stop();
        _devicePollTimer.Elapsed -= OnDevicePollTimerElapsed;
        _devicePollTimer.Dispose();
        _devicePollTimer = null;
    }

    private void OnDevicePollTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            int currentCount = WaveInEvent.DeviceCount;
            if (currentCount != _lastDeviceCount)
            {
                _lastDeviceCount = currentCount;
                var devices = GetDevices();
                DevicesChanged?.Invoke(devices);
            }
        }
        catch (Exception ex)
        {
            DeviceError?.Invoke($"Error polling audio devices: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        StopDevicePolling();
        if (_initialized)
        {
            _waveIn?.Dispose();
            _waveIn = null;
            _initialized = false;
        }
    }
}
