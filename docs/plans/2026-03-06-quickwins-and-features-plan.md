# Quick Wins & Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add tray menu enhancements (connect/disconnect, mute), a toast overlay for recording state, a custom text replacement system with UI editor, and LLM post-processing integration (OpenAI + Anthropic).

**Architecture:** Phase 1 extends the existing `MainWindow` tray setup and adds a lightweight `ToastWindow`. Phase 2 introduces a `ReplacementService` that sits between transcript reception and keyboard injection, plus a `ReplacementsWindow` editor. Phase 3 adds `ILlmClient` with two implementations and an optional post-processing step after recording stops. All UI follows the existing Catppuccin Mocha dark theme.

**Tech Stack:** .NET 8 WPF, NAudio 2.2.1, Serilog, System.Net.Http (for LLM APIs), System.Text.Json

**No test project exists.** Tasks include manual verification steps instead of TDD.

---

## Phase 1a: Enhanced Tray Context Menu

### Task 1: Add mute state and connect/disconnect to tray menu

**Files:**
- Modify: `MainWindow.xaml.cs` — `SetupTrayIcon()` method (~line 969), add `_muted` field, update `OnAudioData()`

**Step 1: Add mute field**

In `MainWindow.xaml.cs`, add a field near the other state fields (after line 38):

```csharp
private bool _muted;
```

**Step 2: Extend SetupTrayIcon()**

Replace the current `SetupTrayIcon()` method with:

```csharp
private System.Windows.Forms.ToolStripLabel? _trayStatusLabel;
private System.Windows.Forms.ToolStripMenuItem? _trayConnectItem;
private System.Windows.Forms.ToolStripMenuItem? _trayMuteItem;

private void SetupTrayIcon()
{
    var iconStream = Application.GetResourceStream(
        new Uri("pack://application:,,,/Resources/mic.ico"))?.Stream;

    _trayIcon = new System.Windows.Forms.NotifyIcon
    {
        Icon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Application,
        Text = "Voice Dictation",
        Visible = true
    };

    _trayIcon.Click += (_, e) =>
    {
        if (e is System.Windows.Forms.MouseEventArgs me && me.Button == System.Windows.Forms.MouseButtons.Left)
            ShowFromTray();
    };

    var menu = new System.Windows.Forms.ContextMenuStrip();

    _trayStatusLabel = new System.Windows.Forms.ToolStripLabel("Not connected");
    _trayStatusLabel.ForeColor = System.Drawing.Color.Gray;
    _trayStatusLabel.Enabled = false;
    menu.Items.Add(_trayStatusLabel);

    menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

    _trayConnectItem = new System.Windows.Forms.ToolStripMenuItem("Connect");
    _trayConnectItem.Click += async (_, _) =>
    {
        if (!_connected) await Dispatcher.InvokeAsync(async () => await ConnectAsync());
        else await Dispatcher.InvokeAsync(async () => await DisconnectAsync());
    };
    menu.Items.Add(_trayConnectItem);

    _trayMuteItem = new System.Windows.Forms.ToolStripMenuItem("Mute");
    _trayMuteItem.Click += (_, _) => Dispatcher.Invoke(ToggleMute);
    menu.Items.Add(_trayMuteItem);

    menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
    menu.Items.Add("Open", null, (_, _) => ShowFromTray());
    menu.Items.Add("Exit", null, (_, _) =>
    {
        _trayIcon.Visible = false;
        _keyboardHook.Dispose();
        Application.Current.Shutdown();
    });

    _trayIcon.ContextMenuStrip = menu;
}
```

**Step 3: Add ToggleMute() and UpdateTrayMenu()**

Add these methods after `SetupTrayIcon()`:

```csharp
private void ToggleMute()
{
    _muted = !_muted;
    _trayMuteItem!.Checked = _muted;
    if (_connected)
    {
        if (_muted)
            SetStatus("Muted", Yellow);
        else if (_recording)
            SetStatus("Recording", Red);
        else
            SetStatus("Connected - ready", Green);
    }
    Log.Information("Mute toggled: {Muted}", _muted);
}

private void UpdateTrayMenu()
{
    if (_trayStatusLabel == null) return;

    if (_muted)
    {
        _trayStatusLabel.Text = "Muted";
        _trayStatusLabel.ForeColor = System.Drawing.Color.FromArgb(0xF9, 0xE2, 0xAF);
    }
    else if (_recording)
    {
        _trayStatusLabel.Text = "Recording";
        _trayStatusLabel.ForeColor = System.Drawing.Color.FromArgb(0xF3, 0x8B, 0xA8);
    }
    else if (_connected)
    {
        _trayStatusLabel.Text = "Connected";
        _trayStatusLabel.ForeColor = System.Drawing.Color.FromArgb(0xA6, 0xE3, 0xA1);
    }
    else
    {
        _trayStatusLabel.Text = "Not connected";
        _trayStatusLabel.ForeColor = System.Drawing.Color.Gray;
    }

    _trayConnectItem!.Text = _connected ? "Disconnect" : "Connect";
}
```

**Step 4: Integrate mute into audio pipeline**

In `OnAudioData()` (~line 904), add mute check:

```csharp
private async void OnAudioData(byte[] chunk)
{
    if (_provider is null || !_recording) return;
    if (!_muted)
        await _provider.SendAudioAsync(chunk);
    _vad?.ProcessAudio(chunk);
}
```

**Step 5: Call UpdateTrayMenu() from state changes**

Add `UpdateTrayMenu()` call at the end of:
- `ConnectAsync()` — after `ConnectButton.Content = "Disconnect";`
- `DisconnectAsync()` — after `ConnectButton.Background = Blue;`
- `StartRecording()` — at the end
- `StopRecording()` — after setting status to "Connected - ready"
- `ToggleMute()` — at the end

**Step 6: Verify**

Run: `dotnet build && dotnet run`
- Right-click tray icon: should show status, Connect/Disconnect, Mute, Open, Exit
- Connect, then right-click: status should say "Connected", item should say "Disconnect"
- Click Mute: status shows "Muted", main window status turns yellow
- Start recording while muted: VU meter moves but no transcription happens
- Unmute: transcription works again

**Step 7: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: add connect/disconnect, mute, and status to tray menu"
```

---

## Phase 1b: Toast Overlay

### Task 2: Create ToastWindow

**Files:**
- Create: `ToastWindow.xaml`
- Create: `ToastWindow.xaml.cs`

**Step 1: Create ToastWindow.xaml**

```xml
<Window x:Class="VoiceDictation.ToastWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True"
        Background="Transparent" Topmost="True"
        ShowInTaskbar="False" ShowActivated="False"
        Focusable="False" IsHitTestVisible="False"
        SizeToContent="WidthAndHeight"
        WindowStartupLocation="Manual">
    <Border Background="#E61E1E2E" CornerRadius="8" Padding="14,8">
        <StackPanel Orientation="Horizontal">
            <Ellipse x:Name="Dot" Width="10" Height="10"
                     VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBlock x:Name="MessageText"
                       Foreground="#CDD6F4" FontSize="13"
                       FontFamily="Segoe UI" VerticalAlignment="Center"/>
        </StackPanel>
    </Border>
</Window>
```

**Step 2: Create ToastWindow.xaml.cs**

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VoiceDictation;

public partial class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _closeTimer;

    private static readonly Color Red = Color.FromRgb(0xF3, 0x8B, 0xA8);
    private static readonly Color Green = Color.FromRgb(0xA6, 0xE3, 0xA1);

    private ToastWindow(string message, bool isRecording)
    {
        InitializeComponent();

        MessageText.Text = message;
        Dot.Fill = new SolidColorBrush(isRecording ? Red : Green);

        var workArea = SystemParameters.WorkArea;
        Loaded += (_, _) =>
        {
            Left = workArea.Right - ActualWidth - 16;
            Top = workArea.Bottom - ActualHeight - 16;
        };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) =>
            {
                if (_current == this) _current = null;
                Close();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        };
    }

    public static void ShowToast(string message, bool isRecording)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _current?.Close();
            _current = new ToastWindow(message, isRecording);
            _current.Show();
            _current._closeTimer.Start();
        });
    }
}
```

**Step 3: Verify it builds**

Run: `dotnet build`
Expected: success, no errors.

**Step 4: Commit**

