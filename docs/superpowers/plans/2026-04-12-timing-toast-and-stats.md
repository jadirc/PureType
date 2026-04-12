# Timing Toast & Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show STT and AI processing times in toast notifications and track them in the stats system.

**Architecture:** WhisperService measures ProcessAsync via Stopwatch and fires a new TranscriptionTimed event. RecordingController captures the duration and exposes it as `LastSttDuration`. MainWindow wraps LLM calls with its own Stopwatch, updates toast messages with timing, and passes both durations to StatsService.

**Tech Stack:** WPF, System.Diagnostics.Stopwatch, existing StatsService/ToastWindow

---

### Task 1: Extend StatsService with timing fields (TDD)

**Files:**
- Modify: `src/PureType/Services/StatsService.cs`
- Modify: `tests/PureType.Tests/Services/StatsServiceTests.cs`

- [ ] **Step 1: Write failing tests for timing in RecordSession**

Add to `StatsServiceTests.cs`:

```csharp
[Fact]
public void RecordSession_tracks_stt_milliseconds()
{
    var svc = new StatsService(_tempFile);
    svc.RecordSession(10, 30, sttMs: 1200);

    var snap = svc.GetStats();
    Assert.Equal(1200, snap.TodaySttMs);
}

[Fact]
public void RecordAiTime_tracks_ai_milliseconds()
{
    var svc = new StatsService(_tempFile);
    svc.RecordSession(10, 30, sttMs: 1200);
    svc.RecordAiTime(700);

    var snap = svc.GetStats();
    Assert.Equal(1200, snap.TodaySttMs);
    Assert.Equal(700, snap.TodayAiMs);
    Assert.Equal(1, snap.TodaySessions); // RecordAiTime must NOT increment sessions
}

[Fact]
public void RecordSession_accumulates_timing()
{
    var svc = new StatsService(_tempFile);
    svc.RecordSession(10, 30, sttMs: 1000);
    svc.RecordAiTime(500);
    svc.RecordSession(20, 60, sttMs: 1500);
    svc.RecordAiTime(800);

    var snap = svc.GetStats();
    Assert.Equal(2500, snap.TodaySttMs);
    Assert.Equal(1300, snap.TodayAiMs);
}

[Fact]
public void RecordSession_without_timing_defaults_to_zero()
{
    var svc = new StatsService(_tempFile);
    svc.RecordSession(10, 30);

    var snap = svc.GetStats();
    Assert.Equal(0, snap.TodaySttMs);
    Assert.Equal(0, snap.TodayAiMs);
}

[Fact]
public void Timing_persists_across_instances()
{
    var svc1 = new StatsService(_tempFile);
    svc1.RecordSession(10, 30, sttMs: 1200);
    svc1.RecordAiTime(700);

    var svc2 = new StatsService(_tempFile);
    var snap = svc2.GetStats();
    Assert.Equal(1200, snap.TodaySttMs);
    Assert.Equal(700, snap.TodayAiMs);
}

[Fact]
public void DayHistory_includes_timing()
{
    var svc = new StatsService(_tempFile);
    svc.RecordSession(10, 30, sttMs: 1200);
    svc.RecordAiTime(700);

    var snap = svc.GetStats();
    var entry = snap.DayHistory[0];
    Assert.Equal(1200, entry.SttMs);
    Assert.Equal(700, entry.AiMs);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PureType.Tests --filter "StatsServiceTests" --nologo -v q`
Expected: FAIL — `TodaySttMs`, `TodayAiMs`, `RecordAiTime`, `DayHistoryEntry.SttMs`, `DayHistoryEntry.AiMs` do not exist.

- [ ] **Step 3: Extend DayStats, StatsSnapshot, DayHistoryEntry, and RecordSession**

In `StatsService.cs`, add fields to `DayStats`:

```csharp
public class DayStats
{
    public int Words { get; set; }
    public int Sessions { get; set; }
    public int Seconds { get; set; }
    public int SttMilliseconds { get; set; }
    public int AiMilliseconds { get; set; }
}
```

Update `DayHistoryEntry`:

```csharp
public record DayHistoryEntry(string Date, int Words, int Sessions, int Seconds, int SttMs, int AiMs);
```

Update `StatsSnapshot` — add today and total timing:

