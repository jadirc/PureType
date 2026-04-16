# Voxtral STT Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Mistral Voxtral as a third speech-to-text provider (cloud batch mode) alongside Deepgram and Whisper.

**Architecture:** New `VoxtralService : ITranscriptionProvider` that buffers PCM audio during recording, packages it as WAV on finalize, and uploads to the Mistral REST API (`/v1/audio/transcriptions`). API key is resolved from the existing LLM endpoint keys. UI gets a third provider option with a model selector dropdown.

**Tech Stack:** .NET 8, WPF, HttpClient, System.Text.Json

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `src/PureType/Services/VoxtralService.cs` | ITranscriptionProvider implementation: audio buffering, WAV packaging, Mistral API upload, silence detection, timing |
| Modify | `src/PureType/Services/SettingsService.cs` | Add `VoxtralModel` field to `TranscriptionSettings` |
| Modify | `src/PureType/MainWindow.xaml` | Add "Voxtral (Cloud)" ComboBoxItem to ProviderCombo |
| Modify | `src/PureType/MainWindow.xaml.cs` | Add voxtral branch in `ConnectAsync`, API key resolution from EndpointKeys |
| Modify | `src/PureType/SettingsWindow.xaml` | Add "Voxtral (Cloud)" ComboBoxItem + VoxtralModelPanel with model dropdown |
| Modify | `src/PureType/SettingsWindow.xaml.cs` | Extend `SetProviderVisibility`, `Save_Click`, `PopulateFromSettings`, `RestoreDefaultVisibility` |
| Create | `tests/PureType.Tests/Services/VoxtralServiceTests.cs` | Unit tests for WAV packaging, silence detection, settings round-trip |

---

### Task 1: Add VoxtralModel to TranscriptionSettings

**Files:**
- Modify: `src/PureType/Services/SettingsService.cs:21-28`
- Test: `tests/PureType.Tests/Services/SettingsServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add a test to `tests/PureType.Tests/Services/SettingsServiceTests.cs`:

```csharp
[Fact]
public void VoxtralModel_defaults_to_mistral_small_latest()
{
    var settings = new TranscriptionSettings();
    Assert.Equal("mistral-small-latest", settings.VoxtralModel);
}

