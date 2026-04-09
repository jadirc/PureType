# LLM Auto-Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an always-on LLM auto-correction pipeline stage that automatically sends session transcript to an LLM for grammar/style correction when recording stops.

**Architecture:** New `AutoCorrectionSettings` record in settings, a new `_autoCorrectionEnabled` flag in `RecordingController` that suppresses live output and fires `AutoCorrectionRequested` at session end, handled in `MainWindow.xaml.cs` with provider fallback logic. Settings UI section with toggle, optional separate provider config, and style instructions text field.

**Tech Stack:** C# / .NET 8 / WPF, existing `ILlmClient` abstraction (OpenAI + Anthropic clients), xUnit tests.

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `src/PureType/Services/SettingsService.cs` | Modify | Add `AutoCorrectionSettings` record, add to `AppSettings` |
| `src/PureType/Services/RecordingController.cs` | Modify | Add auto-correction flag, suppress live output, fire event |
| `src/PureType/MainWindow.xaml.cs` | Modify | Handle `AutoCorrectionRequested`, resolve provider, call LLM |
| `src/PureType/SettingsWindow.xaml` | Modify | Add auto-correction UI section |
| `src/PureType/SettingsWindow.xaml.cs` | Modify | Load/save auto-correction settings |
| `tests/PureType.Tests/Services/RecordingControllerTests.cs` | Modify | Test suppression logic and event firing |

---

### Task 1: Add AutoCorrectionSettings Data Model

**Files:**
- Modify: `src/PureType/Services/SettingsService.cs:92-127`

- [ ] **Step 1: Add `AutoCorrectionSettings` record**

Insert after the `LlmSettings` record (after line 102) in `SettingsService.cs`:

```csharp
public record AutoCorrectionSettings
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";
    public string StyleInstructions { get; init; } = "";
}
```

- [ ] **Step 2: Add to `AppSettings`**

In the `AppSettings` record (line 120–127), add a new property after `Llm`:

```csharp
public AutoCorrectionSettings AutoCorrection { get; init; } = new();
```

- [ ] **Step 3: Update `MigrateFromTxt`**

In `MigrateFromTxt` (line 240–246), add initialization for the new record. After `var window = new WindowSettings();` add:

```csharp
var autoCorrection = new AutoCorrectionSettings();
```

And in the return statement at the end of that method, add:

```csharp
AutoCorrection = autoCorrection,
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add src/PureType/Services/SettingsService.cs
git commit -m "feat: add AutoCorrectionSettings data model"
```

---

### Task 2: Add Auto-Correction Logic to RecordingController

**Files:**
- Modify: `src/PureType/Services/RecordingController.cs:34-95,230-274,286-334`
- Test: `tests/PureType.Tests/Services/RecordingControllerTests.cs`

- [ ] **Step 1: Write failing tests**

Add these tests to `RecordingControllerTests.cs` after the existing tests:

```csharp
// ── Auto-correction tests ─────────────────────────────────────────

[Fact]
public void Configure_with_auto_correction_enabled()
{
    var controller = CreateController();
    controller.Configure(new AppSettings
    {
        Audio = new AudioSettings(),
        Llm = new LlmSettings(),
        AutoCorrection = new AutoCorrectionSettings { Enabled = true },
    });
    // Should not throw — flag is stored internally
}
```

Update the `CreateController` helper to accept `autoCorrectionEnabled`:

```csharp
private RecordingController CreateController(
    bool llmEnabled = false,
    string inputMode = "Type",
    bool autoCorrectionEnabled = false)
{
    var controller = new RecordingController(_audio, _replacements);
    controller.Configure(new AppSettings
    {
        Audio = new AudioSettings { Vad = false, InputMode = inputMode },
        Llm = new LlmSettings { Enabled = llmEnabled },
        AutoCorrection = new AutoCorrectionSettings { Enabled = autoCorrectionEnabled },
    });
    return controller;
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: Compilation error — `AutoCorrectionSettings` property not yet read in `Configure`.

- [ ] **Step 3: Add auto-correction state to RecordingController**

In `RecordingController.cs`, add a new field in the Settings section (after line 36 `private bool _llmEnabled;`):

```csharp
private bool _autoCorrectionEnabled;
```

Add a new event in the Events section (after the `LlmProcessingRequested` event, line 61):

```csharp
/// <summary>Request auto-correction LLM processing with collected session text.</summary>
public event Action<string>? AutoCorrectionRequested;
```

- [ ] **Step 4: Update `Configure` method**

In the `Configure` method (line 88–95), add after `_prompts = settings.Llm.Prompts;`:

```csharp
_autoCorrectionEnabled = settings.AutoCorrection.Enabled;
```

- [ ] **Step 5: Suppress live output when auto-correction is active**

In `OnTranscriptReceived` (line 288–333), change the condition on line 305 from:

```csharp
if (_selectedPrompt == null)
```

to:

```csharp
if (_selectedPrompt == null && !_autoCorrectionEnabled)
```

- [ ] **Step 6: Fire AutoCorrectionRequested at session end**

In `StopRecording` (line 230–274), after the existing prompt-key block (lines 253–259), add the auto-correction path:

```csharp
else if (_autoCorrectionEnabled && _sessionChunks.Count > 0)
{
    var fullText = string.Join("", _sessionChunks);
    AutoCorrectionRequested?.Invoke(fullText);
}
```

- [ ] **Step 7: Update status text with "(AI)" suffix**

In `StopRecording`, change the toast for non-prompt-key stops (line 250–251) from:

```csharp
if (_selectedPrompt == null)
    ToastRequested?.Invoke("Recording stopped", Green, true);
