# Phase 1 & 2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement Phase 1 (Deepgram Quick Wins) and Phase 2 (Local Whisper Engine) from the feature roadmap, adding Nova-3, interim results, microphone selection, VU meter, keyword boosting, autostart, and a full local Whisper transcription engine with hybrid mode.

**Architecture:** Phase 1 extends the existing `DeepgramService` and `AudioCaptureService` with parameter/UI changes. Phase 2 introduces a new `WhisperService` that implements the same event pattern (`TranscriptReceived`, `ErrorOccurred`) as `DeepgramService`, and a `ITranscriptionProvider` interface so `MainWindow` can switch between providers. Whisper.net (C# bindings for whisper.cpp) provides local transcription. Audio is buffered during recording and transcribed on stop (batch mode).

**Tech Stack:** .NET 8 WPF, NAudio 2.2.1, Whisper.net 1.9.0, Whisper.net.Runtime (CPU), Whisper.net.Runtime.Cuda.Windows (GPU optional), Serilog

**No test project exists.** Tasks that would normally include TDD steps instead include manual verification steps (run the app, test the feature). Consider adding a test project in a future phase.

---

## Phase 1: Quick Wins

### Task 1: Nova-3 Model Upgrade

**Files:**
- Modify: `Services/DeepgramService.cs:42`

**Step 1: Change model parameter**

In `DeepgramService.cs` line 42, change `model=nova-2` to `model=nova-3`:

```csharp
$"&model=nova-3" +
```

**Step 2: Verify**

Run: `dotnet build`
Then launch the app, connect, and dictate. Confirm transcripts still arrive. Check the log window for connection success.

**Step 3: Commit**

```bash
git add Services/DeepgramService.cs
git commit -m "feat: upgrade Deepgram model from nova-2 to nova-3"
```

---

### Task 2: Interim Results (Live Preview)

**Files:**
- Modify: `Services/DeepgramService.cs:45,153-170`
- Modify: `MainWindow.xaml.cs:562-593`
- Modify: `MainWindow.xaml:168-171`

**Step 1: Enable interim_results in Deepgram connection**

In `DeepgramService.cs` line 45, change `interim_results=false` to `interim_results=true`:

```csharp
$"&interim_results=true" +
```

**Step 2: Add `is_final` field to transcript event**

Modify `DeepgramService` to pass whether a result is final. Change the event signature and `ParseAndEmitTranscript`:

```csharp
// Line 19: change event signature
public event Action<string, bool>? TranscriptReceived;  // (text, isFinal)

// Replace ParseAndEmitTranscript (lines 153-170):
private void ParseAndEmitTranscript(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("channel", out var channel)) return;
        if (!channel.TryGetProperty("alternatives", out var alts)) return;
        if (alts.GetArrayLength() == 0) return;

        var transcript = alts[0].GetProperty("transcript").GetString();
        if (string.IsNullOrWhiteSpace(transcript)) return;

        bool isFinal = root.TryGetProperty("is_final", out var finalProp) && finalProp.GetBoolean();

        TranscriptReceived?.Invoke(transcript.Trim(), isFinal);
    }
    catch { /* ungültiges JSON ignorieren */ }
}
```

**Step 3: Update MainWindow to handle interim vs final**

In `MainWindow.xaml.cs`, add a field to track interim text and update the handlers:

```csharp
// Add field after line 35 (_isPttMode):
private string _interimText = "";

// Replace OnTranscriptReceived (lines 563-578):
private void OnTranscriptReceived(string text, bool isFinal)
{
    Dispatcher.BeginInvoke(async () =>
    {
        if (isFinal)
        {
            _interimText = "";
            AppendTranscript(text);
            try
            {
                await KeyboardInjector.TypeTextAsync(text);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler bei Textinjektion");
            }
        }
        else
        {
            _interimText = text;
            ShowInterimTranscript(text);
        }
    });
}
```

Add the `ShowInterimTranscript` method after `AppendTranscript`:

```csharp
private void ShowInterimTranscript(string text)
{
    var current = TranscriptText.Text;
    if (current == "Hier erscheint das erkannte Transkript ...")
        current = "";

    // Show interim text in grey italic by appending to regular text display
    InterimText.Text = text;
}
```

**Step 4: Add InterimText element to XAML**

In `MainWindow.xaml`, inside the transcript Border (after line 171, after the main TextBlock), add:

```xml
<TextBlock x:Name="InterimText"
           Foreground="#6C7086" FontSize="13" FontStyle="Italic"
           TextWrapping="Wrap" LineHeight="20"
           Margin="0,2,0,0"/>
```

Wrap both TextBlocks in a StackPanel inside the ScrollViewer:

```xml
<ScrollViewer Grid.Row="1" x:Name="TranscriptScroll"
              VerticalScrollBarVisibility="Auto"
              Padding="10,4,10,10">
    <StackPanel>
        <TextBlock x:Name="TranscriptText"
                   Foreground="#A6E3A1" FontSize="13"
                   TextWrapping="Wrap" LineHeight="20"
                   Text="Hier erscheint das erkannte Transkript ..."/>
        <TextBlock x:Name="InterimText"
                   Foreground="#6C7086" FontSize="13" FontStyle="Italic"
                   TextWrapping="Wrap" LineHeight="20"/>
    </StackPanel>
</ScrollViewer>
```

**Step 5: Clear interim on recording stop**

In `MainWindow.xaml.cs`, in `StopRecording()` (around line 537), add after `_recording = false;`:

```csharp
_interimText = "";
InterimText.Text = "";
```

**Step 6: Update ConnectAsync event subscription**

In `ConnectAsync()` line 444, the event handler signature now matches `Action<string, bool>` — no change needed since the delegate types already match.

**Step 7: Verify**

Run: `dotnet build && dotnet run`
Connect and dictate. You should see grey italic interim text appearing in real-time, replaced by green final text. Only final text should be typed into the target window.

**Step 8: Commit**

```bash
git add Services/DeepgramService.cs MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add interim results with live preview in transcript"
```

---

### Task 3: Microphone Selection

**Files:**
- Modify: `Services/AudioCaptureService.cs`
- Modify: `MainWindow.xaml` (add ComboBox)
- Modify: `MainWindow.xaml.cs` (populate ComboBox, pass device to service, save/load)

**Step 1: Add device selection to AudioCaptureService**

Replace `AudioCaptureService.cs` with device support:

```csharp
using NAudio.Wave;

namespace VoiceDictation.Services;

public class AudioCaptureService : IDisposable
{
    private WaveInEvent? _waveIn;
    private bool _isRunning;
    private bool _initialized;
    private int _deviceNumber;

    public event Action<byte[]>? AudioDataAvailable;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Returns list of (deviceNumber, productName) for all available input devices.
    /// </summary>
    public static List<(int Number, string Name)> GetDevices()
    {
        var devices = new List<(int, string)>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add((i, caps.ProductName));
        }
        return devices;
    }

    public void SetDevice(int deviceNumber)
    {
        if (_isRunning)
            throw new InvalidOperationException("Cannot change device while recording.");
        if (_initialized)
        {
            _waveIn?.Dispose();
            _waveIn = null;
            _initialized = false;
        }
        _deviceNumber = deviceNumber;
    }

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
```

**Step 2: Add microphone ComboBox to XAML**

In `MainWindow.xaml`, add a new row after the API Key section. Insert two new RowDefinitions after row 6 (API Key):

After the existing row 6 StackPanel (API Key, line ~186), add:

```xml
<!-- Mikrofon -->
<StackPanel Grid.Row="8">
    <TextBlock Text="MIKROFON" Foreground="#BAC2DE"
               FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
    <ComboBox x:Name="MicrophoneCombo" Padding="10,7" FontSize="13"
              SelectionChanged="MicrophoneCombo_SelectionChanged"/>
</StackPanel>
```

Shift all subsequent Grid.Row numbers up by 2 (Language becomes row 10, Mode becomes row 12, etc.). Update the RowDefinitions to add the new row pair.

**Step 3: Populate microphone list and handle selection**

In `MainWindow.xaml.cs`, add to the constructor (after `_audio.AudioDataAvailable += OnAudioData;`):

```csharp
PopulateMicrophones();
```

Add new methods:

```csharp
private void PopulateMicrophones()
{
    var devices = AudioCaptureService.GetDevices();
    MicrophoneCombo.Items.Clear();
    foreach (var (number, name) in devices)
    {
        var item = new System.Windows.Controls.ComboBoxItem
        {
            Content = name,
            Tag = number.ToString()
        };
        MicrophoneCombo.Items.Add(item);
    }
    if (MicrophoneCombo.Items.Count > 0)
        MicrophoneCombo.SelectedIndex = 0;
}

private void MicrophoneCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
{
    if (_isLoading || MicrophoneCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
    if (int.TryParse((string)item.Tag, out int deviceNumber))
    {
        try
        {
            _audio.SetDevice(deviceNumber);
            Log.Information("Mikrofon gewechselt: {Device}", item.Content);
        }
        catch (InvalidOperationException)
        {
            Log.Warning("Mikrofonwechsel nicht möglich während Aufnahme");
        }
    }
    SaveSettings();
}
```

**Step 4: Persist microphone selection**

In `SaveSettings()`, add a line:

```csharp
$"microphone={(string)((MicrophoneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "0")}",
```

In `LoadSettings()` switch, add:

```csharp
case "microphone":
    // Deferred: applied after PopulateMicrophones
    _savedMicrophoneDevice = value;
    break;
```

Add field `private string? _savedMicrophoneDevice;` and apply after `PopulateMicrophones()`:

```csharp
PopulateMicrophones();
if (_savedMicrophoneDevice != null)
{
    SelectComboByTag(MicrophoneCombo, _savedMicrophoneDevice);
    if (int.TryParse(_savedMicrophoneDevice, out int devNum))
        _audio.SetDevice(devNum);
}
```

**Step 5: Verify**

Run: `dotnet build && dotnet run`
Open the app. The MIKROFON dropdown should list available audio input devices. Selecting a different device should take effect on the next recording start.

**Step 6: Commit**

```bash
git add Services/AudioCaptureService.cs MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add microphone device selection dropdown"
```

---

### Task 4: VU Meter

**Files:**
- Modify: `Services/AudioCaptureService.cs` (add RMS calculation)
- Modify: `MainWindow.xaml` (add level bar)
- Modify: `MainWindow.xaml.cs` (update level bar on audio data)

**Step 1: Add audio level event to AudioCaptureService**

In `AudioCaptureService.cs`, add after `AudioDataAvailable` event:

```csharp
/// <summary>Audio level 0.0 - 1.0 (RMS normalized)</summary>
public event Action<double>? AudioLevelChanged;
```

In `OnDataAvailable`, after firing `AudioDataAvailable`, compute and fire RMS:

```csharp
private void OnDataAvailable(object? sender, WaveInEventArgs e)
{
    if (e.BytesRecorded <= 0) return;
    var chunk = new byte[e.BytesRecorded];
    Array.Copy(e.Buffer, chunk, e.BytesRecorded);
    AudioDataAvailable?.Invoke(chunk);

    // Calculate RMS level (16-bit PCM)
    double sumSquares = 0;
    int sampleCount = e.BytesRecorded / 2;
    for (int i = 0; i < e.BytesRecorded - 1; i += 2)
    {
        short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
        double normalized = sample / 32768.0;
        sumSquares += normalized * normalized;
    }
    double rms = Math.Sqrt(sumSquares / sampleCount);
    double level = Math.Min(1.0, rms * 3.0); // amplify for visibility
    AudioLevelChanged?.Invoke(level);
}
```

**Step 2: Add VU meter bar to XAML**

Inside the status Border (row 0 in the Grid, line 125-137), add a level bar below the status text. Replace the status Border content:

```xml
<Border Grid.Row="0" Background="#313244" CornerRadius="8" Padding="12,10">
    <StackPanel>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
            <Ellipse x:Name="StatusDot" Width="12" Height="12"
                     Fill="#F38BA8" VerticalAlignment="Center" Margin="0,0,10,0">
                <Ellipse.Effect>
                    <DropShadowEffect Color="#F38BA8" BlurRadius="8" ShadowDepth="0" Opacity="0.8"/>
                </Ellipse.Effect>
            </Ellipse>
            <TextBlock x:Name="StatusText" Text="Nicht verbunden"
                       Foreground="#CDD6F4" FontSize="14" FontWeight="SemiBold"
                       VerticalAlignment="Center"/>
        </StackPanel>
        <Border Background="#181825" CornerRadius="3" Height="4" Margin="0,8,0,0">
            <Border x:Name="VuMeterBar" Background="#A6E3A1" CornerRadius="3"
                    Height="4" HorizontalAlignment="Left" Width="0"/>
        </Border>
    </StackPanel>
</Border>
```

**Step 3: Wire up level event in MainWindow.xaml.cs**

In the constructor, after `_audio.AudioDataAvailable += OnAudioData;`:

```csharp
_audio.AudioLevelChanged += OnAudioLevel;
```

Add handler:

```csharp
private void OnAudioLevel(double level)
{
    Dispatcher.BeginInvoke(() =>
    {
        var parentWidth = ((Border)VuMeterBar.Parent).ActualWidth;
        VuMeterBar.Width = level * parentWidth;
    });
}
```

In `StopRecording()`, reset the VU meter after stopping:

```csharp
VuMeterBar.Width = 0;
```

**Step 4: Verify**

Run: `dotnet build && dotnet run`
Connect and start recording. The green bar below the status text should animate with your voice level.

**Step 5: Commit**

```bash
git add Services/AudioCaptureService.cs MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add VU meter audio level indicator"
```

---

### Task 5: Keyword Boosting

**Files:**
- Modify: `Services/DeepgramService.cs` (accept keywords parameter)
- Modify: `MainWindow.xaml` (add keywords text field)
- Modify: `MainWindow.xaml.cs` (pass keywords, save/load)

**Step 1: Add keywords parameter to DeepgramService**

Modify constructor and `ConnectAsync`:

```csharp
// Add field after _language:
private readonly string[] _keywords;

// Update constructor:
public DeepgramService(string apiKey, string language = "de", string[]? keywords = null)
{
    _apiKey = apiKey;
    _language = language;
    _keywords = keywords ?? Array.Empty<string>();
}

// In ConnectAsync, build URI with keywords:
var uriBuilder = $"wss://api.deepgram.com/v1/listen" +
    $"?encoding=linear16" +
    $"&sample_rate=16000" +
    $"&channels=1" +
    $"&model=nova-3" +
    $"&language={_language}" +
    $"&smart_format=true" +
    $"&interim_results=true" +
    $"&punctuate=true";

foreach (var kw in _keywords)
{
    if (!string.IsNullOrWhiteSpace(kw))
        uriBuilder += $"&keywords={Uri.EscapeDataString(kw.Trim())}";
}

var uri = new Uri(uriBuilder);
```

**Step 2: Add keywords TextBox to XAML**

Add a new row in the XAML grid (after the microphone section, before Language). Add RowDefinitions for the new row.

```xml
<!-- Keywords -->
<StackPanel Grid.Row="N">
    <TextBlock Text="KEYWORDS (Boost)" Foreground="#BAC2DE"
               FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
    <TextBox x:Name="KeywordsBox"
             Background="#313244" Foreground="#CDD6F4"
             BorderBrush="#45475A" BorderThickness="1"
             Padding="10,8" FontSize="12" FontFamily="Consolas"
             TextWrapping="Wrap" AcceptsReturn="False"
             ToolTip="Kommagetrennte Begriffe, z.B.: Kubernetes, OAuth, Anthropic"
             LostFocus="KeywordsBox_LostFocus"/>
</StackPanel>
```

(Assign correct Grid.Row number based on final row layout.)

**Step 3: Pass keywords to DeepgramService**

In `MainWindow.xaml.cs`, in `ConnectAsync()`, pass keywords when creating the service:

```csharp
var keywords = KeywordsBox.Text
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
_deepgram = new DeepgramService(apiKey, language, keywords);
```

Add handler:

```csharp
private void KeywordsBox_LostFocus(object sender, RoutedEventArgs e)
{
    if (!_isLoading)
        SaveSettings();
}
```

**Step 4: Persist keywords**

In `SaveSettings()`, add:

```csharp
$"keywords={KeywordsBox.Text.Trim()}",
```

In `LoadSettings()` switch, add:

```csharp
case "keywords":
    KeywordsBox.Text = value;
    break;
```

**Step 5: Verify**

Run: `dotnet build && dotnet run`
Enter keywords like "Kubernetes, OAuth, Anthropic". Reconnect. Dictate using those words — they should be recognized more reliably.

**Step 6: Commit**

```bash
git add Services/DeepgramService.cs MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add keyword boosting for Deepgram transcription"
```

---

### Task 6: Autostart with Windows

**Files:**
- Modify: `MainWindow.xaml` (add checkbox)
- Modify: `MainWindow.xaml.cs` (registry-based autostart logic)

**Step 1: Add autostart checkbox to XAML**

Add below the hotkey info section (last row), adding a new RowDefinition:

```xml
<!-- Autostart -->
<CheckBox Grid.Row="N" x:Name="AutostartCheck"
          Content="  Mit Windows starten"
          Foreground="#CDD6F4" FontSize="12"
          Margin="0,4,0,0"
          Checked="AutostartCheck_Changed"
          Unchecked="AutostartCheck_Changed"/>
```

**Step 2: Implement autostart via Registry**

In `MainWindow.xaml.cs`, add:

```csharp
using Microsoft.Win32;

private const string AutostartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
private const string AutostartValueName = "VoiceDictation";

private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
{
    if (_isLoading) return;
    SetAutostart(AutostartCheck.IsChecked == true);
}

private static void SetAutostart(bool enable)
{
    using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey, writable: true);
    if (key == null) return;

    if (enable)
    {
        var exePath = Environment.ProcessPath;
        if (exePath != null)
            key.SetValue(AutostartValueName, $"\"{exePath}\"");
    }
    else
    {
        key.DeleteValue(AutostartValueName, throwOnMissingValue: false);
    }
}

private static bool IsAutostartEnabled()
{
    using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey);
    return key?.GetValue(AutostartValueName) != null;
}
```

In `LoadSettings()`, after loading other settings, set the checkbox:

```csharp
AutostartCheck.IsChecked = IsAutostartEnabled();
```

**Step 3: Verify**

Run: `dotnet build && dotnet run`
Check the autostart checkbox. Open `regedit` and verify `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\VoiceDictation` exists. Uncheck and verify it's removed.

**Step 4: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add autostart with Windows option"
```

---

## Phase 2: Local Whisper Engine

### Task 7: Add Whisper.net NuGet Packages

**Files:**
- Modify: `VoiceDictation.csproj`

**Step 1: Add packages**

```bash
cd D:/work/source/mygit/VoiceDictation
dotnet add package Whisper.net --version 1.9.0
dotnet add package Whisper.net.Runtime --version 1.9.0
```

The CPU runtime is the safe default. CUDA runtime can be added later (Task 12).

**Step 2: Verify build**

Run: `dotnet build`
Should compile without errors.

**Step 3: Commit**

```bash
git add VoiceDictation.csproj
git commit -m "chore: add Whisper.net NuGet packages for local transcription"
```

---

### Task 8: Create ITranscriptionProvider Interface

**Files:**
- Create: `Services/ITranscriptionProvider.cs`

**Step 1: Create the interface**

This interface will be implemented by both `DeepgramService` and the new `WhisperService`:

```csharp
namespace VoiceDictation.Services;

/// <summary>
/// Common interface for transcription providers (Deepgram cloud, Whisper local).
/// </summary>
public interface ITranscriptionProvider : IAsyncDisposable
{
    /// <summary>Fired when a transcript is available. (text, isFinal)</summary>
    event Action<string, bool>? TranscriptReceived;

    /// <summary>Fired on errors.</summary>
    event Action<string>? ErrorOccurred;

    /// <summary>Fired when disconnected unexpectedly.</summary>
    event Action? Disconnected;

    bool IsConnected { get; }

    Task ConnectAsync();

    /// <summary>Send raw PCM-16 audio data (16kHz, mono).</summary>
    Task SendAudioAsync(byte[] audioData);

    /// <summary>Signal end of audio segment (flush buffers).</summary>
    Task SendFinalizeAsync();
}
```

**Step 2: Make DeepgramService implement the interface**

In `DeepgramService.cs`, change class declaration:

```csharp
public class DeepgramService : ITranscriptionProvider
```

No other changes needed — DeepgramService already has all required members.

**Step 3: Verify**

Run: `dotnet build`

**Step 4: Commit**

```bash
git add Services/ITranscriptionProvider.cs Services/DeepgramService.cs
git commit -m "feat: extract ITranscriptionProvider interface from DeepgramService"
```

---

### Task 9: Create WhisperModelManager

**Files:**
- Create: `Services/WhisperModelManager.cs`

**Step 1: Create the model manager**

This class handles downloading and caching Whisper models:

```csharp
using System.IO;
using System.Net.Http;
using Serilog;

namespace VoiceDictation.Services;

/// <summary>
/// Downloads and caches Whisper GGML models from HuggingFace.
/// </summary>
public static class WhisperModelManager
{
    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceDictation", "models");

    public static readonly (string Name, string DisplayName, string Size)[] AvailableModels =
    [
        ("tiny",   "Tiny (schnell, ~75 MB)",    "ggml-tiny.bin"),
        ("base",   "Base (gut, ~142 MB)",        "ggml-base.bin"),
        ("small",  "Small (besser, ~466 MB)",    "ggml-small.bin"),
        ("medium", "Medium (sehr gut, ~1.5 GB)", "ggml-medium.bin"),
        ("large-v3", "Large-v3 (beste, ~3 GB)",  "ggml-large-v3.bin"),
    ];

    public static string GetModelPath(string modelName)
    {
        var model = AvailableModels.FirstOrDefault(m => m.Name == modelName);
        var fileName = model.Size ?? $"ggml-{modelName}.bin";
        return Path.Combine(ModelsDir, fileName);
    }

    public static bool IsModelDownloaded(string modelName)
    {
        return File.Exists(GetModelPath(modelName));
    }

    /// <summary>
    /// Downloads a model from HuggingFace. Reports progress 0.0-1.0.
    /// </summary>
    public static async Task DownloadModelAsync(string modelName, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelsDir);

        var model = AvailableModels.FirstOrDefault(m => m.Name == modelName);
        var fileName = model.Size ?? $"ggml-{modelName}.bin";
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{fileName}";
        var targetPath = Path.Combine(ModelsDir, fileName);
        var tempPath = targetPath + ".tmp";

        Log.Information("Downloading Whisper model {Model} from {Url}", modelName, url);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        long downloaded = 0;

        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;
            if (totalBytes > 0)
                onProgress?.Invoke((double)downloaded / totalBytes);
        }

        // Rename temp to final (atomic on same volume)
        File.Move(tempPath, targetPath, overwrite: true);
        Log.Information("Whisper model {Model} downloaded ({Bytes} bytes)", modelName, downloaded);
    }
}
```

**Step 2: Verify**

Run: `dotnet build`

**Step 3: Commit**

```bash
git add Services/WhisperModelManager.cs
git commit -m "feat: add WhisperModelManager for downloading/caching GGML models"
```

---

### Task 10: Create WhisperService

**Files:**
- Create: `Services/WhisperService.cs`

**Step 1: Create the Whisper transcription service**

This service buffers audio during recording, then transcribes on finalize (batch mode):

```csharp
using System.IO;
using Serilog;
using Whisper.net;