[Fact]
public void VoxtralModel_round_trips_through_json()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "settings.json");
    try
    {
        var svc = new SettingsService(path);
        var original = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                Provider = "voxtral",
                VoxtralModel = "mistral-medium-latest",
            }
        };
        svc.Save(original);
        var loaded = svc.Load();
        Assert.Equal("voxtral", loaded.Transcription.Provider);
        Assert.Equal("mistral-medium-latest", loaded.Transcription.VoxtralModel);
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "VoxtralModel" -v minimal`
Expected: FAIL — `TranscriptionSettings` does not have a `VoxtralModel` property.

- [ ] **Step 3: Add VoxtralModel property**

In `src/PureType/Services/SettingsService.cs`, add to the `TranscriptionSettings` record (after `WhisperTuning`):

```csharp
public record TranscriptionSettings
{
    public string Language { get; init; } = "de";
    public string Provider { get; init; } = "deepgram";
    public string ApiKey { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string WhisperModel { get; init; } = "tiny";
    public WhisperTuningSettings WhisperTuning { get; init; } = new();
    public string VoxtralModel { get; init; } = "mistral-small-latest";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "VoxtralModel" -v minimal`
Expected: PASS (both tests)

- [ ] **Step 5: Commit**

```bash
git add src/PureType/Services/SettingsService.cs tests/PureType.Tests/Services/SettingsServiceTests.cs
git commit -m "feat: add VoxtralModel to TranscriptionSettings"
```

---

### Task 2: Create VoxtralService core (WAV packaging + silence detection)

**Files:**
- Create: `src/PureType/Services/VoxtralService.cs`
- Create: `tests/PureType.Tests/Services/VoxtralServiceTests.cs`

- [ ] **Step 1: Write failing tests for WAV header and silence detection**

Create `tests/PureType.Tests/Services/VoxtralServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "VoxtralService" -v minimal`
Expected: FAIL — `VoxtralService` class does not exist.

- [ ] **Step 3: Implement VoxtralService**

Create `src/PureType/Services/VoxtralService.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Serilog;

namespace PureType.Services;

public class VoxtralService : ITranscriptionProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private string _language;
    private readonly HttpClient _http = new();
    private readonly MemoryStream _audioBuffer = new();
    private bool _connected;

    public event Action<string, bool>? TranscriptReceived;
    public event Action<string>? ErrorOccurred;
    public event Action? Disconnected;
    public event Action? SilenceSkipped;
    public event Action<TimeSpan>? TranscriptionTimed;

    public bool IsConnected => _connected;

    public VoxtralService(string apiKey, string model, string language = "de")
    {
        _apiKey = apiKey;
        _model = model;
        _language = string.IsNullOrEmpty(language) ? "auto" : language;
    }

    public Task ConnectAsync()
    {
        _connected = true;
        Log.Information("VoxtralService ready: Model={Model}, Language={Language}", _model, _language);
        return Task.CompletedTask;
    }

    public Task SendAudioAsync(byte[] audioData)
    {
        if (!_connected) return Task.CompletedTask;
        lock (_audioBuffer)
        {
            _audioBuffer.Write(audioData, 0, audioData.Length);
        }
        return Task.CompletedTask;
    }

    public async Task SendFinalizeAsync()
    {
        if (!_connected) return;

        byte[] pcmData;
        lock (_audioBuffer)
        {
            pcmData = _audioBuffer.ToArray();
            _audioBuffer.SetLength(0);
        }

        if (pcmData.Length < 3200) // less than 100ms
            return;

        Log.Debug("Voxtral: processing {Bytes} bytes ({Seconds:F1}s) of audio",
            pcmData.Length, pcmData.Length / 2.0 / 16000);

        if (!HasSpeech(pcmData))
        {
            Log.Debug("Voxtral: skipping likely silence");
            SilenceSkipped?.Invoke();
            return;
        }

        var wav = BuildWav(pcmData);

        try
        {
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(wav);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(fileContent, "file", "audio.wav");
            content.Add(new StringContent(_model), "model");
            if (_language != "auto")
                content.Add(new StringContent(_language), "language");

            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.mistral.ai/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = content;

            var response = await _http.SendAsync(request, cts.Token);
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var msg = $"Voxtral API error {(int)response.StatusCode}: {json}";
                Log.Error(msg);
                ErrorOccurred?.Invoke(msg);
                return;
            }

            var text = ParseTranscript(json);
            Log.Debug("Voxtral result ({Elapsed}ms): \"{Text}\"", sw.ElapsedMilliseconds, text);
            TranscriptionTimed?.Invoke(sw.Elapsed);

            if (!string.IsNullOrWhiteSpace(text))
                TranscriptReceived?.Invoke(text.Trim(), true);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Voxtral transcription timed out after 30s");
            ErrorOccurred?.Invoke("Voxtral timed out — try again");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Voxtral transcription failed");
            ErrorOccurred?.Invoke($"Voxtral error: {ex.Message}");
        }
    }

    public Task SetLanguageAsync(string language)
    {
        _language = string.IsNullOrEmpty(language) ? "auto" : language;
        Log.Information("Voxtral language changed to {Language}", _language);
        return Task.CompletedTask;
    }

    internal static string ParseTranscript(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("text").GetString() ?? "";
    }

    internal static byte[] BuildWav(byte[] pcmData)
    {
        var wav = new byte[44 + pcmData.Length];
        var ms = new MemoryStream(wav);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + pcmData.Length);     // file size - 8
        bw.Write("WAVE"u8);