```bash
git add ToastWindow.xaml ToastWindow.xaml.cs
git commit -m "feat: add ToastWindow for recording state notifications"
```

---

### Task 3: Integrate toast into recording flow

**Files:**
- Modify: `MainWindow.xaml.cs` — `StartRecording()` and `StopRecording()`

**Step 1: Add toast calls**

In `StartRecording()`, after `SetStatus("Recording", Red);`:

```csharp
ToastWindow.ShowToast("Recording started", true);
```

In `StopRecording()`, after `SoundFeedback.PlayStop();`:

```csharp
ToastWindow.ShowToast("Recording stopped", false);
```

**Step 2: Verify**

Run: `dotnet run`
- Start recording via shortcut: toast appears bottom-right "Recording started" with red dot
- Stop recording: toast appears "Recording stopped" with green dot
- Toast fades out after ~1.5 seconds
- Toast does not steal focus from the active window

**Step 3: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: show toast overlay on recording start/stop"
```

---

## Phase 2: Custom Replacements

### Task 4: Create ReplacementService

**Files:**
- Create: `Services/ReplacementService.cs`

**Step 1: Create ReplacementService.cs**

```csharp
using System.IO;
using System.Text.RegularExpressions;
using Serilog;

namespace VoiceDictation.Services;

public class ReplacementService : IDisposable
{
    private readonly string _filePath;
    private FileSystemWatcher? _watcher;
    private List<(string trigger, string replacement)> _rules = new();

    public IReadOnlyList<(string trigger, string replacement)> Rules => _rules;

    public ReplacementService(string filePath)
    {
        _filePath = filePath;
        Reload();
        WatchFile();
    }

    public void Reload()
    {
        if (!File.Exists(_filePath))
        {
            _rules = new List<(string, string)>();
            return;
        }

        var rules = new List<(string, string)>();
        foreach (var line in File.ReadAllLines(_filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            string? trigger = null, replacement = null;

            // Try " -> " first, then " → "
            var idx = line.IndexOf(" -> ", StringComparison.Ordinal);
            if (idx >= 0)
            {
                trigger = line[..idx].Trim();
                replacement = line[(idx + 4)..].Trim();
            }
            else
            {
                idx = line.IndexOf(" → ", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    trigger = line[..idx].Trim();
                    replacement = line[(idx + 3)..].Trim();
                }
            }

            if (trigger != null && replacement != null && trigger.Length > 0)
            {
                // Unescape special sequences
                replacement = replacement
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t");
                rules.Add((trigger, replacement));
            }
        }

        _rules = rules;
        Log.Information("Loaded {Count} replacement rules from {Path}", rules.Count, _filePath);
    }

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || _rules.Count == 0)
            return text;

        foreach (var (trigger, replacement) in _rules)
        {
            text = Regex.Replace(text, Regex.Escape(trigger), replacement, RegexOptions.IgnoreCase);
        }

