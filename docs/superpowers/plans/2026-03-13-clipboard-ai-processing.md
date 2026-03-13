# Clipboard AI Processing — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow users to apply LLM post-processing to clipboard text via a configurable global shortcut and prompt picker popup.

**Architecture:** A 5th shortcut channel in KeyboardHookService fires `ClipboardAiPressed`. MainWindow reads clipboard, opens a PromptPickerWindow for prompt selection, sends text to the LLM via the existing `ProcessWithLlmAsync` (extended with a `clipboardOnly` parameter), and outputs the result either at cursor or back to clipboard.

**Tech Stack:** WPF (.NET 8), Win32 low-level keyboard hook, existing LLM clients (Anthropic/OpenAI)

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `src/PureType/Services/SettingsService.cs` | Modify | Add `ClipboardAi` to `ShortcutSettings` |
| `src/PureType/Helpers/KeyboardHookService.cs` | Modify | Add 5th shortcut channel |
| `src/PureType/PromptPickerWindow.xaml` | Create | XAML layout for prompt picker |
| `src/PureType/PromptPickerWindow.xaml.cs` | Create | Filtering, keyboard nav, shift detection |
| `src/PureType/MainWindow.xaml.cs` | Modify | Hook lifecycle, event handler, ProcessWithLlmAsync extension |
| `src/PureType/SettingsWindow.xaml` | Modify | Add Clipboard AI shortcut row |
| `src/PureType/SettingsWindow.xaml.cs` | Modify | Wire shortcut recording, collision checks, save |
| `tests/PureType.Tests/Services/SettingsServiceTests.cs` | Modify | Add ClipboardAi roundtrip + default tests |

---

## Chunk 1: Settings & Keyboard Hook

### Task 1: Add `ClipboardAi` to ShortcutSettings

**Files:**
- Modify: `src/PureType/Services/SettingsService.cs:11-17`
- Test: `tests/PureType.Tests/Services/SettingsServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Add two tests to `SettingsServiceTests.cs`:

```csharp
[Fact]
public void ClipboardAi_defaults_to_empty()
{
    var settings = new AppSettings();
    Assert.Equal("", settings.Shortcuts.ClipboardAi);
}

