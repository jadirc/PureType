# Auto-Reconnect & Unit Tests Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add automatic WebSocket reconnection with exponential backoff to DeepgramService, and add unit tests for ReplacementService, VadService, UiHelper, and SettingsService.

**Architecture:** Reconnect logic lives inside DeepgramService's ReceiveLoopAsync. A new `Reconnecting` event lets MainWindow show status. Unit tests use xUnit with temp files and synthetic PCM data — no mocking framework.

**Tech Stack:** .NET 8, WPF, xUnit, ClientWebSocket

---

### Task 1: Add Reconnect Logic to DeepgramService

**Files:**
- Modify: `src/VoiceDictation/Services/DeepgramService.cs`

**Step 1: Add new event and fields**

At line 20 (after existing events), add:

```csharp
public event Action<int, int>? Reconnecting; // (attempt, maxAttempts)
```

Add private fields after `_keywords` (line 18):

```csharp
private const int MaxReconnectAttempts = 10;
private const int MaxBackoffSeconds = 30;
private bool _reconnecting;
```

**Step 2: Extract URI builder into a helper method**

Extract lines 39-56 from `ConnectAsync()` into a private method so reconnect can reuse it:

```csharp
private Uri BuildUri()
{
    var uriBuilder =
        $"wss://api.deepgram.com/v1/listen" +
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

    return new Uri(uriBuilder);
}
```

Update `ConnectAsync()` to call `var uri = BuildUri();` instead of the inline code.

**Step 3: Add reconnect loop to ReceiveLoopAsync**

Replace the `finally` block in `ReceiveLoopAsync()` (currently just `Disconnected?.Invoke();`) with:

```csharp
finally
{
    // If cancellation was requested (user disconnect), don't reconnect
    if (_cts?.IsCancellationRequested ?? true)
    {
        Disconnected?.Invoke();
        return;
    }

    await TryReconnectAsync();
}
```

**Step 4: Add TryReconnectAsync method**

```csharp
private async Task TryReconnectAsync()
{
    if (_reconnecting) return;
    _reconnecting = true;

    try
    {
        for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
        {
            if (_cts?.IsCancellationRequested ?? true) break;

            Reconnecting?.Invoke(attempt, MaxReconnectAttempts);

            var delaySec = Math.Min(1 << (attempt - 1), MaxBackoffSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), _cts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                _ws.Options.SetRequestHeader("Authorization", $"Token {_apiKey}");
                await _ws.ConnectAsync(BuildUri(), _cts?.Token ?? CancellationToken.None);

                // Success — restart receive loop (runs in background, returns here)
                _ = Task.Run(ReceiveLoopAsync);
                return;
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Next attempt
            }
        }

        // All attempts failed
        Disconnected?.Invoke();
    }
    finally
    {
        _reconnecting = false;
    }
}
```

**Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 6: Commit**

```
feat: add auto-reconnect with exponential backoff to DeepgramService
```

---

### Task 2: Wire Reconnect Event in MainWindow

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

**Step 1: Subscribe to Reconnecting event in ConnectAsync**

After line 373 (`_provider.Disconnected += OnDisconnected;`), add:

```csharp
if (_provider is DeepgramService deepgram)
    deepgram.Reconnecting += OnReconnecting;
```

**Step 2: Add OnReconnecting handler**

After the `OnDisconnected` method (~line 607), add:

```csharp
private void OnReconnecting(int attempt, int maxAttempts) =>
    Dispatcher.Invoke(() =>
    {
        SetStatus($"Reconnecting ({attempt}/{maxAttempts})\u2026", Yellow);
        UpdateTrayMenu();
    });
```

**Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```
feat: show reconnect status in UI during DeepgramService retry
```

---

### Task 3: ReplacementService Tests

**Files:**
- Create: `tests/VoiceDictation.Tests/Services/ReplacementServiceTests.cs`

**Step 1: Write all tests**