```csharp
public record StatsSnapshot(
    int TotalWords,
    int TotalSessions,
    int TotalSeconds,
    int TodayWords,
    int TodaySessions,
    int TodaySeconds,
    int TodaySttMs,
    int TodayAiMs,
    int TotalSttMs,
    int TotalAiMs,
    IReadOnlyList<DayHistoryEntry> DayHistory);
```

Update `RecordSession`:

```csharp
public void RecordSession(int wordCount, int durationSeconds, int sttMs = 0)
{
    var key = DateTime.Today.ToString("yyyy-MM-dd");

    if (!_data.Days.TryGetValue(key, out var day))
    {
        day = new DayStats();
        _data.Days[key] = day;
    }

    day.Words += wordCount;
    day.Sessions += 1;
    day.Seconds += durationSeconds;
    day.SttMilliseconds += sttMs;

    Prune();
    Save();
}
```

Add `RecordAiTime` (adds AI timing to the current day without incrementing sessions):

```csharp
public void RecordAiTime(int aiMs)
{
    var key = DateTime.Today.ToString("yyyy-MM-dd");

    if (!_data.Days.TryGetValue(key, out var day))
    {
        day = new DayStats();
        _data.Days[key] = day;
    }

    day.AiMilliseconds += aiMs;
    Save();
}
```

Update `GetStats`:

```csharp
public StatsSnapshot GetStats()
{
    var todayKey = DateTime.Today.ToString("yyyy-MM-dd");
    _data.Days.TryGetValue(todayKey, out var today);

    var totalWords = _data.Days.Values.Sum(d => d.Words);
    var totalSessions = _data.Days.Values.Sum(d => d.Sessions);
    var totalSeconds = _data.Days.Values.Sum(d => d.Seconds);
    var totalSttMs = _data.Days.Values.Sum(d => d.SttMilliseconds);
    var totalAiMs = _data.Days.Values.Sum(d => d.AiMilliseconds);

    var history = _data.Days
        .OrderByDescending(kv => kv.Key)
        .Select(kv => new DayHistoryEntry(
            kv.Key, kv.Value.Words, kv.Value.Sessions, kv.Value.Seconds,
            kv.Value.SttMilliseconds, kv.Value.AiMilliseconds))
        .ToList();

    return new StatsSnapshot(
        totalWords, totalSessions, totalSeconds,
        today?.Words ?? 0, today?.Sessions ?? 0, today?.Seconds ?? 0,
        today?.SttMilliseconds ?? 0, today?.AiMilliseconds ?? 0,
        totalSttMs, totalAiMs,
        history);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PureType.Tests --filter "StatsServiceTests" --nologo -v q`
Expected: ALL PASS

- [ ] **Step 5: Commit**

```bash
git add src/PureType/Services/StatsService.cs tests/PureType.Tests/Services/StatsServiceTests.cs
git commit -m "feat: add STT and AI timing fields to StatsService"
```

---

### Task 2: Add TranscriptionTimed event to WhisperService

**Files:**
- Modify: `src/PureType/Services/ITranscriptionProvider.cs`
- Modify: `src/PureType/Services/WhisperService.cs`

- [ ] **Step 1: Add TranscriptionTimed event to ITranscriptionProvider**

In `ITranscriptionProvider.cs`, add a default no-op event so DeepgramService doesn't need changes:

```csharp
public interface ITranscriptionProvider : IAsyncDisposable
{
    event Action<string, bool>? TranscriptReceived;
    event Action<string>? ErrorOccurred;
    event Action? Disconnected;
    event Action<TimeSpan>? TranscriptionTimed { add { } remove { } }

    bool IsConnected { get; }

    Task ConnectAsync();
    Task SendAudioAsync(byte[] audioData);
    Task SendFinalizeAsync();

    Task SetLanguageAsync(string language) => Task.CompletedTask;
}
```

- [ ] **Step 2: Add Stopwatch and fire TranscriptionTimed in WhisperService**

In `WhisperService.cs`, add the event field after the existing events:

```csharp
public event Action<TimeSpan>? TranscriptionTimed;
```

In `SendFinalizeAsync`, wrap the ProcessAsync block with a Stopwatch. Add `using System.Diagnostics;` if not already present (it is — used for `Process.GetCurrentProcess()`).

Replace the block starting at the `var result = new StringBuilder()` line through the `TranscriptReceived?.Invoke` call:

```csharp
var result = new System.Text.StringBuilder();
int segCount = 0;
var sw = Stopwatch.StartNew();
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var segment in _processor.ProcessAsync(samples, cts.Token))
{
    segCount++;
    Log.Debug("Whisper segment {N}: Start={Start}, End={End}, Text=\"{Text}\", Prob={Prob:F3}",
        segCount, segment.Start, segment.End, segment.Text, segment.Probability);
    result.Append(segment.Text);
}
sw.Stop();

var text = result.ToString().Trim();
Log.Debug("Whisper result ({Segments} segments, {Elapsed}ms): \"{Text}\"", segCount, sw.ElapsedMilliseconds, text);
TranscriptionTimed?.Invoke(sw.Elapsed);
if (!string.IsNullOrWhiteSpace(text))
    TranscriptReceived?.Invoke(text, true);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build -c Release --nologo -v q`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/PureType/Services/ITranscriptionProvider.cs src/PureType/Services/WhisperService.cs
git commit -m "feat: add TranscriptionTimed event to WhisperService with Stopwatch"
```

---

### Task 3: RecordingController — capture STT timing and update toasts

**Files:**
- Modify: `src/PureType/Services/RecordingController.cs`
- Modify: `tests/PureType.Tests/Services/RecordingControllerTests.cs`

- [ ] **Step 1: Write failing test for LastSttDuration**

Add to `RecordingControllerTests.cs`:

```csharp
[Fact]
public void LastSttDuration_initially_zero()
{
    var controller = CreateController();
    Assert.Equal(TimeSpan.Zero, controller.LastSttDuration);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PureType.Tests --filter "LastSttDuration_initially_zero" --nologo -v q`
Expected: FAIL — `LastSttDuration` does not exist.

- [ ] **Step 3: Add LastSttDuration property and subscribe to TranscriptionTimed**

In `RecordingController.cs`, add the property after the existing public API section:

```csharp
public TimeSpan LastSttDuration { get; private set; }
```

In `SetProvider`, subscribe to `TranscriptionTimed`:

```csharp
public void SetProvider(ITranscriptionProvider? provider, bool connected)
{
    if (_provider is not null)
    {
        _provider.TranscriptReceived -= OnTranscriptReceived;
        _provider.TranscriptionTimed -= OnTranscriptionTimed;
    }

    _provider = provider;
    _connected = connected;

    if (_provider is not null)
    {
        _provider.TranscriptReceived += OnTranscriptReceived;
        _provider.TranscriptionTimed += OnTranscriptionTimed;
    }
}
```

Add the handler:

```csharp
private void OnTranscriptionTimed(TimeSpan duration)
{
    LastSttDuration = duration;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PureType.Tests --filter "LastSttDuration_initially_zero" --nologo -v q`
Expected: PASS

- [ ] **Step 5: Update StopRecording toasts with STT timing**

In `StopRecording()`, change the non-AI toast (currently `"Recording stopped"`) to include timing:

Replace:

```csharp
if (_selectedPrompt == null && !_autoCorrectionEnabled)
    ToastRequested?.Invoke("Recording stopped", Green, true);
```

With:

```csharp
if (_selectedPrompt == null && !_autoCorrectionEnabled)
{
    var sttLabel = LastSttDuration > TimeSpan.Zero
        ? $"Whisper: {LastSttDuration.TotalSeconds:F1}s"
        : "Recording stopped";
    ToastRequested?.Invoke(sttLabel, Green, true);
}
```

- [ ] **Step 6: Pass sttMs to RecordSession**

In `StopRecording()`, update the `_stats.RecordSession` call:

Replace:

```csharp
_stats.RecordSession(wordCount, duration);
```

With:

```csharp
var sttMs = (int)LastSttDuration.TotalMilliseconds;
_stats.RecordSession(wordCount, duration, sttMs: sttMs);
```

- [ ] **Step 7: Reset LastSttDuration on StartRecording**

In `StartRecording()`, add after `_recordingStartTime = DateTime.UtcNow;`:

```csharp
LastSttDuration = TimeSpan.Zero;
```

- [ ] **Step 8: Build and run all tests**

Run: `dotnet build -c Release --nologo -v q && dotnet test tests/PureType.Tests --nologo -v q`
Expected: 0 errors, all tests pass

- [ ] **Step 9: Commit**

```bash
git add src/PureType/Services/RecordingController.cs tests/PureType.Tests/Services/RecordingControllerTests.cs
git commit -m "feat: capture STT timing in RecordingController and show in toast"
```

---

### Task 4: MainWindow — measure AI timing, update toasts, record stats

**Files:**
- Modify: `src/PureType/MainWindow.xaml.cs`

- [ ] **Step 1: Update ProcessAutoCorrectAsync with Stopwatch and timing toasts**

In `ProcessAutoCorrectAsync`, add a Stopwatch around the LLM call and update toasts. The `using System.Diagnostics;` import is not needed — `Stopwatch` is already available via global usings or existing imports.

Replace the body of `ProcessAutoCorrectAsync` with:

```csharp
private async Task ProcessAutoCorrectAsync(string text)
{
    try
    {
        var ac = _settings.AutoCorrection;
        var baseUrl = !string.IsNullOrEmpty(ac.BaseUrl) ? ac.BaseUrl : _settings.Llm.BaseUrl;
        var apiKey = !string.IsNullOrEmpty(ac.ApiKey) ? ac.ApiKey : _settings.Llm.ApiKey;
        var model = !string.IsNullOrEmpty(ac.Model) ? ac.Model : _settings.Llm.Model;

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
        {
            Log.Warning("Auto-correction skipped: no provider configured");
            await OutputText(text);
            return;
        }

        bool isAnthropic = baseUrl.Contains("anthropic.com", StringComparison.OrdinalIgnoreCase);
        ILlmClient client = isAnthropic
            ? new AnthropicLlmClient(apiKey, model)
            : new OpenAiLlmClient(apiKey, baseUrl, model);

        var systemPrompt = string.IsNullOrWhiteSpace(ac.StyleInstructions)
            ? AutoCorrectionBasePrompt
            : AutoCorrectionBasePrompt + "\n\n" + ac.StyleInstructions.Trim();

        var sttSec = _controller.LastSttDuration.TotalSeconds;
        var sttLabel = sttSec > 0 ? $"Whisper: {sttSec:F1}s — " : "";

        Log.Information("Auto-correcting {Length} chars via {BaseUrl}/{Model}", text.Length, baseUrl, model);
        ToastWindow.ShowToast($"{sttLabel}AI correcting\u2026", Yellow.Color, autoClose: false);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await client.ProcessAsync(systemPrompt, text);
        sw.Stop();
        var processed = _replacements.Apply(result);

        var aiSec = sw.Elapsed.TotalSeconds;
        var totalSec = sttSec + aiSec;
        Log.Information("Auto-correction result: {Length} chars in {AiMs}ms", processed.Length, sw.ElapsedMilliseconds);
        ToastWindow.ShowToast($"Done \u2014 STT {sttSec:F1}s + AI {aiSec:F1}s = {totalSec:F1}s", Green.Color, autoClose: true);

        _stats.RecordAiTime((int)sw.ElapsedMilliseconds);

        await OutputText(processed);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Auto-correction failed, outputting raw text");
        ToastWindow.ShowToast("AI correction failed \u2014 raw text used", Yellow.Color, autoClose: true);
        await OutputText(text);
    }
}
```

Note: `_stats.RecordSession(0, 0, aiMs: ...)` adds AI timing to the current day without incrementing words/sessions/seconds (those were already recorded by RecordingController).

- [ ] **Step 2: Update ProcessWithLlmAsync with Stopwatch and timing toasts**

In `ProcessWithLlmAsync`, update the toast messages and add timing. Replace the section from the `Log.Information("Sending...")` line through the `ToastWindow.ShowToast("AI processing complete"...)` line:

```csharp
var sttSec = _controller.LastSttDuration.TotalSeconds;
var sttLabel = sttSec > 0 ? $"Whisper: {sttSec:F1}s — " : "";

Log.Information("Sending {Length} chars to LLM ({BaseUrl}/{Model}) with prompt '{PromptName}'",
    text.Length, baseUrl, model, namedPrompt.Name);
ToastWindow.ShowToast($"{sttLabel}AI: {namedPrompt.Name} \u2026",
    Yellow.Color, autoClose: false);

var sw = System.Diagnostics.Stopwatch.StartNew();
var result = await client.ProcessAsync(namedPrompt.Prompt, text);
sw.Stop();
var processed = _replacements.Apply(result);

var aiSec = sw.Elapsed.TotalSeconds;
var totalSec = sttSec + aiSec;
Log.Information("LLM result: {Length} chars in {AiMs}ms", processed.Length, sw.ElapsedMilliseconds);
ToastWindow.ShowToast($"Done \u2014 STT {sttSec:F1}s + AI {aiSec:F1}s = {totalSec:F1}s", Green.Color, autoClose: true);

_stats.RecordAiTime((int)sw.ElapsedMilliseconds);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build -c Release --nologo -v q`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/PureType/MainWindow.xaml.cs
git commit -m "feat: show STT+AI timing in toasts and record AI time in stats"
```

---

### Task 5: StatsWindow — display timing averages

**Files:**
- Modify: `src/PureType/StatsWindow.xaml`
- Modify: `src/PureType/StatsWindow.xaml.cs`

- [ ] **Step 1: Add timing display elements to XAML**

In `StatsWindow.xaml`, add a `TodayTiming` TextBlock after `TodayDuration` in the Today card:

```xml
<TextBlock x:Name="TodayTiming" Foreground="{DynamicResource TextDimBrush}"
           FontSize="12" Margin="0,4,0,0"/>
```

Add a `TotalTiming` TextBlock after `TotalDuration` in the All Time card:

```xml
<TextBlock x:Name="TotalTiming" Foreground="{DynamicResource TextDimBrush}"
           FontSize="12" Margin="0,4,0,0"/>
```

Add two new columns to the DataGrid after the Duration column:

```xml
<DataGridTextColumn Header="Ø STT" Binding="{Binding AvgStt}" Width="60"/>
<DataGridTextColumn Header="Ø AI" Binding="{Binding AvgAi}" Width="60"/>
```

- [ ] **Step 2: Update PopulateStats to show timing averages**

In `StatsWindow.xaml.cs`, update `PopulateStats`:

```csharp
private void PopulateStats(StatsSnapshot s)
{
    TodayWords.Text = s.TodayWords.ToString("N0");
    TodaySessions.Text = $"{s.TodaySessions} sessions";
    TodayDuration.Text = FormatDuration(s.TodaySeconds);
    TodayTiming.Text = FormatAvgTiming(s.TodaySttMs, s.TodayAiMs, s.TodaySessions);

    TotalWords.Text = s.TotalWords.ToString("N0");
    TotalSessions.Text = $"{s.TotalSessions} sessions";
    TotalDuration.Text = FormatDuration(s.TotalSeconds);
    TotalTiming.Text = FormatAvgTiming(s.TotalSttMs, s.TotalAiMs, s.TotalSessions);

    HistoryGrid.ItemsSource = s.DayHistory.Select(d => new
    {
        d.Date,
        d.Words,
        d.Sessions,
        DurationDisplay = FormatDuration(d.Seconds),
        AvgStt = FormatAvgMs(d.SttMs, d.Sessions),
        AvgAi = FormatAvgMs(d.AiMs, d.Sessions),
    }).ToList();
}

private static string FormatAvgTiming(int sttMs, int aiMs, int sessions)
{
    if (sessions == 0) return "";
    var sttAvg = FormatAvgMs(sttMs, sessions);
    var aiAvg = FormatAvgMs(aiMs, sessions);
    if (sttAvg == "\u2014" && aiAvg == "\u2014") return "";
    return $"\u00D8 STT: {sttAvg} \u00B7 \u00D8 AI: {aiAvg}";
}

private static string FormatAvgMs(int totalMs, int sessions)
{
    if (sessions == 0 || totalMs == 0) return "\u2014";
    var avgSec = totalMs / 1000.0 / sessions;
    return $"{avgSec:F1}s";
}
```

- [ ] **Step 3: Build and run all tests**

Run: `dotnet build -c Release --nologo -v q && dotnet test tests/PureType.Tests --nologo -v q`
Expected: 0 errors, all tests pass

- [ ] **Step 4: Commit**

```bash
git add src/PureType/StatsWindow.xaml src/PureType/StatsWindow.xaml.cs
git commit -m "feat: show average STT and AI timing in StatsWindow"
```

---

### Task 6: Final integration test

- [ ] **Step 1: Run full test suite**

Run: `dotnet test tests/PureType.Tests --nologo -v q`
Expected: all tests pass (existing + new)

- [ ] **Step 2: Push all commits**

```bash
git push origin master
```