[Fact]
public void ClipboardAi_roundtrips_through_json()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "settings.json");
    try
    {
        var svc = new SettingsService(path);
        var original = new AppSettings
        {
            Shortcuts = new ShortcutSettings { ClipboardAi = "Ctrl+Alt+C" }
        };
        svc.Save(original);
        var loaded = svc.Load();
        Assert.Equal("Ctrl+Alt+C", loaded.Shortcuts.ClipboardAi);
    }
    finally { Directory.Delete(dir, true); }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "ClipboardAi" -v n`
Expected: FAIL — `ShortcutSettings` has no `ClipboardAi` property

- [ ] **Step 3: Add ClipboardAi property to ShortcutSettings**

In `src/PureType/Services/SettingsService.cs`, add to `ShortcutSettings` record (after line 16):

```csharp
public record ShortcutSettings
{
    public string Toggle { get; init; } = "Ctrl+Alt+X";
    public string Ptt { get; init; } = "Win+L-Ctrl";
    public string Mute { get; init; } = "";
    public string LanguageSwitch { get; init; } = "";
    public string ClipboardAi { get; init; } = "";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "ClipboardAi" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/PureType/Services/SettingsService.cs tests/PureType.Tests/Services/SettingsServiceTests.cs
git commit -m "feat: add ClipboardAi shortcut setting"
```

---

### Task 2: Add 5th shortcut channel to KeyboardHookService

**Files:**
- Modify: `src/PureType/Helpers/KeyboardHookService.cs`

- [ ] **Step 1: Add fields and event for ClipboardAi channel**

After the language switch fields (line 73), add:

```csharp
// ── Clipboard AI shortcut config ──
private int _clipboardAiVKey;
private ModifierKeys _clipboardAiModifiers = ModifierKeys.None;
private bool _clipboardAiFired;
```

After the `LanguageSwitchPressed` event (line 99), add:

```csharp
/// <summary>Fired when the clipboard AI shortcut is pressed.</summary>
public event Action? ClipboardAiPressed;
```

- [ ] **Step 2: Add SetClipboardAiShortcut method**

After `SetLanguageSwitchShortcut` (line 141), add:

```csharp
public void SetClipboardAiShortcut(ModifierKeys modifiers, Key key)
{
    _clipboardAiModifiers = modifiers;
    _clipboardAiVKey = KeyInterop.VirtualKeyFromKey(key);
    _clipboardAiFired = false;
}
```

- [ ] **Step 3: Add detection logic in HookCallback**

After the language switch detection block (lines 259-269), add:

```csharp
// ── Clipboard AI shortcut detection ──
if (vkCode == _clipboardAiVKey && _clipboardAiVKey != 0 && isDown && !_clipboardAiFired)
{
    if (AreModifiersHeld(_clipboardAiModifiers))
    {
        _clipboardAiFired = true;
        ClipboardAiPressed?.Invoke();
    }
}
if (vkCode == _clipboardAiVKey && isUp)
    _clipboardAiFired = false;
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/PureType/Helpers/KeyboardHookService.cs
git commit -m "feat: add ClipboardAi channel to KeyboardHookService"
```

---

## Chunk 2: PromptPickerWindow

### Task 3: Create PromptPickerWindow

**Files:**
- Create: `src/PureType/PromptPickerWindow.xaml`
- Create: `src/PureType/PromptPickerWindow.xaml.cs`

- [ ] **Step 1: Create PromptPickerWindow.xaml**

```xml
<Window x:Class="PureType.PromptPickerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Select Prompt"
        Width="320" Height="300"
        WindowStyle="None" AllowsTransparency="True"
        ResizeMode="NoResize"
        WindowStartupLocation="Manual"
        Background="Transparent"
        ShowInTaskbar="False"
        Topmost="True"
        Deactivated="Window_Deactivated">
    <Border Background="{DynamicResource BackgroundBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="1" CornerRadius="8"
            Padding="12">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- Search -->
            <TextBox x:Name="SearchBox" Grid.Row="0"
                     Padding="8,6" FontSize="14"
                     Background="{DynamicResource SurfaceBrush}"
                     Foreground="{DynamicResource TextBrush}"
                     BorderBrush="{DynamicResource AccentBrush}"
                     BorderThickness="1"
                     TextChanged="SearchBox_TextChanged"
                     PreviewKeyDown="SearchBox_PreviewKeyDown"/>

            <!-- Prompt List -->
            <ListBox x:Name="PromptList" Grid.Row="1"
                     Margin="0,8,0,0"
                     Background="Transparent"
                     BorderThickness="0"
                     Foreground="{DynamicResource TextBrush}"
                     FontSize="13"
                     MouseDoubleClick="PromptList_MouseDoubleClick">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Name}" Padding="4,3"/>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Empty State -->
            <TextBlock x:Name="EmptyMessage" Grid.Row="1"
                       Text="No prompts configured"
                       Visibility="Collapsed"
                       Foreground="{DynamicResource MutedTextBrush}"
                       FontSize="13"
                       HorizontalAlignment="Center"
                       VerticalAlignment="Center"/>

            <!-- Hint -->
            <TextBlock Grid.Row="2" Margin="0,6,0,0"
                       FontSize="11"
                       Foreground="{DynamicResource MutedTextBrush}">
                <Run Text="Enter"/>
                <Run Text=" confirm  "/>
                <Run Text="Shift+Enter"/>
                <Run Text=" → clipboard  "/>
                <Run Text="Esc"/>
                <Run Text=" cancel"/>
            </TextBlock>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 2: Create PromptPickerWindow.xaml.cs**

