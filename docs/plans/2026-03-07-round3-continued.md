# Round 3 Continued — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add audio device hot-swap detection, transcript search, extended unit tests, configurable log level, auto-update checking, and update the README for all Round 2+3 features.

**Architecture:** Each feature is a self-contained task. AudioCaptureService gets device-change notifications via NAudio re-enumeration on a timer (Win32 MMDevice callbacks require COM threading that conflicts with WPF). TranscriptHistoryWindow gets a search TextBox with debounced filtering. UpdateChecker is a static class calling GitHub Releases API. Logging gets a runtime-switchable level via Serilog's LoggingLevelSwitch.

**Tech Stack:** C# / .NET 8 / WPF, NAudio 2.2.1, Serilog 4.2.0, System.Net.Http, System.Text.Json, xUnit

---

### Task 1: Audio Device Hot-Swap — Service Layer

**Files:**
- Modify: `src/VoiceDictation/Services/AudioCaptureService.cs`

**Step 1: Add device-change detection to AudioCaptureService**

Add a `DevicesChanged` event and a `System.Timers.Timer` that polls `WaveInEvent.DeviceCount` every 2 seconds. When the count changes, re-enumerate and fire `DevicesChanged`. Also add a `DeviceError` event fired when recording throws due to a removed device.

```csharp
// Add at top of class:
private System.Timers.Timer? _devicePollTimer;
private int _lastDeviceCount;

public event Action<List<(int Number, string Name)>>? DevicesChanged;
public event Action<string>? DeviceError;

public void StartDevicePolling()
{
    _lastDeviceCount = WaveInEvent.DeviceCount;
    _devicePollTimer = new System.Timers.Timer(2000);
    _devicePollTimer.Elapsed += (_, _) =>
    {
        try
        {
            var count = WaveInEvent.DeviceCount;
            if (count != _lastDeviceCount)
            {
                _lastDeviceCount = count;
                DevicesChanged?.Invoke(GetDevices());
            }
        }
        catch { /* device enumeration can fail transiently */ }
    };
    _devicePollTimer.Start();
}

public void StopDevicePolling()
{
    _devicePollTimer?.Stop();
    _devicePollTimer?.Dispose();
    _devicePollTimer = null;
}
```

Wrap `_waveIn!.StartRecording()` in `Start()` to catch `MmException` and fire `DeviceError`:

```csharp
public void Start()
{
    if (_isRunning) return;
    if (!_initialized) Initialize();
    try
    {
        _waveIn!.StartRecording();
    }
    catch (NAudio.MmException ex)
    {
        DeviceError?.Invoke(ex.Message);
        return;
    }
    catch (InvalidOperationException)
    {
        return;
    }
    _isRunning = true;
}
```

Update `Dispose()` to also call `StopDevicePolling()`.

**Step 2: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/VoiceDictation/Services/AudioCaptureService.cs
git commit -m "feat: add device polling and error events to AudioCaptureService"
```

---

### Task 2: Audio Device Hot-Swap — MainWindow Wiring

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

**Step 1: Wire device change events in MainWindow constructor**

After `_audio` is created, start polling and subscribe to events. Find where `_audio` is used in `MainWindow()` constructor and add:

```csharp
_audio.DevicesChanged += devices => Dispatcher.Invoke(() =>
{
    Log.Information("Audio devices changed, {Count} devices found", devices.Count);
    PopulateMicrophones(); // existing method that refreshes MicCombo
    ToastWindow.ShowToast("Microphone list updated", Colors.Blue);
});

_audio.DeviceError += msg => Dispatcher.Invoke(() =>
{
    Log.Warning("Audio device error: {Error}", msg);
    ToastWindow.ShowToast("Microphone disconnected", Colors.Red);
});
```

Start polling at end of constructor:
```csharp
_audio.StartDevicePolling();
```

Update `Window_Closing` or shutdown logic to call `_audio.StopDevicePolling()`.

**Step 2: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/VoiceDictation/MainWindow.xaml.cs
git commit -m "feat: wire audio device hot-swap detection in MainWindow"
```

