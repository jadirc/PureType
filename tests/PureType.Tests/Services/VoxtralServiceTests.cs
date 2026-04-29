using PureType.Services;

namespace PureType.Tests.Services;

public class VoxtralServiceTests
{
    [Fact]
    public void BuildWav_produces_valid_wav_header()
    {
        // 4 samples of 16-bit PCM = 8 bytes of audio data
        var pcm = new byte[] { 0x00, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04 };
        var wav = VoxtralService.BuildWav(pcm);

        // WAV = 44-byte header + PCM data
        Assert.Equal(44 + pcm.Length, wav.Length);

        // RIFF header
        Assert.Equal((byte)'R', wav[0]);
        Assert.Equal((byte)'I', wav[1]);
        Assert.Equal((byte)'F', wav[2]);
        Assert.Equal((byte)'F', wav[3]);

        // WAVE format
        Assert.Equal((byte)'W', wav[8]);
        Assert.Equal((byte)'A', wav[9]);
        Assert.Equal((byte)'V', wav[10]);
        Assert.Equal((byte)'E', wav[11]);

        // Audio format = 1 (PCM)
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));

        // Channels = 1
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));

        // Sample rate = 16000
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));

        // Bits per sample = 16
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));

        // Data chunk starts at 36
        Assert.Equal((byte)'d', wav[36]);
        Assert.Equal((byte)'a', wav[37]);
        Assert.Equal((byte)'t', wav[38]);
        Assert.Equal((byte)'a', wav[39]);

        // Data size
        Assert.Equal(pcm.Length, BitConverter.ToInt32(wav, 40));

        // PCM data follows header
        Assert.Equal(pcm, wav[44..]);
    }

    [Fact]
    public void HasSpeech_returns_false_for_silence()
    {
        // 3200 bytes = 1600 samples = 100ms at 16kHz — all zeros
        var silence = new byte[3200];
        Assert.False(VoxtralService.HasSpeech(silence));
    }

    [Fact]
    public void HasSpeech_returns_true_for_loud_audio()
    {
        // Create audio with amplitude above the speech threshold (0.035 RMS)
        // 0.035 * 32768 ≈ 1147 — use amplitude 2000 to be safely above
        var pcm = new byte[6400]; // 200ms = 3200 samples
        for (int i = 0; i < pcm.Length; i += 2)
        {
            short sample = 2000;
            pcm[i] = (byte)(sample & 0xFF);
            pcm[i + 1] = (byte)((sample >> 8) & 0xFF);
        }
        Assert.True(VoxtralService.HasSpeech(pcm));
    }

    [Fact]
    public void HasSpeech_returns_false_for_short_burst()
    {
        // Only 1 chunk of speech (100ms) — need at least 2 (200ms)
        var pcm = new byte[6400]; // 200ms total
        // First 100ms loud, second 100ms silent
        for (int i = 0; i < 3200; i += 2)
        {
            short sample = 2000;
            pcm[i] = (byte)(sample & 0xFF);
            pcm[i + 1] = (byte)((sample >> 8) & 0xFF);
        }
        Assert.False(VoxtralService.HasSpeech(pcm));
    }

    // ── CompressSilence ────────────────────────────────────────────────

    /// <summary>Builds PCM with the given pattern: each segment is (durationMs, isSpeech).</summary>
    private static byte[] BuildPcm(params (int DurationMs, bool IsSpeech)[] segments)
    {
        const int sampleRate = 16000;
        const short speechAmp = 5000; // well above silence threshold (0.01 RMS ≈ 327)
        var ms = new MemoryStream();
        foreach (var (durationMs, isSpeech) in segments)
        {
            int samples = sampleRate * durationMs / 1000;
            for (int i = 0; i < samples; i++)
            {
                short s = isSpeech ? speechAmp : (short)0;
                ms.WriteByte((byte)(s & 0xFF));
                ms.WriteByte((byte)((s >> 8) & 0xFF));
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void CompressSilence_passes_through_audio_without_long_silence()
    {
        // 500ms silence, 500ms speech, 500ms silence — none exceed the 1s compression threshold
        var pcm = BuildPcm((500, false), (500, true), (500, false));
        var compressed = VoxtralService.CompressSilence(pcm);
        Assert.Equal(pcm.Length, compressed.Length);
    }

    [Fact]
    public void CompressSilence_shortens_long_silence_to_max_window()
    {
        // 500ms speech → 3s silence → 500ms speech
        // The 3000ms silence should compress to 300ms; speech preserved
        var pcm = BuildPcm((500, true), (3000, false), (500, true));
        var compressed = VoxtralService.CompressSilence(pcm);

        const int sampleRate = 16000;
        int expectedMs = 500 + 300 + 500;
        int expectedBytes = sampleRate * expectedMs / 1000 * 2;
        Assert.Equal(expectedBytes, compressed.Length);
    }

    [Fact]
    public void CompressSilence_preserves_short_pauses_unchanged()
    {
        // 500ms speech → 800ms silence → 500ms speech (silence < 1000ms threshold)
        var pcm = BuildPcm((500, true), (800, false), (500, true));
        var compressed = VoxtralService.CompressSilence(pcm);
        Assert.Equal(pcm.Length, compressed.Length);
    }

    [Fact]
    public void CompressSilence_handles_multiple_long_silences()
    {
        // speech → long silence → speech → long silence → speech
        var pcm = BuildPcm(
            (500, true), (2000, false),
            (500, true), (2500, false),
            (500, true));
        var compressed = VoxtralService.CompressSilence(pcm);

        const int sampleRate = 16000;
        // Each 2s+ silence → 300ms; speech preserved
        int expectedMs = 500 + 300 + 500 + 300 + 500;
        int expectedBytes = sampleRate * expectedMs / 1000 * 2;
        Assert.Equal(expectedBytes, compressed.Length);
    }

    [Fact]
    public void CompressSilence_compresses_trailing_silence()
    {
        // speech followed by long trailing silence — common when user stops late
        var pcm = BuildPcm((500, true), (2000, false));
        var compressed = VoxtralService.CompressSilence(pcm);

        const int sampleRate = 16000;
        int expectedMs = 500 + 300;
        int expectedBytes = sampleRate * expectedMs / 1000 * 2;
        Assert.Equal(expectedBytes, compressed.Length);
    }

    [Fact]
    public void CompressSilence_compresses_leading_silence()
    {
        // long lead-in silence before speech
        var pcm = BuildPcm((2000, false), (500, true));
        var compressed = VoxtralService.CompressSilence(pcm);

        const int sampleRate = 16000;
        int expectedMs = 300 + 500;
        int expectedBytes = sampleRate * expectedMs / 1000 * 2;
        Assert.Equal(expectedBytes, compressed.Length);
    }

    [Fact]
    public void CompressSilence_preserves_speech_bytes_verbatim()
    {
        // Verify that speech samples are not altered, only silence is shortened
        var speechPcm = BuildPcm((500, true));
        var pcm = BuildPcm((500, true), (2000, false), (500, true));
        var compressed = VoxtralService.CompressSilence(pcm);

        // First 500ms of compressed output must match original speech exactly
        Assert.Equal(speechPcm, compressed[..speechPcm.Length]);
        // Last 500ms (after 300ms compressed silence) must also match
        int sampleRate = 16000;
        int silenceBytes = sampleRate * 300 / 1000 * 2;
        Assert.Equal(speechPcm, compressed[(speechPcm.Length + silenceBytes)..]);
    }
}