```csharp
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using PureType.Services;

namespace PureType;

public partial class PromptPickerWindow : Window
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private readonly List<NamedPrompt> _allPrompts;

    /// <summary>The prompt the user selected, or null if cancelled.</summary>
    public NamedPrompt? SelectedPrompt { get; private set; }

    /// <summary>True if user held Shift when confirming (result goes to clipboard only).</summary>
    public bool ShiftHeld { get; private set; }

    public PromptPickerWindow(List<NamedPrompt> prompts)
    {
        InitializeComponent();
        _allPrompts = prompts;

        if (prompts.Count == 0)
        {
            EmptyMessage.Visibility = Visibility.Visible;
            PromptList.Visibility = Visibility.Collapsed;
        }
        else
        {
            PromptList.ItemsSource = prompts;
            PromptList.SelectedIndex = 0;
        }

        Loaded += (_, _) =>
        {
            PositionNearCursor();
            SearchBox.Focus();
        };
    }

    private void PositionNearCursor()
    {
        if (!GetCursorPos(out var pt)) return;

        var dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var x = pt.X * dpiScale;
        var y = pt.Y * dpiScale;

        var screen = SystemParameters.WorkArea;
        if (x + Width > screen.Right) x = screen.Right - Width;
        if (y + Height > screen.Bottom) y = screen.Bottom - Height;
        if (x < screen.Left) x = screen.Left;
        if (y < screen.Top) y = screen.Top;

        Left = x;
        Top = y;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allPrompts
            : _allPrompts.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        PromptList.ItemsSource = filtered;
        if (filtered.Count > 0)
            PromptList.SelectedIndex = 0;

        EmptyMessage.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyMessage.Text = _allPrompts.Count == 0 ? "No prompts configured" : "No matches";
        PromptList.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (PromptList.Items.Count > 0)
                {
                    PromptList.SelectedIndex = Math.Min(PromptList.SelectedIndex + 1, PromptList.Items.Count - 1);
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (PromptList.Items.Count > 0)
                {
                    PromptList.SelectedIndex = Math.Max(PromptList.SelectedIndex - 1, 0);
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                Confirm();
                e.Handled = true;
                break;

            case Key.Escape:
                DialogResult = false;
                Close();
                e.Handled = true;
                break;
        }
    }

    private void PromptList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (PromptList.SelectedItem is not NamedPrompt prompt)
            return;

        SelectedPrompt = prompt;
        ShiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        DialogResult = true;
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible && DialogResult == null)
        {
            DialogResult = false;
            Close();
        }
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/PureType/PromptPickerWindow.xaml src/PureType/PromptPickerWindow.xaml.cs
git commit -m "feat: add PromptPickerWindow for clipboard AI prompt selection"
```

---

## Chunk 3: MainWindow Integration

### Task 4: Change hook lifecycle — install at startup, not on connect

**Files:**
- Modify: `src/PureType/MainWindow.xaml.cs`

This is needed so `ClipboardAiPressed` works even when not connected to a transcription provider.

- [ ] **Step 1: Install hook at startup**

In `MainWindow()` constructor, after the event subscriptions (after line 208), add:

```csharp
_keyboardHook.Install();
```

- [ ] **Step 2: Remove Install from RegisterHotkeys**

In `RegisterHotkeys()` (line 706-715), remove the `_keyboardHook.Install();` call (line 714). The method should only configure shortcuts, not install/uninstall the hook.

```csharp
private void RegisterHotkeys()
{
    _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
    _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
    if (_muteKey != Key.None)
        _keyboardHook.SetMuteShortcut(_muteModifiers, _muteKey);
    if (_langSwitchKey != Key.None)
        _keyboardHook.SetLanguageSwitchShortcut(_langSwitchModifiers, _langSwitchKey);
}
```

- [ ] **Step 3: Remove Uninstall from DisconnectAsync**