---

### Task 3: Transcript Search — UI

**Files:**
- Modify: `src/VoiceDictation/TranscriptHistoryWindow.xaml`
- Modify: `src/VoiceDictation/TranscriptHistoryWindow.xaml.cs`

**Step 1: Add search TextBox to XAML**

In `TranscriptHistoryWindow.xaml`, add a new row at the top of the file list's inner Grid (before the SESSIONS header). Insert a TextBox for search:

```xml
<!-- Add a new RowDefinition at position 0: Auto -->
<Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>  <!-- Search box -->
    <RowDefinition Height="Auto"/>  <!-- SESSIONS header -->
    <RowDefinition Height="*"/>     <!-- File list -->
    <RowDefinition Height="Auto"/>  <!-- Open Folder button -->
</Grid.RowDefinitions>

<!-- Search box (new, Grid.Row="0") -->
<TextBox x:Name="SearchBox" Grid.Row="0"
         Background="{DynamicResource SurfaceBrush}"
         Foreground="{DynamicResource TextBrush}"
         BorderThickness="0" FontSize="12"
         Padding="8,6" Margin="6,6,6,0"
         TextChanged="SearchBox_TextChanged">
    <TextBox.Style>
        <Style TargetType="TextBox">
            <Style.Triggers>
                <Trigger Property="Text" Value="">
                    <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
                </Trigger>
            </Style.Triggers>
        </Style>
    </TextBox.Style>
</TextBox>
```

Update Grid.Row on existing elements: SESSIONS header → Row="1", FileList → Row="2", Open Folder → Row="3".

**Step 2: Add search logic in code-behind**

```csharp
using System.Windows.Threading;

// Add field:
private DispatcherTimer? _searchDebounce;

private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
{
    _searchDebounce?.Stop();
    _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
    _searchDebounce.Tick += (_, _) =>
    {
        _searchDebounce.Stop();
        ApplySearch(SearchBox.Text.Trim());
    };
    _searchDebounce.Start();
}

private void ApplySearch(string query)
{
    FileList.Items.Clear();

    if (string.IsNullOrEmpty(query))
    {
        // Show all files (reload)
        LoadFiles();
        return;
    }

    if (!Directory.Exists(TranscriptDir)) return;

    var files = Directory.GetFiles(TranscriptDir, "*.txt")
        .OrderByDescending(f => f);

    foreach (var file in files)
    {
        try
        {
            var content = File.ReadAllText(file);
            if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var display = name.Replace("transcript_", "").Replace("_", "  ");
                if (display.Length >= 12)
                    display = display[..10] + "  " + display[12..14] + ":" + display[14..16] + ":" + display[16..];
                FileList.Items.Add(new ListBoxItem { Content = display, Tag = file });
            }
        }
        catch { /* skip unreadable files */ }
    }
}
```

In `FileList_SelectionChanged`, after setting `PreviewText.Text`, add highlighting:

```csharp
private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (FileList.SelectedItem is not ListBoxItem item || item.Tag is not string path) return;

    try
    {
        var content = File.ReadAllText(path);
        PreviewText.Text = content;
        PreviewHeader.Text = $"PREVIEW — {Path.GetFileNameWithoutExtension(path)}";

        // Highlight search term
        var query = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                PreviewText.Select(idx, query.Length);
                PreviewText.Focus();
            }
        }
    }
    catch (Exception ex)
    {
        PreviewText.Text = $"Error reading file: {ex.Message}";
    }
}
```

**Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/VoiceDictation/TranscriptHistoryWindow.xaml src/VoiceDictation/TranscriptHistoryWindow.xaml.cs
git commit -m "feat: add transcript search with debounce and highlighting"
```

---

### Task 4: Logging Cleanup — Configurable Log Level

**Files:**
- Modify: `src/VoiceDictation/Services/SettingsService.cs` (add LogLevel to WindowSettings)
- Modify: `src/VoiceDictation/MainWindow.xaml.cs` (use LoggingLevelSwitch)
- Modify: `src/VoiceDictation/SettingsWindow.xaml` (add LogLevel combo)
- Modify: `src/VoiceDictation/SettingsWindow.xaml.cs` (wire combo)
- Modify: `src/VoiceDictation/VoiceDictation.csproj` (add Serilog.Expressions or use built-in)

**Step 1: Add LogLevel to WindowSettings**

In `SettingsService.cs`, add to `WindowSettings`:

```csharp
public record WindowSettings
{
    // ... existing properties ...
    public string LogLevel { get; init; } = "Information";
}
```

**Step 2: Use LoggingLevelSwitch in MainWindow**

In `MainWindow.xaml.cs`, replace the Serilog setup:

```csharp
using Serilog.Core;
using Serilog.Events;

// Add field:
private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

// In constructor, replace Log.Logger creation:
LevelSwitch.MinimumLevel = Enum.TryParse<LogEventLevel>(_settings.Window.LogLevel, out var lvl)
    ? lvl : LogEventLevel.Information;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(LevelSwitch)
    .WriteTo.Sink(UiSink)
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();
```

Note: The `LoadSettings()` call happens before `Log.Logger` setup — move the Log.Logger creation to after `LoadSettings()` and `ThemeManager.Apply()`:

```csharp
var isFirstRun = _settingsService.IsFirstRun;
LoadSettings();
ThemeManager.Apply(_settings.Window.Theme);

// Now set up logging with configured level
LevelSwitch.MinimumLevel = Enum.TryParse<LogEventLevel>(_settings.Window.LogLevel, out var lvl)
    ? lvl : LogEventLevel.Information;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(LevelSwitch)
    .WriteTo.Sink(UiSink)
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();
```

Expose a static method for the settings window to change level at runtime:

```csharp
internal static void SetLogLevel(string level)
{
    if (Enum.TryParse<LogEventLevel>(level, out var lvl))
        LevelSwitch.MinimumLevel = lvl;
}
```

**Step 3: Add LogLevel ComboBox to SettingsWindow.xaml**

Add in the appropriate section (near Theme combo):

```xml
<TextBlock Text="Log Level" Foreground="{DynamicResource TextDimBrush}" FontSize="11" Margin="0,8,0,4"/>
<ComboBox x:Name="LogLevelCombo" Width="120" HorizontalAlignment="Left"
          ToolTip="Controls how much detail is written to the log file">
    <ComboBoxItem Content="Debug" Tag="Debug"/>
    <ComboBoxItem Content="Information" Tag="Information"/>
    <ComboBoxItem Content="Warning" Tag="Warning"/>