```csharp
using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class ReplacementServiceTests : IDisposable
{
    private readonly string _tempFile;
    private ReplacementService? _service;

    public ReplacementServiceTests()
    {
        _tempFile = Path.GetTempFileName();
    }

    public void Dispose()
    {
        _service?.Dispose();
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private ReplacementService CreateService(params string[] lines)
    {
        File.WriteAllLines(_tempFile, lines);
        _service = new ReplacementService(_tempFile);
        return _service;
    }

    [Fact]
    public void Apply_replaces_case_insensitive()
    {
        var svc = CreateService("Punkt -> .");
        Assert.Equal("ein . hier", svc.Apply("ein Punkt hier"));
        Assert.Equal("ein . hier", svc.Apply("ein punkt hier"));
        Assert.Equal("ein . hier", svc.Apply("ein PUNKT hier"));
    }

    [Fact]
    public void Apply_handles_arrow_delimiter()
    {
        var svc = CreateService("mfg -> Mit freundlichen Grüßen");
        Assert.Equal("Mit freundlichen Grüßen", svc.Apply("mfg"));
    }

    [Fact]
    public void Apply_handles_unicode_arrow()
    {
        var svc = CreateService("mfg → Mit freundlichen Grüßen");
        Assert.Equal("Mit freundlichen Grüßen", svc.Apply("mfg"));
    }

    [Fact]
    public void Apply_converts_backslash_n_to_newline()
    {
        var svc = CreateService("neue Zeile -> \\n");
        Assert.Equal("text\nmore", svc.Apply("text neue Zeile more"));
    }

    [Fact]
    public void Apply_returns_original_when_no_rules()
    {
        var svc = CreateService();
        Assert.Equal("hello world", svc.Apply("hello world"));
    }

    [Fact]
    public void Apply_returns_original_when_empty_text()
    {
        var svc = CreateService("foo -> bar");
        Assert.Equal("", svc.Apply(""));
    }

    [Fact]
    public void Apply_applies_rules_in_order()
    {
        // First rule transforms "aa" -> "bb", second transforms "bb" -> "cc"
        var svc = CreateService("aa -> bb", "bb -> cc");
        Assert.Equal("cc", svc.Apply("aa"));
    }
}
```

**Step 2: Run tests**

Run: `dotnet test --filter ReplacementServiceTests`
Expected: All 7 tests pass

**Step 3: Commit**

```
test: add ReplacementService unit tests
```

---

### Task 4: VadService Tests

**Files:**
- Create: `tests/VoiceDictation.Tests/Services/VadServiceTests.cs`

**Step 1: Write all tests**

```csharp
using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class VadServiceTests
{
    /// <summary>
    /// Creates a PCM 16-bit mono buffer where every sample has the given amplitude.
    /// 16000 Hz sample rate, so 1 second = 32000 bytes.
    /// </summary>
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

        // Feed silence until past the timeout
        // With 0.1s chunks, we need >3 chunks to exceed 0.3s timeout
        // But VAD uses wall-clock time, so we must actually wait
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

        // Feed loud audio for longer than the timeout
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

        // Silence for 200ms (not enough to trigger 300ms timeout)
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

        // Don't call Reset yet — let internal timer be stale
        bool fired = false;
        vad.SilenceDetected += () => fired = true;

        // Reset now — should set _lastSpeechTime to now
        vad.Reset();

        // Brief silence (under timeout) should not fire
        vad.ProcessAudio(SilentChunk());
        Assert.False(fired);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test --filter VadServiceTests`
Expected: All 4 tests pass

**Step 3: Commit**

```
test: add VadService unit tests
```

---

### Task 5: UiHelper Tests

**Files:**
- Create: `tests/VoiceDictation.Tests/Helpers/UiHelperTests.cs`

**Step 1: Write all tests**

```csharp
using System.Windows.Input;
using VoiceDictation.Helpers;

namespace VoiceDictation.Tests.Helpers;

public class UiHelperTests
{
    [Fact]
    public void FormatShortcut_single_modifier()
    {
        var result = UiHelper.FormatShortcut(ModifierKeys.Control, Key.X);
        Assert.Equal("Ctrl+X", result);
    }

    [Fact]
    public void FormatShortcut_multiple_modifiers()
    {
        var result = UiHelper.FormatShortcut(ModifierKeys.Control | ModifierKeys.Alt, Key.X);
        Assert.Equal("Ctrl+Alt+X", result);
    }

    [Fact]
    public void FormatShortcut_left_right_keys()
    {
        var result = UiHelper.FormatShortcut(ModifierKeys.Windows, Key.LeftCtrl);
        Assert.Equal("Win+L-Ctrl", result);
    }

    [Fact]
    public void ParseShortcut_roundtrip()
    {
        var original = (ModifierKeys.Control | ModifierKeys.Alt, Key.X);
        var formatted = UiHelper.FormatShortcut(original.Item1, original.Item2);
        var (mods, key) = UiHelper.ParseShortcut(formatted, Key.None);

        Assert.Equal(original.Item1, mods);
        Assert.Equal(original.Item2, key);
    }

    [Fact]
    public void ParseShortcut_uses_default_on_invalid()
    {
        var (mods, key) = UiHelper.ParseShortcut("InvalidGarbage", Key.Space);
        Assert.Equal(ModifierKeys.None, mods);
        Assert.Equal(Key.Space, key);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test --filter UiHelperTests`