In `DisconnectAsync()` (line 682-702), remove the `_keyboardHook.Uninstall();` line (line 688). The hook stays active for clipboard AI even when disconnected.

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/PureType/MainWindow.xaml.cs
git commit -m "refactor: install keyboard hook at startup, not on connect"
```

---

### Task 5: Add ClipboardAi shortcut fields and wiring in MainWindow

**Files:**
- Modify: `src/PureType/MainWindow.xaml.cs`

- [ ] **Step 1: Add private fields**

After the language switch fields (line 39-40), add:

```csharp
private Key _clipboardAiKey = Key.None;
private ModifierKeys _clipboardAiModifiers = ModifierKeys.None;
```

- [ ] **Step 2: Parse shortcut in LoadSettings**

In `LoadSettings()` (after line 237), add:

```csharp
if (!string.IsNullOrEmpty(_settings.Shortcuts.ClipboardAi))
    (_clipboardAiModifiers, _clipboardAiKey) = UiHelper.ParseShortcut(_settings.Shortcuts.ClipboardAi, Key.None);
```

- [ ] **Step 3: Apply shortcut in ApplySettings**

In `ApplySettings()` (after the language switch block, ~line 275), add:

```csharp
if (!string.IsNullOrEmpty(_settings.Shortcuts.ClipboardAi))
{
    (_clipboardAiModifiers, _clipboardAiKey) = UiHelper.ParseShortcut(_settings.Shortcuts.ClipboardAi, Key.None);
}
else
    _clipboardAiKey = Key.None;
```

And in the `if (_connected)` block or unconditionally (since the hook is now always installed), register the shortcut. Update the block starting at line 277 to also handle clipboard AI. Since the hook is always installed now, the clipboard AI shortcut should be registered unconditionally:

After the `if (_connected)` block (line 285), add:

```csharp
if (_clipboardAiKey != Key.None)
    _keyboardHook.SetClipboardAiShortcut(_clipboardAiModifiers, _clipboardAiKey);
```

- [ ] **Step 4: Register ClipboardAi shortcut at startup**

In the constructor, after `_keyboardHook.SetPttShortcut(...)` (line 201) and before `ApplyPromptKeys()`, add:

```csharp
if (_clipboardAiKey != Key.None)
    _keyboardHook.SetClipboardAiShortcut(_clipboardAiModifiers, _clipboardAiKey);
```

- [ ] **Step 5: Subscribe to ClipboardAiPressed event**

In the constructor, after the `LanguageSwitchPressed` subscription (line 208), add:

```csharp
_keyboardHook.ClipboardAiPressed += () => Dispatcher.Invoke(() => _ = HandleClipboardAiAsync());
```

- [ ] **Step 6: Build to verify compilation** (HandleClipboardAiAsync not yet implemented — add stub)

Add a temporary stub:

```csharp
private async Task HandleClipboardAiAsync()
{
    // TODO: implement in next task
    await Task.CompletedTask;
}
```

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/PureType/MainWindow.xaml.cs
git commit -m "feat: wire ClipboardAi shortcut in MainWindow"
```

---

### Task 6: Extend ProcessWithLlmAsync and implement HandleClipboardAiAsync

**Files:**
- Modify: `src/PureType/MainWindow.xaml.cs:856-902`

- [ ] **Step 1: Add `clipboardOnly` parameter to ProcessWithLlmAsync**

Change the signature (line 856) from:

```csharp
private async Task ProcessWithLlmAsync(string text, NamedPrompt namedPrompt)
```

to:

```csharp
private async Task ProcessWithLlmAsync(string text, NamedPrompt namedPrompt, bool clipboardOnly = false)
```

- [ ] **Step 2: Add output routing logic**

Replace the try block for typing (lines 886-895) with:

```csharp
if (clipboardOnly)
{
    System.Windows.Clipboard.SetText(processed);
    Log.Information("LLM result copied to clipboard");
    ToastWindow.ShowToast("Copied to clipboard", Green.Color, autoClose: true);
}
else
{
    try
    {
        await KeyboardInjector.TypeTextAsync(processed);
        Log.Information("LLM result typed at cursor");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "LLM result typing failed, copying to clipboard");
        System.Windows.Clipboard.SetText(processed);
    }
}
```

- [ ] **Step 3: Implement HandleClipboardAiAsync**