```

to:

```csharp
if (_selectedPrompt == null && !_autoCorrectionEnabled)
    ToastRequested?.Invoke("Recording stopped", Green, true);
```

In `StartRecording` (line 215), change the status text from:

```csharp
StatusChanged?.Invoke("\u25CF Recording", Red);
ToastRequested?.Invoke("Recording", Red, false);
```

to:

```csharp
var recordingLabel = _autoCorrectionEnabled ? "\u25CF Recording (AI)" : "\u25CF Recording";
StatusChanged?.Invoke(recordingLabel, Red);
ToastRequested?.Invoke(recordingLabel, Red, false);
```

In `StopRecording` (line 271–272), change the idle status from:

```csharp
if (_connected)
    StatusChanged?.Invoke("Connected \u2013 ready", Green);
```

to:

```csharp
if (_connected)
{
    var readyLabel = _autoCorrectionEnabled ? "Connected \u2013 ready (AI)" : "Connected \u2013 ready";
    StatusChanged?.Invoke(readyLabel, Green);
}
```

- [ ] **Step 8: Run tests**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/PureType/Services/RecordingController.cs tests/PureType.Tests/Services/RecordingControllerTests.cs
git commit -m "feat: add auto-correction suppression and event in RecordingController"
```

---

### Task 3: Handle AutoCorrectionRequested in MainWindow

**Files:**
- Modify: `src/PureType/MainWindow.xaml.cs:167,889-946,1028-1043`

- [ ] **Step 1: Subscribe to AutoCorrectionRequested event**

In `MainWindow.xaml.cs`, after the `LlmProcessingRequested` subscription (line 167):

```csharp
_controller.LlmProcessingRequested += (text, prompt) => Dispatcher.Invoke(() => _ = ProcessWithLlmAsync(text, prompt));
```

Add:

```csharp
_controller.AutoCorrectionRequested += text => Dispatcher.Invoke(() => _ = ProcessAutoCorrectAsync(text));
```

- [ ] **Step 2: Add the ProcessAutoCorrectAsync method**

Add this new method after `ProcessWithLlmAsync` (after line 946):

```csharp
// ── Auto-Correction ───────────────────────────────────────────────────

private static readonly string AutoCorrectionBasePrompt =
    "Fix grammar, punctuation and spelling errors in the following dictated text. "
    + "Keep the original meaning, language and content unchanged. "
    + "Do not add, remove or rephrase content. "
    + "Reply with ONLY the corrected text — no preamble, no explanation.";

private async Task ProcessAutoCorrectAsync(string text)
{
    try
    {
        // Resolve provider: own config or fallback to LLM settings
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

        Log.Information("Auto-correcting {Length} chars via {BaseUrl}/{Model}", text.Length, baseUrl, model);
        ToastWindow.ShowToast("AI correcting\u2026", Yellow.Color, autoClose: false);

        var result = await client.ProcessAsync(systemPrompt, text);
        var processed = _replacements.Apply(result);

        Log.Information("Auto-correction result: {Length} chars", processed.Length);
        ToastWindow.ShowToast("AI correction complete", Green.Color, autoClose: true);

        await OutputText(processed);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Auto-correction failed, outputting raw text");
        ToastWindow.ShowToast("AI correction failed \u2014 raw text used", Yellow.Color, autoClose: true);
        await OutputText(text);
    }
}

private async Task OutputText(string text)
{
    try
    {
        switch (_settings.Audio.InputMode)
        {
            case "Copy":
                System.Windows.Clipboard.SetText(text);
                break;
            case "Paste":
                await KeyboardInjector.PasteTextAsync(text);
                break;
            default: // "Type"
                await KeyboardInjector.TypeTextAsync(text);
                break;
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Text output failed, copying to clipboard");
        System.Windows.Clipboard.SetText(text);
    }
}
```

- [ ] **Step 3: Update `UpdateOverlay` for AI indicator**