</ComboBox>
```

**Step 4: Wire in SettingsWindow.xaml.cs**

In `PopulateFromSettings`:
```csharp
UiHelper.SelectComboByTag(LogLevelCombo, settings.Window.LogLevel);
```

In save logic:
```csharp
var logLevel = (LogLevelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Information";
// Update WindowSettings with LogLevel = logLevel
MainWindow.SetLogLevel(logLevel);
```

**Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/VoiceDictation/Services/SettingsService.cs src/VoiceDictation/MainWindow.xaml.cs src/VoiceDictation/SettingsWindow.xaml src/VoiceDictation/SettingsWindow.xaml.cs
git commit -m "feat: add configurable log level with runtime switching"
```

---

### Task 5: Auto-Update Check

**Files:**
- Create: `src/VoiceDictation/Services/UpdateChecker.cs`
- Modify: `src/VoiceDictation/AboutWindow.xaml`
- Modify: `src/VoiceDictation/AboutWindow.xaml.cs`
- Modify: `src/VoiceDictation/MainWindow.xaml.cs` (startup check)

**Step 1: Create UpdateChecker**

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Serilog;

namespace VoiceDictation.Services;

public static class UpdateChecker
{
    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VoiceDictation" } },
        Timeout = TimeSpan.FromSeconds(10),
    };

    private const string ReleasesUrl = "https://api.github.com/repos/jadirc/VoiceDictation/releases/latest";

    public record ReleaseInfo(string TagName, string HtmlUrl);

    /// <summary>
    /// Checks GitHub for a newer release. Returns release info if newer, null if up-to-date.
    /// </summary>
    public static async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            var response = await Http.GetFromJsonAsync<GitHubRelease>(ReleasesUrl);
            if (response?.TagName == null) return null;

            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null) return null;

            // Parse tag like "v1.0.2" or "1.0.2"
            var tag = response.TagName.TrimStart('v');
            if (!Version.TryParse(tag, out var remote)) return null;

            if (remote > current)
                return new ReleaseInfo(response.TagName, response.HtmlUrl ?? ReleasesUrl);

            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Update check failed");
            return null;
        }
    }

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
```

**Step 2: Add "Check for Updates" button to AboutWindow.xaml**

Insert between the Close button and the Libraries list (in Row="4", push Close to Row="5"):

```xml
<!-- Add new row for update button -->
<StackPanel Grid.Row="4" HorizontalAlignment="Center" Margin="0,10,0,0" Orientation="Horizontal">
    <Button x:Name="UpdateButton" Content="Check for Updates"
            Click="UpdateButton_Click"
            Background="Transparent" Foreground="{DynamicResource AccentBrush}"
            FontSize="12" BorderThickness="0" Padding="8,4" Cursor="Hand"/>
    <TextBlock x:Name="UpdateStatus" Foreground="{DynamicResource TextDimBrush}"
               FontSize="12" VerticalAlignment="Center" Margin="8,0,0,0"/>
</StackPanel>
```

Add a 6th RowDefinition (Auto) and move Close to Row="5".

**Step 3: Wire in AboutWindow.xaml.cs**

```csharp
private async void UpdateButton_Click(object sender, RoutedEventArgs e)
{
    UpdateButton.IsEnabled = false;
    UpdateStatus.Text = "Checking...";

    var result = await Services.UpdateChecker.CheckAsync();

    if (result != null)
    {
        UpdateStatus.Text = $"New version available: {result.TagName}";
        var link = new Hyperlink(new Run("Download"))
        {
            NavigateUri = new Uri(result.HtmlUrl),
            Foreground = (SolidColorBrush)FindResource("AccentBrush"),
        };
        link.RequestNavigate += Hyperlink_RequestNavigate;
        UpdateStatus.Inlines.Clear();
        UpdateStatus.Inlines.Add($"{result.TagName} available — ");
        UpdateStatus.Inlines.Add(link);
    }
    else
    {
        UpdateStatus.Text = "You're up to date!";
    }

    UpdateButton.IsEnabled = true;
}
```

**Step 4: Add silent startup check in MainWindow**

At the end of the constructor (or in `Window_Loaded`), add:

```csharp
_ = CheckForUpdatesAsync();

// Add method:
private async Task CheckForUpdatesAsync()
{
    var result = await UpdateChecker.CheckAsync();
    if (result != null)
    {
        ToastWindow.ShowToast($"Update available: {result.TagName}", Colors.Blue, autoClose: true);
    }
}
```

**Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/VoiceDictation/Services/UpdateChecker.cs src/VoiceDictation/AboutWindow.xaml src/VoiceDictation/AboutWindow.xaml.cs src/VoiceDictation/MainWindow.xaml.cs
git commit -m "feat: add auto-update check (startup + About dialog)"
```

---

### Task 6: Unit Tests

**Files:**
- Create: `tests/VoiceDictation.Tests/Services/SoundFeedbackTests.cs`
- Modify: `tests/VoiceDictation.Tests/Services/SettingsServiceTests.cs`
- Create: `tests/VoiceDictation.Tests/Services/UpdateCheckerTests.cs`

**Step 1: Add IsFirstRun tests to SettingsServiceTests**