Replace the stub with:

```csharp
private async Task HandleClipboardAiAsync()
{
    // 1. Read clipboard
    var clipboardText = System.Windows.Clipboard.GetText();
    if (string.IsNullOrWhiteSpace(clipboardText))
    {
        ToastWindow.ShowToast("Clipboard is empty", Yellow.Color, autoClose: true);
        return;
    }

    // 2. Check LLM configured
    if (!_settings.Llm.Enabled || string.IsNullOrEmpty(_settings.Llm.ApiKey) || string.IsNullOrEmpty(_settings.Llm.Model))
    {
        ToastWindow.ShowToast("LLM not configured", Yellow.Color, autoClose: true);
        return;
    }

    // 3. Show prompt picker
    var prompts = _settings.Llm.Prompts;
    if (prompts.Count == 0)
    {
        ToastWindow.ShowToast("No prompts configured", Yellow.Color, autoClose: true);
        return;
    }

    var picker = new PromptPickerWindow(prompts.ToList());
    if (picker.ShowDialog() != true || picker.SelectedPrompt is null)
        return;

    // 4. Immediate feedback + focus restore delay
    ToastWindow.ShowToast($"AI: {picker.SelectedPrompt.Name} \u2026", Yellow.Color, autoClose: false);
    await Task.Delay(200);

    // 5. Process with LLM
    await ProcessWithLlmAsync(clipboardText, picker.SelectedPrompt, picker.ShiftHeld);
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/PureType/MainWindow.xaml.cs
git commit -m "feat: implement clipboard AI processing with prompt picker"
```

---

## Chunk 4: Settings UI

### Task 7: Add Clipboard AI shortcut row to SettingsWindow

**Files:**
- Modify: `src/PureType/SettingsWindow.xaml`
- Modify: `src/PureType/SettingsWindow.xaml.cs`

- [ ] **Step 1: Add XAML for Clipboard AI shortcut row**

In `SettingsWindow.xaml`, after the Language Switch shortcut StackPanel (after line 279), add:

```xml
<!-- Clipboard AI Shortcut -->
<StackPanel Margin="0,0,0,12"
            Tag="clipboard ai shortcut hotkey process llm"
            ToolTip="Optional hotkey to apply AI post-processing to clipboard text. Opens a prompt picker. Press Delete or Backspace to clear.">
    <TextBlock Text="CLIPBOARD AI" Foreground="{DynamicResource LabelBrush}"
               FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
    <TextBox x:Name="ClipboardAiShortcutBox"
             Background="{DynamicResource SurfaceBrush}" Foreground="{DynamicResource TextBrush}"
             BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"
             Padding="10,5" FontSize="13"
             IsReadOnly="True" Cursor="Hand"
             Text=""
             GotFocus="ShortcutBox_GotFocus"
             LostFocus="ShortcutBox_LostFocus"
             PreviewKeyDown="ShortcutBox_PreviewKeyDown"/>
</StackPanel>
```

- [ ] **Step 2: Add fields and populate from settings**

In `SettingsWindow.xaml.cs`, add fields (after line 24):

```csharp
private Key _clipboardAiKey;
private ModifierKeys _clipboardAiModifiers;
```

In `PopulateFromSettings()`, after the LanguageSwitch block (after line 82), add:

```csharp
if (!string.IsNullOrEmpty(settings.Shortcuts.ClipboardAi))
{
    (_clipboardAiModifiers, _clipboardAiKey) = UiHelper.ParseShortcut(settings.Shortcuts.ClipboardAi, Key.None);
    ClipboardAiShortcutBox.Text = UiHelper.FormatShortcut(_clipboardAiModifiers, _clipboardAiKey);
}
```

- [ ] **Step 3: Add ClipboardAi to collision detection in AssignShortcut**

In `AssignShortcut()` (line 631), update the array:

```csharp
var otherBoxes = new[] { ToggleShortcutBox, PttShortcutBox, MuteShortcutBox, LangSwitchShortcutBox, ClipboardAiShortcutBox }
    .Where(b => b != box);
```