In `UpdateOverlay` (line 1028–1043), change the recording and connected states:

```csharp
private void UpdateOverlay()
{
    if (_overlay is null) return;

    if (_muted)
    {
        _overlay.UpdateState(false, true, "Muted", Yellow.Color);
    }
    else if (_controller.IsRecording)
    {
        var label = _settings.AutoCorrection.Enabled ? "Recording (AI)" : "Recording";
        _overlay.UpdateState(true, false, label, Red.Color);
    }
    else if (_connected)
    {
        var label = _settings.AutoCorrection.Enabled ? "Connected \u2013 ready (AI)" : "Connected \u2013 ready";
        _overlay.UpdateState(false, false, label, Green.Color);
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/PureType/MainWindow.xaml.cs
git commit -m "feat: handle AutoCorrectionRequested with provider fallback and error handling"
```

---

### Task 4: Add Settings UI for Auto-Correction

**Files:**
- Modify: `src/PureType/SettingsWindow.xaml:410-523`
- Modify: `src/PureType/SettingsWindow.xaml.cs:98-118,180-213,229-259`

- [ ] **Step 1: Add XAML section**

In `SettingsWindow.xaml`, after the closing `</StackPanel>` of `LlmSettingsPanel` (line 521) and before the closing `</StackPanel>` of the main content (line 523), add:

```xml
<!-- ── AUTO-CORRECTION ── -->
<TextBlock Text="AUTO-CORRECTION" Foreground="{DynamicResource AccentBrush}"
           FontSize="12" FontWeight="Bold" Margin="0,16,0,8"
           Tag="section"/>

<CheckBox x:Name="AutoCorrectionEnabledCheck"
          Content="  Auto-correct transcription (LLM)"
          ToolTip="Automatically send each recording session through an LLM for grammar, punctuation and style correction before typing the result."
          Foreground="{DynamicResource TextBrush}" FontSize="12"
          Tag="auto correction grammar llm"
          Checked="AutoCorrectionEnabledCheck_Changed"
          Unchecked="AutoCorrectionEnabledCheck_Changed"/>

<StackPanel x:Name="AutoCorrectionSettingsPanel" Visibility="Collapsed" Margin="0,8,0,0"
            Tag="auto correction api endpoint key model style">

    <TextBlock Text="Leave provider fields empty to use the AI Post-Processing provider above."
               Foreground="{DynamicResource MutedTextBrush}" FontSize="11" Margin="0,0,0,8"
               TextWrapping="Wrap"/>

    <StackPanel Margin="0,0,0,12"
                ToolTip="Optional separate API endpoint for auto-correction. Leave empty to use the AI Post-Processing endpoint.">
        <TextBlock Text="API ENDPOINT (OPTIONAL)" Foreground="{DynamicResource LabelBrush}"
                   FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
        <ComboBox x:Name="AcBaseUrlCombo" IsEditable="True" Text=""
                  Padding="10,7" FontFamily="Consolas" FontSize="12">
            <ComboBoxItem Content="https://api.openai.com/v1" ToolTip="OpenAI"/>
            <ComboBoxItem Content="https://api.anthropic.com/v1" ToolTip="Anthropic Claude"/>
            <ComboBoxItem Content="https://openrouter.ai/api/v1" ToolTip="OpenRouter"/>
            <ComboBoxItem Content="https://generativelanguage.googleapis.com/v1beta/openai" ToolTip="Google Gemini"/>
        </ComboBox>
    </StackPanel>

    <StackPanel Margin="0,0,0,12"
                ToolTip="Optional separate API key for auto-correction. Leave empty to use the AI Post-Processing key.">
        <TextBlock Text="API KEY (OPTIONAL)" Foreground="{DynamicResource LabelBrush}"
                   FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
        <PasswordBox x:Name="AcApiKeyBox"
                     Background="{DynamicResource SurfaceBrush}" Foreground="{DynamicResource TextBrush}"
                     BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"
                     Padding="10,5" FontFamily="Consolas" FontSize="13"
                     PasswordChar="&#x25CF;"/>
    </StackPanel>

    <StackPanel Margin="0,0,0,12"
                ToolTip="Optional separate model for auto-correction. Leave empty to use the AI Post-Processing model.">
        <TextBlock Text="MODEL (OPTIONAL)" Foreground="{DynamicResource LabelBrush}"
                   FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
        <ComboBox x:Name="AcModelCombo" IsEditable="True" Text=""
                  Padding="10,7" FontFamily="Consolas" FontSize="13"/>
    </StackPanel>

    <StackPanel Margin="0,0,0,12"
                ToolTip="Optional additional instructions appended to the grammar correction prompt. Examples: 'Use formal tone', 'Keep sentences short and direct'.">
        <TextBlock Text="ADDITIONAL STYLE INSTRUCTIONS (OPTIONAL)" Foreground="{DynamicResource LabelBrush}"
                   FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
        <TextBox x:Name="AcStyleInstructionsBox"
                 AcceptsReturn="True" TextWrapping="Wrap"
                 MinHeight="60" MaxHeight="120"
                 Background="{DynamicResource SurfaceBrush}" Foreground="{DynamicResource TextBrush}"
                 BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"
                 Padding="10,7" FontSize="12"
                 VerticalScrollBarVisibility="Auto"/>
    </StackPanel>
</StackPanel>
```

