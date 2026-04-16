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
}