Add assignment branch (after line 643):

```csharp
else if (box == ClipboardAiShortcutBox) { _clipboardAiKey = key; _clipboardAiModifiers = modifiers; }
```

- [ ] **Step 4: Add ClipboardAi to OnRecordingWinPlusModifier**

In `OnRecordingWinPlusModifier()` (line 534), update the check:

```csharp
(box != ToggleShortcutBox && box != PttShortcutBox && box != MuteShortcutBox && box != LangSwitchShortcutBox && box != ClipboardAiShortcutBox)
```

- [ ] **Step 5: Add clear support in ShortcutBox_PreviewKeyDown**

In `ShortcutBox_PreviewKeyDown()`, after the LangSwitchShortcutBox clear block (after line 519), add:

```csharp
if (box == ClipboardAiShortcutBox)
{
    _clipboardAiKey = Key.None;
    _clipboardAiModifiers = ModifierKeys.None;
    box.Text = "";
    box.Foreground = (SolidColorBrush)FindResource("TextBrush");
    Keyboard.ClearFocus();
    return;
}
```

- [ ] **Step 6: Add ClipboardAi to Save_Click**

In `Save_Click()` (line 162-168), update the `ShortcutSettings` construction:

```csharp
Shortcuts = new ShortcutSettings
{
    Toggle = UiHelper.FormatShortcut(_toggleModifiers, _toggleKey),
    Ptt = UiHelper.FormatShortcut(_pttModifiers, _pttKey),
    Mute = _muteKey != Key.None ? UiHelper.FormatShortcut(_muteModifiers, _muteKey) : "",
    LanguageSwitch = _langSwitchKey != Key.None ? UiHelper.FormatShortcut(_langSwitchModifiers, _langSwitchKey) : "",
    ClipboardAi = _clipboardAiKey != Key.None ? UiHelper.FormatShortcut(_clipboardAiModifiers, _clipboardAiKey) : "",
},
```

- [ ] **Step 7: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 8: Run all tests**

Run: `dotnet test`
Expected: All tests pass

- [ ] **Step 9: Commit**

```bash
git add src/PureType/SettingsWindow.xaml src/PureType/SettingsWindow.xaml.cs
git commit -m "feat: add Clipboard AI shortcut to settings UI"
```

---

## Chunk 5: Final Integration & Verification

### Task 8: Manual smoke test

- [ ] **Step 1: Run the app**

Run: `dotnet run --project src/PureType`

- [ ] **Step 2: Test shortcut configuration**

1. Open Settings
2. Verify "CLIPBOARD AI" shortcut row appears under SHORTCUTS
3. Click the field, press a key combination (e.g. `Ctrl+Alt+C`)
4. Save settings
5. Re-open Settings — verify the shortcut persists

- [ ] **Step 3: Test clipboard AI flow**

1. Configure LLM (enable, set API key, model, add at least one prompt)
2. Copy some text to clipboard
3. Press the configured shortcut
4. Verify PromptPickerWindow appears centered
5. Type to filter prompts
6. Press Enter — verify result is typed at cursor
7. Repeat with Shift+Enter — verify result goes to clipboard

- [ ] **Step 4: Test edge cases**

1. Press shortcut with empty clipboard — verify "Clipboard is empty" toast
2. Press shortcut with LLM disabled — verify "LLM not configured" toast
3. Press shortcut with no prompts — verify "No prompts configured" toast
4. Press Escape in picker — verify it closes without action
5. Click outside picker — verify it auto-closes

- [ ] **Step 5: Test shortcut works without transcription connection**

1. Don't connect to Deepgram/Whisper
2. Press clipboard AI shortcut — verify it works independently

- [ ] **Step 6: Run all tests one final time**

Run: `dotnet test`
Expected: All tests pass

- [ ] **Step 7: Final commit if any fixes were needed**

```bash
git add -u
git commit -m "fix: address issues found during clipboard AI smoke test"
```
