using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class VadServiceTests
{
    private static byte[] CreatePcmChunk(short amplitude, double durationSeconds = 0.1)
    {
        int sampleCount = (int)(16000 * durationSeconds);
        var buffer = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            buffer[i * 2] = (byte)(amplitude & 0xFF);
            buffer[i * 2 + 1] = (byte)((amplitude >> 8) & 0xFF);
        }
        return buffer;
    }

    private static byte[] SilentChunk(double seconds = 0.1) => CreatePcmChunk(0, seconds);
    private static byte[] LoudChunk(double seconds = 0.1) => CreatePcmChunk(8000, seconds);

    [Fact]
    public void SilenceDetected_fires_after_timeout()
    {
        var vad = new VadService(silenceThresholdRms: 0.02, silenceTimeoutSeconds: 0.3);
        vad.Reset();

        bool fired = false;
        vad.SilenceDetected += () => fired = true;

        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(0.5))
        {
            vad.ProcessAudio(SilentChunk());
            Thread.Sleep(50);
        }

        Assert.True(fired);
    }

    [Fact]
    public void SilenceDetected_does_not_fire_during_speech()
    {
        var vad = new VadService(silenceThresholdRms: 0.02, silenceTimeoutSeconds: 0.3);
        vad.Reset();

        bool fired = false;
        vad.SilenceDetected += () => fired = true;

        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(0.5))
        {
            vad.ProcessAudio(LoudChunk());
            Thread.Sleep(50);
        }

        Assert.False(fired);
    }

    [Fact]
    public void SilenceDetected_resets_timer_on_speech()
    {
        var vad = new VadService(silenceThresholdRms: 0.02, silenceTimeoutSeconds: 0.3);
        vad.Reset();

        bool fired = false;
        vad.SilenceDetected += () => fired = true;

        // Silence for 200ms
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(0.2))
        {
            vad.ProcessAudio(SilentChunk());
            Thread.Sleep(50);
        }

        // Loud burst resets timer
        vad.ProcessAudio(LoudChunk());

        // Silence for another 200ms — still under threshold since reset
        start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(0.2))
        {
            vad.ProcessAudio(SilentChunk());
            Thread.Sleep(50);
        }

        Assert.False(fired);
    }

    [Fact]
    public void Reset_restarts_silence_timer()
    {
        var vad = new VadService(silenceThresholdRms: 0.02, silenceTimeoutSeconds: 0.3);

        bool fired = false;
        vad.SilenceDetected += () => fired = true;

        vad.Reset();

        // Brief silence (under timeout)
        vad.ProcessAudio(SilentChunk());
        Assert.False(fired);
    }
}