```csharp
[Fact]
public void IsFirstRun_true_when_no_files_exist()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"VoiceDictation_Test_{Guid.NewGuid():N}");
    var jsonPath = Path.Combine(tempDir, "settings.json");
    var svc = new SettingsService(jsonPath);

    Assert.True(svc.IsFirstRun);
}

[Fact]
public void IsFirstRun_false_when_json_exists()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"VoiceDictation_Test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
        var jsonPath = Path.Combine(tempDir, "settings.json");
        File.WriteAllText(jsonPath, "{}");
        var svc = new SettingsService(jsonPath);

        Assert.False(svc.IsFirstRun);
    }
    finally { Directory.Delete(tempDir, true); }
}
```

**Step 2: Create SoundFeedbackTests**

```csharp
using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class SoundFeedbackTests
{
    [Theory]
    [InlineData("Sanft", "Gentle")]
    [InlineData("Klick", "Click")]
    [InlineData("Glocke", "Bell")]
    [InlineData("Tief", "Deep")]
    [InlineData("Doppel", "Double")]
    [InlineData("Gentle", "Gentle")]    // already English
    [InlineData("Unknown", "Unknown")]  // unknown passes through
    public void MigrateName_maps_legacy_german_names(string input, string expected)
    {
        Assert.Equal(expected, SoundFeedback.MigrateName(input));
    }
}
```

**Step 3: Create UpdateCheckerTests (minimal — no network)**

We can test version parsing logic. Since `CheckAsync` hits the network, we just verify the `ReleaseInfo` record works:

```csharp
using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class UpdateCheckerTests
{
    [Fact]
    public void ReleaseInfo_stores_values()
    {
        var info = new UpdateChecker.ReleaseInfo("v1.2.3", "https://example.com");
        Assert.Equal("v1.2.3", info.TagName);
        Assert.Equal("https://example.com", info.HtmlUrl);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test`
Expected: All tests pass

**Step 5: Commit**

```bash
git add tests/VoiceDictation.Tests/Services/SettingsServiceTests.cs tests/VoiceDictation.Tests/Services/SoundFeedbackTests.cs tests/VoiceDictation.Tests/Services/UpdateCheckerTests.cs
git commit -m "test: add unit tests for IsFirstRun, SoundFeedback.MigrateName, UpdateChecker"
```

---

### Task 7: README Update

**Files:**
- Modify: `README.md`

**Step 1: Update README with Round 2+3 features**

Add to the Features section:

- **Dark / Light / Auto theme** — Catppuccin Mocha (dark) and Latte (light) palettes with smooth dissolve transitions. Auto mode follows the Windows system theme in real time.
- **Mute function** — Mute/unmute recording with a configurable shortcut. Muted state shown in status bar and tray menu.
- **Clipboard mode** — Optionally copy transcribed text to clipboard instead of typing it.
- **First-run wizard** — On first launch, a setup wizard guides you through provider selection and API key entry.
- **Audio device hot-swap** — Detects when microphones are connected or disconnected and updates the device list automatically.
- **Transcript search** — Search through saved transcript sessions by keyword with real-time filtering and highlighting.
- **Auto-update check** — Checks GitHub Releases on startup and in the About dialog for newer versions.
- **Configurable log level** — Set the log verbosity (Debug / Information / Warning) from Settings.

Update the Architecture tree to include new files:
- `TrayMenuWindow.xaml(.cs)` — Themed tray context menu
- `WelcomeWindow.xaml(.cs)` — First-run wizard
- `TranscriptHistoryWindow.xaml(.cs)` — Transcript history with search
- `Services/UpdateChecker.cs` — GitHub release update checking
- `Helpers/ThemeManager.cs` — Theme switching with dissolve animation
- `Themes/Dark.xaml`, `Themes/Light.xaml` — Catppuccin color palettes

Update the "Dark UI" bullet to: **Themes** — Dark (Catppuccin Mocha) and Light (Catppuccin Latte) with Auto mode that follows the Windows system setting

**Step 2: Build to verify nothing broken**

Run: `dotnet build`

**Step 3: Commit**

```bash
git add README.md
git commit -m "docs: update README with Round 2+3 features"
```