namespace VoiceDictation.Services;

/// <summary>
/// Local transcription using Whisper.net (whisper.cpp).
/// Buffers audio during recording, transcribes on finalize.
/// </summary>
public class WhisperService : ITranscriptionProvider
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private readonly MemoryStream _audioBuffer = new();
    private readonly string _modelName;
    private readonly string _language;
    private bool _connected;

    public event Action<string, bool>? TranscriptReceived;
    public event Action<string>? ErrorOccurred;
    public event Action? Disconnected;

    public bool IsConnected => _connected;

    public WhisperService(string modelName, string language = "de")
    {
        _modelName = modelName;
        _language = string.IsNullOrEmpty(language) ? "auto" : language;
    }

    public async Task ConnectAsync()
    {
        var modelPath = WhisperModelManager.GetModelPath(_modelName);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper-Modell nicht gefunden: {modelPath}. Bitte zuerst herunterladen.");

        await Task.Run(() =>
        {
            _factory = WhisperFactory.FromPath(modelPath);

            var builder = _factory.CreateBuilder()
                .WithLanguage(_language == "auto" ? "auto" : _language);

            _processor = builder.Build();
        });

        _connected = true;
        Log.Information("Whisper-Engine geladen: Modell={Model}, Sprache={Language}", _modelName, _language);
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
        if (!_connected || _processor is null) return;

        byte[] pcmData;
        lock (_audioBuffer)
        {
            pcmData = _audioBuffer.ToArray();
            _audioBuffer.SetLength(0);
        }

        if (pcmData.Length < 3200) // less than 100ms of audio
            return;

        try
        {
            // Convert PCM-16 (16kHz, mono, 16-bit) to float samples
            var samples = ConvertPcm16ToFloat(pcmData);

            var result = new System.Text.StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(new MemoryStream(FloatToBytes(samples))))
            {
                result.Append(segment.Text);
            }

            var text = result.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
                TranscriptReceived?.Invoke(text, true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Whisper-Transkription fehlgeschlagen");
            ErrorOccurred?.Invoke($"Whisper-Fehler: {ex.Message}");
        }
    }

    private static float[] ConvertPcm16ToFloat(byte[] pcmData)
    {
        int sampleCount = pcmData.Length / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
            samples[i] = sample / 32768f;
        }
        return samples;
    }

    private static byte[] FloatToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public async ValueTask DisposeAsync()
    {
        _connected = false;
        _processor?.Dispose();
        _factory?.Dispose();
        _audioBuffer.Dispose();
        Disconnected?.Invoke();
        await Task.CompletedTask;
    }
}
```

**Step 2: Verify**

Run: `dotnet build`

**Step 3: Commit**

```bash
git add Services/WhisperService.cs
git commit -m "feat: add WhisperService for local offline transcription"
```

---

### Task 11: Add Hybrid Mode UI and Provider Switching

**Files:**
- Modify: `MainWindow.xaml` (add provider dropdown, model dropdown, download button)
- Modify: `MainWindow.xaml.cs` (switch between providers, model download UI)

**Step 1: Refactor MainWindow to use ITranscriptionProvider**

In `MainWindow.xaml.cs`, change the `_deepgram` field:

```csharp
// Replace line 15:
private ITranscriptionProvider? _provider;
```

Replace all occurrences of `_deepgram` with `_provider` throughout the file. Key places:
- `ConnectAsync()`: `_provider = new DeepgramService(...)` or `_provider = new WhisperService(...)`
- `DisconnectAsync()`: `await _provider.DisposeAsync()`
- `OnAudioData()`: `await _provider.SendAudioAsync(chunk)`
- `StopRecording()`: `await _provider.SendFinalizeAsync()`

**Step 2: Add provider and model selection to XAML**

Add new rows at the top of the settings area (after the transcript, before API Key). This needs new RowDefinitions.

```xml
<!-- Provider -->
<StackPanel Grid.Row="N">
    <TextBlock Text="PROVIDER" Foreground="#BAC2DE"
               FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
    <ComboBox x:Name="ProviderCombo" Padding="10,7" FontSize="13"
              SelectionChanged="ProviderCombo_SelectionChanged">
        <ComboBoxItem Content="Deepgram (Cloud)" Tag="deepgram" IsSelected="True"/>
        <ComboBoxItem Content="Whisper (Lokal)" Tag="whisper"/>
    </ComboBox>
