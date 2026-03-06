using Serilog;

namespace VoiceDictation.Services;

/// <summary>
/// Simple energy-based Voice Activity Detection.
/// Detects silence to auto-stop recording.
/// </summary>
public class VadService
{
    private DateTime _lastSpeechTime = DateTime.UtcNow;
    private readonly double _silenceThreshold;
    private readonly TimeSpan _silenceTimeout;

    public event Action? SilenceDetected;

    public VadService(double silenceThresholdRms = 0.02, double silenceTimeoutSeconds = 3.0)
    {
        _silenceThreshold = silenceThresholdRms;
        _silenceTimeout = TimeSpan.FromSeconds(silenceTimeoutSeconds);
    }

    public void Reset()
    {
        _lastSpeechTime = DateTime.UtcNow;
    }

    public void ProcessAudio(byte[] pcmData)
    {
        double sumSquares = 0;
        int sampleCount = pcmData.Length / 2;
        for (int i = 0; i < pcmData.Length - 1; i += 2)
        {
            short sample = (short)(pcmData[i] | (pcmData[i + 1] << 8));
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }
        double rms = Math.Sqrt(sumSquares / sampleCount);

        if (rms > _silenceThreshold)
        {
            _lastSpeechTime = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _lastSpeechTime > _silenceTimeout)
        {
            Log.Debug("VAD: Silence detected after {Seconds}s", _silenceTimeout.TotalSeconds);
            SilenceDetected?.Invoke();
            _lastSpeechTime = DateTime.UtcNow; // prevent repeated firing
        }
    }
}