        return text;
    }

    public void Save(IEnumerable<(string trigger, string replacement)> rules)
    {
        var lines = new List<string>();
        foreach (var (trigger, replacement) in rules)
        {
            // Re-escape for file storage
            var stored = replacement
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
            lines.Add($"{trigger} -> {stored}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllLines(_filePath, lines);
        Reload();
    }

    private void WatchFile()
    {
        var dir = Path.GetDirectoryName(_filePath);
        var name = Path.GetFileName(_filePath);
        if (dir == null) return;

        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _watcher.Changed += (_, _) =>
        {
            // Debounce: file may still be written
            Thread.Sleep(100);
            try { Reload(); } catch (Exception ex) { Log.Warning(ex, "Failed to reload replacements"); }
        };
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
```

**Step 2: Verify**

Run: `dotnet build`
Expected: success.

**Step 3: Commit**

```bash
git add Services/ReplacementService.cs
git commit -m "feat: add ReplacementService for custom text replacements"
```

---

### Task 5: Integrate ReplacementService into transcript pipeline

**Files:**
- Modify: `MainWindow.xaml.cs` — add field, initialize, use in `OnTranscriptReceived()`

**Step 1: Add field and initialize**

Add field near the other services (after line 17):

```csharp
private readonly ReplacementService _replacements = new(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 "VoiceDictation", "replacements.txt"));
```

**Step 2: Apply replacements before injection**

In `OnTranscriptReceived()`, change the `isFinal` branch (~line 921):

```csharp
if (isFinal)
{
    _interimText = "";
    InterimText.Text = "";
    var processed = _replacements.Apply(text);
    AppendTranscript(processed);
    try
    {
        await KeyboardInjector.TypeTextAsync(processed);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Text injection error");
    }
}
```

**Step 3: Dispose in Window_Closing**

In `Window_Closing()`, add before `Application.Current.Shutdown();`:

```csharp
_replacements.Dispose();
```

**Step 4: Verify**

Create `%LOCALAPPDATA%\VoiceDictation\replacements.txt` with:
```
mfg -> Mit freundlichen Grüßen
```
Run app, dictate "mfg" — should type "Mit freundlichen Grüßen".

**Step 5: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: integrate replacement service into transcript pipeline"
```

---

### Task 6: Create ReplacementsWindow UI editor

**Files:**
- Create: `ReplacementsWindow.xaml`
- Create: `ReplacementsWindow.xaml.cs`

**Step 1: Create ReplacementsWindow.xaml**

```xml
<Window x:Class="VoiceDictation.ReplacementsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Text Replacements"
        Width="450" Height="400"
        MinWidth="350" MinHeight="300"
        ResizeMode="CanResizeWithGrip"
        WindowStartupLocation="CenterOwner"
        Background="#1E1E2E"
        FontFamily="Segoe UI">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <TextBlock Grid.Row="0" Text="REPLACEMENT RULES" Foreground="#BAC2DE"
                   FontSize="11" FontWeight="SemiBold" Margin="0,0,0,8"/>

        <!-- Rules list -->
        <Border Grid.Row="1" Background="#181825" CornerRadius="6" Padding="4">
            <DataGrid x:Name="RulesGrid" AutoGenerateColumns="False"
                      Background="Transparent" Foreground="#CDD6F4"
                      BorderThickness="0" RowBackground="#181825"
                      AlternatingRowBackground="#1a1a2e"
                      GridLinesVisibility="None" HeadersVisibility="Column"
                      CanUserAddRows="False" CanUserDeleteRows="False"
                      CanUserReorderColumns="False" SelectionMode="Single"
                      FontSize="13">
                <DataGrid.ColumnHeaderStyle>
                    <Style TargetType="DataGridColumnHeader">
                        <Setter Property="Background" Value="#313244"/>
                        <Setter Property="Foreground" Value="#BAC2DE"/>
                        <Setter Property="Padding" Value="8,5"/>
                        <Setter Property="BorderThickness" Value="0"/>
                        <Setter Property="FontWeight" Value="SemiBold"/>
                        <Setter Property="FontSize" Value="11"/>
                    </Style>
                </DataGrid.ColumnHeaderStyle>
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Trigger" Binding="{Binding Trigger}"
                                        Width="*" Foreground="#CDD6F4">
                        <DataGridTextColumn.EditingElementStyle>
                            <Style TargetType="TextBox">
                                <Setter Property="Background" Value="#313244"/>
                                <Setter Property="Foreground" Value="#CDD6F4"/>
                            </Style>
                        </DataGridTextColumn.EditingElementStyle>
                    </DataGridTextColumn>
                    <DataGridTextColumn Header="Replacement" Binding="{Binding Replacement}"
                                        Width="*" Foreground="#CDD6F4">
                        <DataGridTextColumn.EditingElementStyle>
                            <Style TargetType="TextBox">
                                <Setter Property="Background" Value="#313244"/>
                                <Setter Property="Foreground" Value="#CDD6F4"/>
                            </Style>
                        </DataGridTextColumn.EditingElementStyle>
                    </DataGridTextColumn>
                </DataGrid.Columns>
            </DataGrid>
        </Border>

        <!-- Buttons -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0" HorizontalAlignment="Right">
            <Button Content="Open File" Click="OpenFile_Click"
                    Background="Transparent" Foreground="#89B4FA"
                    BorderThickness="0" Padding="10,6" Cursor="Hand" FontSize="12"/>
            <Button Content="Add" Click="Add_Click"
                    Background="#89B4FA" Foreground="#1E1E2E"
                    BorderThickness="0" Padding="14,6" Cursor="Hand" Margin="6,0,0,0"
                    FontWeight="SemiBold" FontSize="12"/>
            <Button Content="Remove" Click="Remove_Click"
                    Background="#F38BA8" Foreground="#1E1E2E"
                    BorderThickness="0" Padding="14,6" Cursor="Hand" Margin="6,0,0,0"
                    FontWeight="SemiBold" FontSize="12"/>
        </StackPanel>
    </Grid>
</Window>
```

**Step 2: Create ReplacementsWindow.xaml.cs**

```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using VoiceDictation.Services;

namespace VoiceDictation;

public partial class ReplacementsWindow : Window
{
    private readonly ReplacementService _service;
    private readonly ObservableCollection<ReplacementRule> _rules = new();

    public ReplacementsWindow(ReplacementService service)
    {
        InitializeComponent();
        _service = service;
        RulesGrid.ItemsSource = _rules;
        LoadRules();
    }

    private void LoadRules()
    {
        _rules.Clear();
        foreach (var (trigger, replacement) in _service.Rules)
        {
            // Show escape sequences in editor
            var display = replacement.Replace("\n", "\\n").Replace("\t", "\\t");
            _rules.Add(new ReplacementRule { Trigger = trigger, Replacement = display });
        }
    }

    private void SaveRules()
    {
        var rules = _rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Trigger))
            .Select(r => (r.Trigger, r.Replacement.Replace("\\n", "\n").Replace("\\t", "\t")))
            .ToList();
        _service.Save(rules);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        _rules.Add(new ReplacementRule { Trigger = "", Replacement = "" });
        RulesGrid.SelectedIndex = _rules.Count - 1;
        RulesGrid.Focus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is ReplacementRule rule)
        {
            _rules.Remove(rule);
            SaveRules();
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var path = _service.FilePath;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# Replacement rules: trigger -> replacement\n");
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveRules();
        base.OnClosing(e);
    }
}

public class ReplacementRule
{
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
}
```

**Step 3: Expose FilePath in ReplacementService**

In `Services/ReplacementService.cs`, add a public property:

```csharp
public string FilePath => _filePath;
```

**Step 4: Verify**

Run: `dotnet build`
Expected: success.

**Step 5: Commit**

```bash
git add ReplacementsWindow.xaml ReplacementsWindow.xaml.cs Services/ReplacementService.cs
git commit -m "feat: add ReplacementsWindow UI editor"
```

---

### Task 7: Add replacements button to main window

**Files:**
- Modify: `MainWindow.xaml` — add button row
- Modify: `MainWindow.xaml.cs` — add click handler

**Step 1: Add UI button**

In `MainWindow.xaml`, after the Keywords StackPanel (Grid.Row="14") and before the Language section (Grid.Row="16"), add a button in a new row. First add a new row to the grid definition after row 14's spacer (after `<RowDefinition Height="12"/>` at ~line 118):

Actually, to keep it simple, add a small button inside the existing Keywords StackPanel — or better, add it as a standalone button in a currently unused row. The simplest approach: add a "Replacements" button in the row after VAD (row 32). Add two new RowDefinitions:

After the last `<RowDefinition Height="Auto"/>  <!-- Row 32: VAD -->`:
```xml
<RowDefinition Height="4"/>
<RowDefinition Height="Auto"/>  <!-- Row 34: Replacements -->
```

Then after the VAD checkbox:
```xml
<!-- Replacements -->
<Button Grid.Row="34" Content="Text Replacements …"
        Click="ReplacementsButton_Click"
        Background="Transparent" Foreground="#89B4FA"
        FontSize="12" BorderThickness="0"
        Padding="0,4" Cursor="Hand"
        HorizontalAlignment="Left"/>
```

**Step 2: Add click handler**

In `MainWindow.xaml.cs`:

```csharp
private ReplacementsWindow? _replacementsWindow;

private void ReplacementsButton_Click(object sender, RoutedEventArgs e)
{
    if (_replacementsWindow is { IsLoaded: true })
    {
        _replacementsWindow.Activate();
        return;
    }
    _replacementsWindow = new ReplacementsWindow(_replacements);
    _replacementsWindow.Owner = this;
    _replacementsWindow.Show();
}
```

**Step 3: Verify**

Run: `dotnet run`
- Click "Text Replacements" button — editor window opens
- Add a rule: trigger "test123", replacement "Hello World"
- Close editor, dictate "test123" — should type "Hello World"
- Click "Open File" — opens replacements.txt in default editor

**Step 4: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add replacements button to main window"
```

---

## Phase 3: LLM Post-Processing

### Task 8: Create ILlmClient interface and implementations

**Files:**
- Create: `Services/ILlmClient.cs`
- Create: `Services/OpenAiLlmClient.cs`
- Create: `Services/AnthropicLlmClient.cs`

**Step 1: Create ILlmClient.cs**

```csharp
namespace VoiceDictation.Services;

public interface ILlmClient
{
    Task<string> ProcessAsync(string systemPrompt, string text, CancellationToken ct = default);
}
```

**Step 2: Create OpenAiLlmClient.cs**

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;

namespace VoiceDictation.Services;

public class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _http = new();
    private readonly string _baseUrl;
    private readonly string _model;

    public OpenAiLlmClient(string apiKey, string baseUrl, string model)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> ProcessAsync(string systemPrompt, string text, CancellationToken ct = default)
    {
        var body = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.3
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var result = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        Log.Debug("OpenAI LLM response: {Result}", result);
        return result ?? text;
    }
}
```

**Step 3: Create AnthropicLlmClient.cs**

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Serilog;

namespace VoiceDictation.Services;

public class AnthropicLlmClient : ILlmClient
{
    private readonly HttpClient _http = new();
    private readonly string _model;

    public AnthropicLlmClient(string apiKey, string model)
    {
        _model = model;
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<string> ProcessAsync(string systemPrompt, string text, CancellationToken ct = default)
    {
        var body = new
        {
            model = _model,
            max_tokens = 4096,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = text }
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.anthropic.com/v1/messages", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var result = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        Log.Debug("Anthropic LLM response: {Result}", result);
        return result ?? text;
    }
}
```

**Step 4: Verify**

Run: `dotnet build`
Expected: success.

**Step 5: Commit**

```bash
git add Services/ILlmClient.cs Services/OpenAiLlmClient.cs Services/AnthropicLlmClient.cs
git commit -m "feat: add ILlmClient with OpenAI and Anthropic implementations"
```

---

### Task 9: Add LLM settings UI to MainWindow

**Files:**
- Modify: `MainWindow.xaml` — add collapsible LLM settings section
- Modify: `MainWindow.xaml.cs` — add fields, load/save settings, toggle visibility

**Step 1: Add grid rows in MainWindow.xaml**

After the Replacements row definition, add:
```xml
<RowDefinition Height="4"/>
<RowDefinition Height="Auto"/>  <!-- Row 36: LLM Post-Processing -->
```

**Step 2: Add LLM UI section**

After the Replacements button:
```xml
<!-- LLM Post-Processing -->
<StackPanel Grid.Row="36" x:Name="LlmPanel">
    <CheckBox x:Name="LlmEnabledCheck"
              Content="  AI Post-Processing"
              Foreground="#CDD6F4" FontSize="12"
              Checked="LlmEnabledCheck_Changed"
              Unchecked="LlmEnabledCheck_Changed"/>
    <StackPanel x:Name="LlmSettingsPanel" Visibility="Collapsed" Margin="0,8,0,0">
        <TextBlock Text="LLM PROVIDER" Foreground="#BAC2DE"
                   FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
        <ComboBox x:Name="LlmProviderCombo" Padding="10,7" FontSize="13"
                  SelectionChanged="LlmProviderCombo_SelectionChanged">
            <ComboBoxItem Content="OpenAI-compatible" Tag="openai" IsSelected="True"/>
            <ComboBoxItem Content="Anthropic Claude" Tag="anthropic"/>
        </ComboBox>

        <TextBlock Text="API KEY" Foreground="#BAC2DE"
                   FontSize="11" FontWeight="SemiBold" Margin="0,8,0,5"/>
        <PasswordBox x:Name="LlmApiKeyBox"
                     Background="#313244" Foreground="#CDD6F4"
                     BorderBrush="#45475A" BorderThickness="1"
                     Padding="10,8" FontFamily="Consolas" FontSize="13"
                     PasswordChar="●"
                     PasswordChanged="LlmApiKeyBox_PasswordChanged"/>

        <StackPanel x:Name="LlmBaseUrlPanel">
            <TextBlock Text="BASE URL" Foreground="#BAC2DE"
                       FontSize="11" FontWeight="SemiBold" Margin="0,8,0,5"/>
            <TextBox x:Name="LlmBaseUrlBox"
                     Text="https://api.openai.com/v1"
                     Background="#313244" Foreground="#CDD6F4"
                     BorderBrush="#45475A" BorderThickness="1"
                     Padding="10,8" FontFamily="Consolas" FontSize="13"
                     LostFocus="LlmSettingChanged"/>
        </StackPanel>

        <TextBlock Text="MODEL" Foreground="#BAC2DE"
                   FontSize="11" FontWeight="SemiBold" Margin="0,8,0,5"/>
        <TextBox x:Name="LlmModelBox"
                 Text="gpt-4o-mini"
                 Background="#313244" Foreground="#CDD6F4"
                 BorderBrush="#45475A" BorderThickness="1"
                 Padding="10,8" FontFamily="Consolas" FontSize="13"
                 LostFocus="LlmSettingChanged"/>

        <TextBlock Text="SYSTEM PROMPT" Foreground="#BAC2DE"
                   FontSize="11" FontWeight="SemiBold" Margin="0,8,0,5"/>
        <TextBox x:Name="LlmPromptBox"
                 Text="Clean up the following dictated text. Fix punctuation, grammar, and formatting. Keep the meaning unchanged. Reply with only the corrected text."
                 Background="#313244" Foreground="#CDD6F4"
                 BorderBrush="#45475A" BorderThickness="1"
                 Padding="10,8" FontSize="12"
                 TextWrapping="Wrap" AcceptsReturn="True"
                 Height="80" VerticalScrollBarVisibility="Auto"
                 LostFocus="LlmSettingChanged"/>
    </StackPanel>
</StackPanel>
```

**Step 3: Add event handlers in MainWindow.xaml.cs**

```csharp
private void LlmEnabledCheck_Changed(object sender, RoutedEventArgs e)
{
    if (_isLoading) return;
    LlmSettingsPanel.Visibility = LlmEnabledCheck.IsChecked == true
        ? Visibility.Visible : Visibility.Collapsed;
    SaveSettings();
}

private void LlmProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
{
    if (_isLoading || LlmProviderCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
    var isOpenAi = (string)item.Tag == "openai";
    LlmBaseUrlPanel.Visibility = isOpenAi ? Visibility.Visible : Visibility.Collapsed;
    SaveSettings();
}

private void LlmApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
{
    if (!_isLoading) SaveSettings();
}

private void LlmSettingChanged(object sender, RoutedEventArgs e)
{
    if (!_isLoading) SaveSettings();
}
```

**Step 4: Add LLM settings to LoadSettings/SaveSettings**

In `SaveSettings()`, add these lines to the `lines` list:
```csharp
$"llm_enabled={LlmEnabledCheck.IsChecked == true}",
$"llm_provider={(string)((LlmProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "openai")}",
$"llm_apikey={LlmApiKeyBox.Password.Trim()}",
$"llm_baseurl={LlmBaseUrlBox.Text.Trim()}",
$"llm_model={LlmModelBox.Text.Trim()}",
$"llm_prompt={LlmPromptBox.Text.Trim().Replace("\n", "\\n")}",
```

In `LoadSettings()`, add cases in the switch:
```csharp
case "llm_enabled":
    LlmEnabledCheck.IsChecked = value.Equals("True", StringComparison.OrdinalIgnoreCase);
    LlmSettingsPanel.Visibility = LlmEnabledCheck.IsChecked == true
        ? Visibility.Visible : Visibility.Collapsed;
    break;
case "llm_provider":
    SelectComboByTag(LlmProviderCombo, value);
    break;
case "llm_apikey":
    LlmApiKeyBox.Password = value;
    break;
case "llm_baseurl":
    LlmBaseUrlBox.Text = value;
    break;
case "llm_model":
    LlmModelBox.Text = value;
    break;
case "llm_prompt":
    LlmPromptBox.Text = value.Replace("\\n", "\n");
    break;
```

**Step 5: Verify**

Run: `dotnet run`
- Check "AI Post-Processing" — settings section expands
- Switch provider to Anthropic — Base URL field hides
- Switch back to OpenAI — Base URL reappears
- Uncheck — section collapses
- Restart app — settings are persisted

**Step 6: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add LLM post-processing settings UI"
```

---

### Task 10: Integrate LLM post-processing into recording flow

**Files:**
- Modify: `MainWindow.xaml.cs` — collect chunks, call LLM on stop

**Step 1: Add session transcript accumulator**

Add field near the other state fields:
```csharp
private readonly List<string> _sessionChunks = new();
```

**Step 2: Collect chunks in OnTranscriptReceived**

In the `isFinal` branch of `OnTranscriptReceived()`, after the existing `AppendTranscript` / `TypeTextAsync` block, add:
```csharp
_sessionChunks.Add(processed);
```

**Step 3: Clear chunks on StartRecording**

In `StartRecording()`, add at the beginning (after the guard):
```csharp
_sessionChunks.Clear();
```

**Step 4: Process with LLM on StopRecording**

In `StopRecording()`, after `SoundFeedback.PlayStop();` and the toast call, add:

```csharp
if (LlmEnabledCheck.IsChecked == true && _sessionChunks.Count > 0)
{
    var fullText = string.Join("", _sessionChunks);
    _ = ProcessWithLlmAsync(fullText);
}
```

Add the `ProcessWithLlmAsync` method:

```csharp
private async Task ProcessWithLlmAsync(string text)
{
    try
    {
        var providerItem = LlmProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var providerTag = (string)(providerItem?.Tag ?? "openai");
        var apiKey = LlmApiKeyBox.Password.Trim();
        var model = LlmModelBox.Text.Trim();
        var prompt = LlmPromptBox.Text.Trim();

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
        {
            Log.Warning("LLM post-processing skipped: missing API key or model");
            return;
        }

        ILlmClient client = providerTag == "anthropic"
            ? new AnthropicLlmClient(apiKey, model)
            : new OpenAiLlmClient(apiKey, LlmBaseUrlBox.Text.Trim(), model);

        Log.Information("Sending {Length} chars to LLM ({Provider}/{Model})", text.Length, providerTag, model);
        ToastWindow.ShowToast("Processing with AI …", true);

        var result = await client.ProcessAsync(prompt, text);
        var processed = _replacements.Apply(result);

        Log.Information("LLM result: {Length} chars", processed.Length);
        ToastWindow.ShowToast("AI processing complete", false);

        // Copy to clipboard for user to paste
        Clipboard.SetText(processed);
        Log.Information("LLM result copied to clipboard");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "LLM post-processing failed");
        ToastWindow.ShowToast("AI processing failed", true);
    }
}
```

**Step 5: Verify**

Run: `dotnet run`
- Enable AI post-processing, set an API key and model
- Dictate something, stop recording
- Toast shows "Processing with AI …" then "AI processing complete"
- Ctrl+V should paste the cleaned-up text
- Check log for LLM request/response details

**Step 6: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: integrate LLM post-processing on recording stop"
```

---

## Summary

| Task | Description | Commit message |
|------|-------------|----------------|
| 1 | Enhanced tray menu with connect/disconnect, status, mute | `feat: add connect/disconnect, mute, and status to tray menu` |
| 2 | Create ToastWindow | `feat: add ToastWindow for recording state notifications` |
| 3 | Integrate toast into recording flow | `feat: show toast overlay on recording start/stop` |
| 4 | Create ReplacementService | `feat: add ReplacementService for custom text replacements` |
| 5 | Integrate replacements into pipeline | `feat: integrate replacement service into transcript pipeline` |
| 6 | Create ReplacementsWindow editor | `feat: add ReplacementsWindow UI editor` |
| 7 | Add replacements button to main window | `feat: add replacements button to main window` |
| 8 | Create ILlmClient + implementations | `feat: add ILlmClient with OpenAI and Anthropic implementations` |
| 9 | Add LLM settings UI | `feat: add LLM post-processing settings UI` |
| 10 | Integrate LLM into recording flow | `feat: integrate LLM post-processing on recording stop` |