</StackPanel>

<!-- Whisper Model (visible only when Whisper selected) -->
<StackPanel Grid.Row="N+2" x:Name="WhisperModelPanel" Visibility="Collapsed">
    <TextBlock Text="WHISPER MODELL" Foreground="#BAC2DE"
               FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <ComboBox x:Name="WhisperModelCombo" Grid.Column="0" Padding="10,7" FontSize="13"/>
        <Button x:Name="DownloadModelButton" Grid.Column="1" Content="DL"
                Click="DownloadModelButton_Click"
                Background="#89B4FA" Foreground="#1E1E2E"
                FontWeight="SemiBold" FontSize="12"
                BorderThickness="0" Padding="10,7" Margin="6,0,0,0"
                Cursor="Hand" ToolTip="Modell herunterladen"/>
    </Grid>
    <ProgressBar x:Name="DownloadProgress" Height="4" Margin="0,6,0,0"
                 Visibility="Collapsed" Minimum="0" Maximum="100"
                 Background="#181825" Foreground="#A6E3A1"/>
</StackPanel>
```

**Step 3: Populate model list and handle provider switching**

In `MainWindow.xaml.cs`:

```csharp
private void PopulateWhisperModels()
{
    WhisperModelCombo.Items.Clear();
    foreach (var (name, displayName, _) in WhisperModelManager.AvailableModels)
    {
        var suffix = WhisperModelManager.IsModelDownloaded(name) ? " [OK]" : "";
        var item = new System.Windows.Controls.ComboBoxItem
        {
            Content = displayName + suffix,
            Tag = name
        };
        WhisperModelCombo.Items.Add(item);
    }
    if (WhisperModelCombo.Items.Count > 0)
        WhisperModelCombo.SelectedIndex = 0;
}