        // fmt chunk
        bw.Write("fmt "u8);
        bw.Write(16);                      // chunk size
        bw.Write((short)1);                // audio format: PCM
        bw.Write((short)1);                // channels: mono
        bw.Write(16000);                   // sample rate
        bw.Write(16000 * 1 * 16 / 8);     // byte rate
        bw.Write((short)(1 * 16 / 8));    // block align
        bw.Write((short)16);              // bits per sample

        // data chunk
        bw.Write("data"u8);
        bw.Write(pcmData.Length);
        bw.Write(pcmData);

        return wav;
    }

    internal static bool HasSpeech(byte[] pcmData)
    {
        const float SpeechRmsThreshold = 0.035f;
        const int chunkSamples = 1600; // 100ms at 16kHz
        const int minSpeechChunks = 2;

        int sampleCount = pcmData.Length / 2;
        int speechChunkCount = 0;

        for (int offset = 0; offset < sampleCount; offset += chunkSamples)
        {
            int end = Math.Min(offset + chunkSamples, sampleCount);
            float chunkSumSq = 0;
            for (int k = offset; k < end; k++)
            {
                short s = (short)(pcmData[k * 2] | (pcmData[k * 2 + 1] << 8));
                float sample = s / 32768f;
                chunkSumSq += sample * sample;
            }
            float chunkRms = (float)Math.Sqrt(chunkSumSq / (end - offset));
            if (chunkRms >= SpeechRmsThreshold)
                speechChunkCount++;
            if (speechChunkCount >= minSpeechChunks)
                return true;
        }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        _http.Dispose();
        _audioBuffer.Dispose();
        Disconnected?.Invoke();
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "VoxtralService" -v minimal`
Expected: PASS (all 4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/PureType/Services/VoxtralService.cs tests/PureType.Tests/Services/VoxtralServiceTests.cs
git commit -m "feat: add VoxtralService with WAV packaging and silence detection"
```

---

### Task 3: Add Voxtral to MainWindow provider selection

**Files:**
- Modify: `src/PureType/MainWindow.xaml:74-83`
- Modify: `src/PureType/MainWindow.xaml.cs:625-718`

- [ ] **Step 1: Add ComboBoxItem to MainWindow.xaml**

In `src/PureType/MainWindow.xaml`, after the existing two ComboBoxItems in the ProviderCombo (around line 80-81):

```xml
<ComboBox x:Name="ProviderCombo" Padding="10,7" FontSize="13"
          SelectionChanged="ProviderCombo_SelectionChanged">
    <ComboBoxItem Content="Deepgram (Cloud)" Tag="deepgram" IsSelected="True"/>
    <ComboBoxItem Content="Whisper (Local)" Tag="whisper"/>
    <ComboBoxItem Content="Voxtral (Cloud)" Tag="voxtral"/>
</ComboBox>
```

- [ ] **Step 2: Add voxtral branch to ConnectAsync**

In `src/PureType/MainWindow.xaml.cs`, the `ConnectAsync` method currently has:

```csharp
if (providerType == "whisper")
{
    // ... whisper setup ...
}
else
{
    // ... deepgram setup ...
}
```

Change to:

```csharp
if (providerType == "whisper")
{
    var modelName = _settings.Transcription.WhisperModel;

    if (!WhisperModelManager.IsModelDownloaded(modelName))
    {
        MessageBox.Show("Please download the Whisper model first.", "Error",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        ConnectButton.IsEnabled = true;
        SetStatus("Not connected", Red);
        return;
    }

    var whisperKeywords = _settings.Transcription.Keywords;
    var tuning = _settings.Transcription.WhisperTuning;
    Log.Information("Creating WhisperService with keywords={Keywords}, sampling={Sampling}, beamSize={BeamSize}, entropy={Entropy}",
        whisperKeywords, tuning.SamplingStrategy, tuning.BeamSize, tuning.EntropyThreshold);
    _provider = new WhisperService(modelName, language, whisperKeywords, tuning);
}
else if (providerType == "voxtral")
{
    var mistralKey = ResolveMistralApiKey();
    if (string.IsNullOrEmpty(mistralKey))
    {
        MessageBox.Show("Please configure a Mistral API key in AI Post-Processing settings first.",
            "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        ConnectButton.IsEnabled = true;
        SetStatus("Not connected", Red);
        return;
    }

    var model = _settings.Transcription.VoxtralModel;
    Log.Information("Creating VoxtralService with model={Model}", model);
    _provider = new VoxtralService(mistralKey, model, language);
}
else
{
    var apiKey = _settings.Transcription.ApiKey;
    if (string.IsNullOrEmpty(apiKey))
    {
        MessageBox.Show("Please enter a Deepgram API key.", "Error",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        ConnectButton.IsEnabled = true;
        SetStatus("Not connected", Red);
        return;
    }
    var keywords = _settings.Transcription.Keywords
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .ToArray();
    _provider = new DeepgramService(apiKey, language, keywords.Length > 0 ? keywords : null);
}
```

- [ ] **Step 3: Add SilenceSkipped handler for Voxtral**

In `ConnectAsync`, after the existing `if (_provider is WhisperService whisper)` block (around line 677-681), add:

```csharp
if (_provider is VoxtralService voxtral)
{
    voxtral.SilenceSkipped += () => Dispatcher.Invoke(() =>
        ToastWindow.ShowToast("No speech detected", Colors.Orange, true));
}
```

- [ ] **Step 4: Update the status label**

In `ConnectAsync`, change the label assignment (around line 697):

```csharp
var label = providerType switch
{
    "whisper" => "Whisper (local)",
    "voxtral" => "Voxtral (cloud)",
    _ => "Deepgram",
};
```

- [ ] **Step 5: Add ResolveMistralApiKey helper**

Add this private method to MainWindow.xaml.cs (near the ConnectAsync region):

```csharp
private string? ResolveMistralApiKey()
{
    foreach (var kvp in _settings.Llm.EndpointKeys)
    {
        if (kvp.Key.Contains("api.mistral.ai", StringComparison.OrdinalIgnoreCase))
            return kvp.Value;
    }
    return null;
}
```

- [ ] **Step 6: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/PureType/MainWindow.xaml src/PureType/MainWindow.xaml.cs
git commit -m "feat: add Voxtral provider to MainWindow connect flow"
```

---

### Task 4: Add Voxtral to SettingsWindow

**Files:**
- Modify: `src/PureType/SettingsWindow.xaml:140-158`
- Modify: `src/PureType/SettingsWindow.xaml.cs:62-66, 146-154, 170-194, 809-821`

- [ ] **Step 1: Add ComboBoxItem and VoxtralModelPanel to SettingsWindow.xaml**

In `src/PureType/SettingsWindow.xaml`, add the third ComboBoxItem to the ProviderCombo (after line 143):

```xml
<ComboBox x:Name="ProviderCombo" Padding="10,7" FontSize="13"
          SelectionChanged="ProviderCombo_SelectionChanged">
    <ComboBoxItem Content="Deepgram (Cloud)" Tag="deepgram" IsSelected="True"/>
    <ComboBoxItem Content="Whisper (Local)" Tag="whisper"/>
    <ComboBoxItem Content="Voxtral (Cloud)" Tag="voxtral"/>
</ComboBox>
```

Add the VoxtralModelPanel right after the ApiKeyPanel (after line 158, before the WhisperModelPanel):

```xml
<!-- Voxtral Model (Voxtral only) -->
<StackPanel x:Name="VoxtralModelPanel" Visibility="Collapsed" Margin="0,0,0,12"
            Tag="voxtral model mistral cloud speech"
            ToolTip="Choose a Voxtral speech recognition model. Larger models are more accurate but slower.">
    <TextBlock Text="VOXTRAL MODEL" Foreground="{DynamicResource LabelBrush}"
               FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
    <ComboBox x:Name="VoxtralModelCombo" Padding="10,7" FontSize="13">
        <ComboBoxItem Content="Small — fast, good quality" Tag="mistral-small-latest" IsSelected="True"/>
        <ComboBoxItem Content="Medium — higher quality, slower" Tag="mistral-medium-latest"/>
    </ComboBox>
</StackPanel>
```

- [ ] **Step 2: Update SetProviderVisibility**

In `src/PureType/SettingsWindow.xaml.cs`, replace the `SetProviderVisibility` method (lines 146-154):

```csharp
private void SetProviderVisibility(string providerTag)
{
    WhisperModelPanel.Visibility = providerTag == "whisper" ? Visibility.Visible : Visibility.Collapsed;
    WhisperTuningHeader.Visibility = providerTag == "whisper" ? Visibility.Visible : Visibility.Collapsed;
    WhisperTuningPanel.Visibility = providerTag == "whisper" ? Visibility.Visible : Visibility.Collapsed;
    ApiKeyPanel.Visibility = providerTag == "deepgram" ? Visibility.Visible : Visibility.Collapsed;
    VoxtralModelPanel.Visibility = providerTag == "voxtral" ? Visibility.Visible : Visibility.Collapsed;
    KeywordsPanel.Visibility = providerTag != "voxtral" ? Visibility.Visible : Visibility.Collapsed;
}
```

- [ ] **Step 3: Update PopulateFromSettings to set VoxtralModel combo**

In `PopulateFromSettings`, after line 67 (`KeywordsBox.Text = ...`), add:

```csharp
UiHelper.SelectComboByTag(VoxtralModelCombo, settings.Transcription.VoxtralModel);
```

- [ ] **Step 4: Update Save_Click to save VoxtralModel**

In the `Save_Click` method, add a variable for the voxtral model item (after line 174):

```csharp
var voxtralModelItem = VoxtralModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
```

Then in the `TranscriptionSettings` constructor (after the `WhisperTuning` block, around line 193):

```csharp
VoxtralModel = (string)(voxtralModelItem?.Tag ?? "mistral-small-latest"),
```

- [ ] **Step 5: Update RestoreDefaultVisibility**

In `RestoreDefaultVisibility` (around line 809-821), replace the provider-visibility block:

```csharp
if (fe == WhisperModelPanel || fe == ApiKeyPanel || fe == WhisperTuningPanel || fe == VoxtralModelPanel)
{
    var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
    var providerTag = (string)(providerItem?.Tag ?? "deepgram");
    if (fe == WhisperModelPanel || fe == WhisperTuningPanel)
        fe.Visibility = providerTag == "whisper" ? Visibility.Visible : Visibility.Collapsed;
    else if (fe == VoxtralModelPanel)
        fe.Visibility = providerTag == "voxtral" ? Visibility.Visible : Visibility.Collapsed;
    else // ApiKeyPanel
        fe.Visibility = providerTag == "deepgram" ? Visibility.Visible : Visibility.Collapsed;
    return;
}
```

- [ ] **Step 6: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Run full test suite**

Run: `dotnet test -v minimal`
Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/PureType/SettingsWindow.xaml src/PureType/SettingsWindow.xaml.cs
git commit -m "feat: add Voxtral provider UI to SettingsWindow"
```

---

### Task 5: Final verification and full test run

**Files:** None (verification only)

- [ ] **Step 1: Run full build**

Run: `dotnet build`
Expected: Build succeeded with 0 errors, 0 warnings.

- [ ] **Step 2: Run all tests**

Run: `dotnet test -v minimal`
Expected: All tests pass.

- [ ] **Step 3: Verify settings round-trip**

Run: `dotnet test --filter "VoxtralModel" -v normal`
Expected: Both VoxtralModel tests pass.

- [ ] **Step 4: Verify VoxtralService tests**

Run: `dotnet test --filter "VoxtralService" -v normal`
Expected: All 4 VoxtralService tests pass (BuildWav, HasSpeech silence, HasSpeech loud, HasSpeech short burst).