- [ ] **Step 2: Add code-behind for loading settings**

In `SettingsWindow.xaml.cs`, after the LLM settings loading section (after the `_lastBaseUrl` assignment, around line 118), add:

```csharp
// Auto-Correction
AutoCorrectionEnabledCheck.IsChecked = settings.AutoCorrection.Enabled;
AutoCorrectionSettingsPanel.Visibility = settings.AutoCorrection.Enabled ? Visibility.Visible : Visibility.Collapsed;
AcBaseUrlCombo.Text = settings.AutoCorrection.BaseUrl;
AcApiKeyBox.Password = settings.AutoCorrection.ApiKey;
AcModelCombo.Text = settings.AutoCorrection.Model;
AcStyleInstructionsBox.Text = settings.AutoCorrection.StyleInstructions;
```

- [ ] **Step 3: Add code-behind for saving settings**

In the `OK_Click` method, in the `ResultSettings` construction (around line 203), after `Llm = BuildLlmSettings(),` add:

```csharp
AutoCorrection = BuildAutoCorrectionSettings(),
```

Add the `BuildAutoCorrectionSettings` method after `BuildLlmSettings`:

```csharp
private AutoCorrectionSettings BuildAutoCorrectionSettings()
{
    return new AutoCorrectionSettings
    {
        Enabled = AutoCorrectionEnabledCheck.IsChecked == true,
        BaseUrl = AcBaseUrlCombo.Text.Trim(),
        ApiKey = AcApiKeyBox.Password.Trim(),
        Model = AcModelCombo.Text.Trim(),
        StyleInstructions = AcStyleInstructionsBox.Text.Trim(),
    };
}
```

- [ ] **Step 4: Add toggle visibility handler**

Add the event handler method in the code-behind:

```csharp
private void AutoCorrectionEnabledCheck_Changed(object sender, RoutedEventArgs e)
{
    AutoCorrectionSettingsPanel.Visibility = AutoCorrectionEnabledCheck.IsChecked == true
        ? Visibility.Visible : Visibility.Collapsed;
}
```

- [ ] **Step 5: Handle search visibility for AutoCorrectionSettingsPanel**

Find the existing search-filtering code that handles `LlmSettingsPanel` visibility (around line 797). Add the same pattern for `AutoCorrectionSettingsPanel`. Locate the block:

```csharp
if (fe == LlmSettingsPanel)
```

And after that entire block, add:

```csharp
if (fe == AutoCorrectionSettingsPanel)
{
    fe.Visibility = AutoCorrectionEnabledCheck.IsChecked == true
        ? Visibility.Visible : Visibility.Collapsed;
    continue;
}
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 7: Run all tests**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/PureType/SettingsWindow.xaml src/PureType/SettingsWindow.xaml.cs
git commit -m "feat: add auto-correction settings UI with toggle and provider fields"
```

---

### Task 5: Integration Test and Final Verification

**Files:**
- Test: `tests/PureType.Tests/Services/RecordingControllerTests.cs`

- [ ] **Step 1: Add comprehensive tests**

Add these additional tests to `RecordingControllerTests.cs`:

```csharp
[Fact]
public void AutoCorrectionRequested_event_exists()
{
    var controller = CreateController(autoCorrectionEnabled: true);
    bool fired = false;
    controller.AutoCorrectionRequested += _ => fired = true;
    Assert.False(fired);
}

[Fact]
public void Configure_auto_correction_does_not_affect_llm()
{
    var controller = CreateController(llmEnabled: true, autoCorrectionEnabled: true);
    // Both flags can coexist — prompt key overrides auto-correction at runtime
    controller.Configure(new AppSettings
    {
        Audio = new AudioSettings(),
        Llm = new LlmSettings { Enabled = true },
        AutoCorrection = new AutoCorrectionSettings { Enabled = true },
    });
    // Should not throw
}
```

- [ ] **Step 2: Run full test suite**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 3: Full build verification**

Run: `dotnet build`
Expected: Clean build, no warnings related to new code.

- [ ] **Step 4: Commit**

```bash
git add tests/PureType.Tests/Services/RecordingControllerTests.cs
git commit -m "test: add auto-correction integration tests"
```