private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
{
    if (_isLoading || ProviderCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
    var provider = (string)item.Tag;
    bool isWhisper = provider == "whisper";

    WhisperModelPanel.Visibility = isWhisper ? Visibility.Visible : Visibility.Collapsed;

    // API Key only needed for Deepgram
    // (Keep visible but could be dimmed for Whisper)

    if (!_isLoading)
        SaveSettings();
}

private async void DownloadModelButton_Click(object sender, RoutedEventArgs e)
{
    if (WhisperModelCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
    var modelName = (string)item.Tag;

    if (WhisperModelManager.IsModelDownloaded(modelName))
    {
        MessageBox.Show("Modell ist bereits heruntergeladen.", "Info",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return;
    }

    DownloadModelButton.IsEnabled = false;
    DownloadProgress.Visibility = Visibility.Visible;
    DownloadProgress.Value = 0;

    try
    {
        await WhisperModelManager.DownloadModelAsync(modelName,
            progress => Dispatcher.Invoke(() => DownloadProgress.Value = progress * 100));

        PopulateWhisperModels(); // refresh [OK] markers
        MessageBox.Show($"Modell '{modelName}' erfolgreich heruntergeladen.", "Fertig",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Model download failed");
        MessageBox.Show($"Download fehlgeschlagen:\n{ex.Message}", "Fehler",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
    finally
    {
        DownloadModelButton.IsEnabled = true;
        DownloadProgress.Visibility = Visibility.Collapsed;
    }
}
```

**Step 4: Update ConnectAsync to create the right provider**

```csharp
private async Task ConnectAsync()
{
    var providerItem = (System.Windows.Controls.ComboBoxItem)ProviderCombo.SelectedItem;
    var providerType = (string)providerItem.Tag;

    SetStatus("Verbinde ...", Yellow);
    ConnectButton.IsEnabled = false;

    try
    {
        var langItem = (System.Windows.Controls.ComboBoxItem)LanguageCombo.SelectedItem;
        var language = (string)langItem.Tag;

        if (providerType == "whisper")
        {
            var modelItem = WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var modelName = (string)(modelItem?.Tag ?? "tiny");

            if (!WhisperModelManager.IsModelDownloaded(modelName))
            {
                MessageBox.Show("Bitte zuerst das Whisper-Modell herunterladen.", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ConnectButton.IsEnabled = true;
                SetStatus("Nicht verbunden", Red);
                return;
            }

            _provider = new WhisperService(modelName, language);
        }
        else
        {
            var apiKey = ApiKeyBox.Password.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("Bitte einen Deepgram API Key eingeben.", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ConnectButton.IsEnabled = true;
                SetStatus("Nicht verbunden", Red);
                return;
            }
            var keywords = KeywordsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _provider = new DeepgramService(apiKey, language, keywords);
        }

        _provider.TranscriptReceived += OnTranscriptReceived;
        _provider.ErrorOccurred      += OnError;
        _provider.Disconnected       += OnDisconnected;

        await _provider.ConnectAsync();

        _connected = true;
        _audio.Initialize();
        RegisterHotkeys();

        var label = providerType == "whisper" ? "Whisper (lokal)" : "Deepgram";
        SetStatus($"Verbunden – {label}", Green);
        ConnectButton.Content = "Trennen";
        ConnectButton.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
        SaveSettings();
        Log.Information("{Provider} verbunden (Sprache: {Language})", label, language);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Verbindung fehlgeschlagen");
        SetStatus("Verbindung fehlgeschlagen", Red);
        MessageBox.Show($"Fehler:\n{ex.Message}", "Verbindungsfehler",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
    finally
    {
        ConnectButton.IsEnabled = true;
    }
}
```

**Step 5: Persist provider and model settings**

In `SaveSettings()`, add:

```csharp
$"provider={(string)((ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "deepgram")}",
$"whisper_model={(string)((WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "tiny")}",
```

In `LoadSettings()` switch, add:

```csharp
case "provider":
    SelectComboByTag(ProviderCombo, value);
    break;
case "whisper_model":
    // Deferred until PopulateWhisperModels runs
    _savedWhisperModel = value;
    break;
```

Add field `private string? _savedWhisperModel;` and apply after `PopulateWhisperModels()`.

**Step 6: Initialize in constructor**

In the constructor, add after `PopulateMicrophones()`:

```csharp
PopulateWhisperModels();
if (_savedWhisperModel != null)
    SelectComboByTag(WhisperModelCombo, _savedWhisperModel);

// Show/hide Whisper panel based on saved provider
var provItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
WhisperModelPanel.Visibility = (string?)provItem?.Tag == "whisper"
    ? Visibility.Visible : Visibility.Collapsed;
```

**Step 7: Verify**

Run: `dotnet build && dotnet run`

1. Select "Whisper (Lokal)" provider
2. Select "Tiny" model and click "DL" to download
3. Wait for download to complete
4. Click "Verbinden"
5. Press F9, dictate, press F9 again
6. After a brief pause, the transcription should appear
7. Switch back to "Deepgram (Cloud)" and verify it still works

**Step 8: Commit**

```bash
git add Services/ITranscriptionProvider.cs Services/WhisperService.cs MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add hybrid mode with provider switching (Deepgram/Whisper)"
```

---

### Task 12: GPU Acceleration (Optional)

**Files:**
- Modify: `VoiceDictation.csproj`
- Modify: `Services/WhisperService.cs`

**Step 1: Add CUDA runtime package**

```bash
dotnet add package Whisper.net.Runtime.Cuda.Windows --version 1.9.0-preview1
```

Note: This is a large package (~500MB). Consider making this a conditional build configuration in the future.

**Step 2: Add GPU detection to WhisperService**

The Whisper.net library auto-selects the best available runtime (CUDA > CPU). No code change needed — if the CUDA runtime package is present and a compatible GPU is found, it will be used automatically.

Add a log line in `ConnectAsync()` after loading the model:

```csharp
Log.Information("Whisper runtime: GPU wird automatisch verwendet wenn verfügbar");
```

**Step 3: Verify**

Run: `dotnet build && dotnet run`
With an NVIDIA GPU, check logs for CUDA usage. Without GPU, it falls back to CPU transparently.

**Step 4: Commit**

```bash
git add VoiceDictation.csproj Services/WhisperService.cs
git commit -m "feat: add CUDA GPU acceleration support for Whisper"
```

---

### Task 13: Voice Activity Detection (VAD)

**Files:**
- Create: `Services/VadService.cs`
- Modify: `MainWindow.xaml.cs` (integrate VAD for auto-stop)
- Modify: `MainWindow.xaml` (add VAD toggle)

**Step 1: Create energy-based VAD service**

```csharp
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

    /// <summary>Fired when silence timeout is reached.</summary>
    public event Action? SilenceDetected;

    /// <param name="silenceThresholdRms">RMS level below which audio is considered silence (0.0-1.0)</param>
    /// <param name="silenceTimeoutSeconds">Seconds of silence before triggering auto-stop</param>
    public VadService(double silenceThresholdRms = 0.02, double silenceTimeoutSeconds = 3.0)
    {
        _silenceThreshold = silenceThresholdRms;
        _silenceTimeout = TimeSpan.FromSeconds(silenceTimeoutSeconds);
    }

    public void Reset()
    {
        _lastSpeechTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Feed PCM-16 audio data (16kHz, mono, 16-bit).
    /// </summary>
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
            Log.Debug("VAD: Stille erkannt nach {Seconds}s", _silenceTimeout.TotalSeconds);
            SilenceDetected?.Invoke();
            _lastSpeechTime = DateTime.UtcNow; // prevent repeated firing
        }
    }
}
```

**Step 2: Add VAD toggle to XAML**

Add a checkbox near the autostart checkbox:

```xml
<CheckBox Grid.Row="N" x:Name="VadCheck"
          Content="  Auto-Stop bei Stille (3s)"
          Foreground="#CDD6F4" FontSize="12"
          Margin="0,4,0,0"
          Checked="VadCheck_Changed"
          Unchecked="VadCheck_Changed"/>
```

**Step 3: Integrate VAD into MainWindow**

Add field:

```csharp
private VadService? _vad;
```

In `StartRecording()`:

```csharp
if (VadCheck.IsChecked == true)
{
    _vad = new VadService();
    _vad.SilenceDetected += () => Dispatcher.Invoke(StopRecording);
    _vad.Reset();
}
```

In `OnAudioData()`:

```csharp
private async void OnAudioData(byte[] chunk)
{
    if (_provider is null || !_recording) return;
    await _provider.SendAudioAsync(chunk);
    _vad?.ProcessAudio(chunk);
}
```

In `StopRecording()`:

```csharp
_vad = null;
```

Persist VAD setting in save/load:

```csharp
// SaveSettings:
$"vad={VadCheck.IsChecked == true}",

// LoadSettings:
case "vad":
    VadCheck.IsChecked = value.Equals("True", StringComparison.OrdinalIgnoreCase);
    break;
```

**Step 4: Verify**

Run: `dotnet build && dotnet run`
Enable "Auto-Stop bei Stille". Start recording, speak, then go silent for 3 seconds. Recording should stop automatically.

**Step 5: Commit**

```bash
git add Services/VadService.cs MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add voice activity detection with auto-stop on silence"
```

---

## Summary: Task Execution Order

| # | Task | Phase | Effort |
|---|------|-------|--------|
| 1 | Nova-3 Model Upgrade | 1 | ~2 min |
| 2 | Interim Results (Live Preview) | 1 | ~20 min |
| 3 | Microphone Selection | 1 | ~20 min |
| 4 | VU Meter | 1 | ~15 min |
| 5 | Keyword Boosting | 1 | ~15 min |
| 6 | Autostart with Windows | 1 | ~10 min |
| 7 | Add Whisper.net NuGet Packages | 2 | ~2 min |
| 8 | ITranscriptionProvider Interface | 2 | ~10 min |
| 9 | WhisperModelManager | 2 | ~15 min |
| 10 | WhisperService | 2 | ~20 min |
| 11 | Hybrid Mode UI + Provider Switching | 2 | ~30 min |
| 12 | GPU Acceleration (Optional) | 2 | ~5 min |
| 13 | Voice Activity Detection (VAD) | 2 | ~15 min |

**Important notes for the implementer:**

1. **XAML Row Numbers:** Tasks 2-6 and 11 all add new rows to the XAML grid. Each task shifts subsequent row numbers. Track the current row count carefully. Consider implementing Tasks 2-6 together to get the XAML layout finalized once.

2. **The Whisper.net `ProcessAsync` API** expects audio as a stream of float32 samples at 16kHz. The `WhisperService` converts from the app's PCM-16 format. Verify the exact API against the [Whisper.net README](https://github.com/sandrohanea/whisper.net) at implementation time, as the API may have changed.

3. **Model download sizes** can be large (75MB to 3GB). The download manager uses a temp file + rename pattern to prevent corrupt partial downloads.

4. **Settings file migration:** No breaking changes — new keys are simply appended. Old settings files work fine (missing keys use defaults).