Expected: All 5 tests pass

**Step 3: Commit**

```
test: add UiHelper ParseShortcut/FormatShortcut tests
```

---

### Task 6: Extend SettingsService Tests

**Files:**
- Modify: `tests/VoiceDictation.Tests/Services/SettingsServiceTests.cs`

**Step 1: Add roundtrip and defaults tests**

Append to the existing class:

```csharp
[Fact]
public void Save_and_Load_roundtrip()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"VoiceDictation_Test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
        var jsonPath = Path.Combine(tempDir, "settings.json");
        var svc = new SettingsService(jsonPath);

        var settings = new AppSettings
        {
            Shortcuts = new ShortcutSettings { Toggle = "Ctrl+Shift+Z", Ptt = "Win+R-Alt" },
            Transcription = new TranscriptionSettings
            {
                Language = "en",
                Provider = "whisper",
                ApiKey = "test-key",
                Keywords = "hello,world",
                WhisperModel = "base"
            },
            Audio = new AudioSettings { Microphone = "USB Mic", Tone = "Classic", Vad = true },
            Llm = new LlmSettings
            {
                Enabled = true,
                ApiKey = "sk-test",
                BaseUrl = "https://api.example.com/v1",
                Model = "gpt-4",
                Prompt = "Fix grammar."
            },
            Window = new WindowSettings { Left = 100, Top = 200, Width = 800, Height = 600, StartMinimized = true },
        };

        svc.Save(settings);
        var loaded = svc.Load();

        Assert.Equal(settings.Shortcuts.Toggle, loaded.Shortcuts.Toggle);
        Assert.Equal(settings.Shortcuts.Ptt, loaded.Shortcuts.Ptt);
        Assert.Equal(settings.Transcription.Language, loaded.Transcription.Language);
        Assert.Equal(settings.Transcription.Provider, loaded.Transcription.Provider);
        Assert.Equal(settings.Transcription.ApiKey, loaded.Transcription.ApiKey);
        Assert.Equal(settings.Audio.Microphone, loaded.Audio.Microphone);
        Assert.Equal(settings.Audio.Tone, loaded.Audio.Tone);
        Assert.True(loaded.Audio.Vad);
        Assert.True(loaded.Llm.Enabled);
        Assert.Equal(settings.Llm.ApiKey, loaded.Llm.ApiKey);
        Assert.Equal(settings.Window.Left, loaded.Window.Left);
        Assert.Equal(settings.Window.Top, loaded.Window.Top);
        Assert.True(loaded.Window.StartMinimized);
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

[Fact]
public void Load_returns_defaults_when_no_file_exists()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"VoiceDictation_Test_{Guid.NewGuid():N}");
    var jsonPath = Path.Combine(tempDir, "settings.json");
    var svc = new SettingsService(jsonPath);

    var result = svc.Load();

    Assert.Equal("Ctrl+Alt+X", result.Shortcuts.Toggle);
    Assert.Equal("deepgram", result.Transcription.Provider);
    Assert.Equal("de", result.Transcription.Language);
    Assert.False(result.Llm.Enabled);
}
```

**Step 2: Make SettingsService accept a custom path**

Currently `SettingsService` hardcodes `JsonPath`. We need to add a constructor that accepts a path for testability.

In `src/VoiceDictation/Services/SettingsService.cs`, add a constructor:

```csharp
public class SettingsService
{
    private static readonly string DefaultSettingsDir = ...;  // existing
    private static readonly string DefaultJsonPath = ...;     // existing
    private static readonly string TxtPath = ...;             // existing

    private readonly string _jsonPath;

    public SettingsService() : this(DefaultJsonPath) { }

    public SettingsService(string jsonPath)
    {
        _jsonPath = jsonPath;
    }
    // ...
}
```

Update `Load()` and `Save()` to use `_jsonPath` instead of `JsonPath`.

**Step 3: Run all tests**

Run: `dotnet test`
Expected: All tests pass (existing + new)

**Step 4: Commit**

```
test: add SettingsService roundtrip and defaults tests
```

---

### Task 7: Final Verification

**Step 1: Run full build and all tests**

Run: `dotnet build && dotnet test`
Expected: Build succeeded, all tests pass

**Step 2: Verify test count**

Expected: ~22 tests total (4 existing + 7 Replacement + 4 VAD + 5 UiHelper + 2 Settings)
