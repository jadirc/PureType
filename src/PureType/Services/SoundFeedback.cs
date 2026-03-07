using System.IO;
using System.Media;

namespace PureType.Services;

/// <summary>
/// Generates and plays short audio tones for recording start/stop feedback.
/// Tones are synthesized in memory as WAV — no external audio files needed.
/// Supports 5 selectable tone presets.
/// </summary>
public static class SoundFeedback
{
    private static SoundPlayer? _startPlayer;
    private static SoundPlayer? _stopPlayer;
    private static SoundPlayer? _reconnectPlayer;

    private enum Envelope { Linear, Exponential }
    private enum Pattern { Single, DoublePip }

    private record ToneSpec(int Frequency, int DurationMs, float Volume, Envelope Env, Pattern Pat);

    private static readonly Dictionary<string, (ToneSpec Start, ToneSpec Stop)> Presets = new()
    {
        ["Gentle"]     = (new(880,  80, 0.30f, Envelope.Linear,      Pattern.Single),
                          new(440,  80, 0.30f, Envelope.Linear,      Pattern.Single)),
        ["Click"]      = (new(1200, 30, 0.35f, Envelope.Linear,      Pattern.Single),
                          new(800,  30, 0.35f, Envelope.Linear,      Pattern.Single)),
        ["Bell"]       = (new(1047, 120, 0.30f, Envelope.Exponential, Pattern.Single),
                          new(523,  120, 0.30f, Envelope.Exponential, Pattern.Single)),
        ["Deep"]       = (new(330,  60, 0.35f, Envelope.Linear,      Pattern.Single),
                          new(220,  60, 0.35f, Envelope.Linear,      Pattern.Single)),
        ["Double-Pip"] = (new(660,  60, 0.30f, Envelope.Linear,      Pattern.DoublePip),
                          new(440,  60, 0.30f, Envelope.Linear,      Pattern.DoublePip)),
    };

    private static readonly Dictionary<string, string> LegacyNameMap = new()
    {
        ["Sanft"] = "Gentle",
        ["Klick"] = "Click",
        ["Glocke"] = "Bell",
        ["Tief"] = "Deep",
        ["Doppel-Pip"] = "Double-Pip",
        ["Kein"] = "None",
    };

    public static string[] GetPresetNames() => [.. Presets.Keys];

    public const string NoTonePreset = "None";
    public static string DefaultPreset => "Gentle";

    /// <summary>
    /// Migrates legacy German preset names from old settings files to English.
    /// </summary>
    public static string MigrateName(string name) =>
        LegacyNameMap.TryGetValue(name, out var english) ? english : name;

    /// <summary>
    /// Pre-generates the tone WAVs so playback is instant.
    /// Call once at startup or when preset changes.
    /// </summary>
    public static void Init(string? presetName = null)
    {
        presetName ??= DefaultPreset;

        _startPlayer?.Dispose();
        _stopPlayer?.Dispose();
        _reconnectPlayer?.Dispose();
        _startPlayer = null;
        _stopPlayer = null;
        _reconnectPlayer = null;

        if (presetName == NoTonePreset)
            return;

        if (!Presets.TryGetValue(presetName, out var preset))
            preset = Presets[DefaultPreset];

        _startPlayer = new SoundPlayer(GenerateTone(preset.Start));
        _startPlayer.Load();

        _stopPlayer = new SoundPlayer(GenerateTone(preset.Stop));
        _stopPlayer.Load();

        var reconnectSpec = new ToneSpec(660, 60, 0.25f, Envelope.Linear, Pattern.DoublePip);
        _reconnectPlayer = new SoundPlayer(GenerateTone(reconnectSpec));
        _reconnectPlayer.Load();
    }

    public static void PlayStart() => Task.Run(() => _startPlayer?.Play());

    public static void PlayStop() => Task.Run(() => _stopPlayer?.Play());

    public static void PlayReconnect() => Task.Run(() => _reconnectPlayer?.Play());

    private static MemoryStream GenerateTone(ToneSpec spec)
    {
        const int sampleRate = 44100;
        const int bitsPerSample = 16;
        const int channels = 1;

        int totalSamples;
        if (spec.Pat == Pattern.DoublePip)
        {
            int pipSamples = sampleRate * 15 / 1000;
            int gapSamples = sampleRate * 30 / 1000;
            totalSamples = pipSamples + gapSamples + pipSamples;
        }
        else
        {
            totalSamples = sampleRate * spec.DurationMs / 1000;
        }

        int dataSize = totalSamples * channels * (bitsPerSample / 8);

        var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        // WAV header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                              // chunk size
        writer.Write((short)1);                        // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
        writer.Write((short)(channels * bitsPerSample / 8));     // block align
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);

        if (spec.Pat == Pattern.DoublePip)
            WriteDoublePip(writer, spec, sampleRate, totalSamples);
        else
            WriteSingleTone(writer, spec, sampleRate, totalSamples);

        ms.Position = 0;
        return ms;
    }

    private static void WriteSingleTone(BinaryWriter writer, ToneSpec spec, int sampleRate, int sampleCount)
    {
        int fadeSamples = sampleRate * 5 / 1000; // 5ms fade
        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            double sample = Math.Sin(2.0 * Math.PI * spec.Frequency * t);

            double envelope;
            if (spec.Env == Envelope.Exponential)
            {
                envelope = Math.Exp(-3.0 * i / sampleCount);
                if (i < fadeSamples)
                    envelope *= (double)i / fadeSamples;
            }
            else
            {
                envelope = 1.0;
                if (i < fadeSamples)
                    envelope = (double)i / fadeSamples;
                else if (i > sampleCount - fadeSamples)
                    envelope = (double)(sampleCount - i) / fadeSamples;
            }

            short value = (short)(sample * envelope * spec.Volume * short.MaxValue);
            writer.Write(value);
        }
    }

    private static void WriteDoublePip(BinaryWriter writer, ToneSpec spec, int sampleRate, int totalSamples)
    {
        int pipSamples = sampleRate * 15 / 1000;
        int gapSamples = sampleRate * 30 / 1000;
        int fadeSamples = sampleRate * 2 / 1000; // 2ms fade for short pips

        for (int i = 0; i < totalSamples; i++)
        {
            bool inFirstPip = i < pipSamples;
            bool inGap = i >= pipSamples && i < pipSamples + gapSamples;
            bool inSecondPip = i >= pipSamples + gapSamples;

            if (inGap)
            {
                writer.Write((short)0);
                continue;
            }

            int pipIndex = inFirstPip ? i : i - pipSamples - gapSamples;
            double t = (double)pipIndex / sampleRate;
            double sample = Math.Sin(2.0 * Math.PI * spec.Frequency * t);

            double envelope = 1.0;
            if (pipIndex < fadeSamples)
                envelope = (double)pipIndex / fadeSamples;
            else if (pipIndex > pipSamples - fadeSamples)
                envelope = (double)(pipSamples - pipIndex) / fadeSamples;

            short value = (short)(sample * envelope * spec.Volume * short.MaxValue);
            writer.Write(value);
        }
    }
}
