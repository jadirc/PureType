# UI Polish Round 3 — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add themed tooltips, WPF tray context menu, overlay-dissolve theme transitions, and a minimal first-run wizard.

**Architecture:** Each feature is independent. Tooltip styling goes in the theme resource dictionaries. The tray menu replaces WinForms `ContextMenuStrip` with a WPF borderless window. Theme transitions use a screenshot overlay in `ThemeManager`. The first-run wizard is a new `WelcomeWindow` shown before `MainWindow` when no settings file exists.

**Tech Stack:** WPF, C# .NET 8, XAML resource dictionaries, Win32 interop (tray icon positioning)

---

### Task 1: Tooltip Styling — Dark Theme

**Files:**
- Modify: `src/VoiceDictation/Themes/Dark.xaml` (insert before closing `</ResourceDictionary>`, after scrollbar style ~line 288)

**Step 1: Add ToolTip style to Dark.xaml**

Insert before the closing `</ResourceDictionary>` tag (line 290):

```xml
    <!-- ToolTip -->
    <Style TargetType="{x:Type ToolTip}">
        <Setter Property="Background" Value="#313244"/>
        <Setter Property="Foreground" Value="#CDD6F4"/>
        <Setter Property="BorderBrush" Value="#45475A"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,5"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type ToolTip}">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6" Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <Border.Effect>
                            <DropShadowEffect Color="Black" BlurRadius="8" ShadowDepth="2" Opacity="0.3"/>
                        </Border.Effect>
                        <ContentPresenter/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/VoiceDictation/Themes/Dark.xaml
git commit -m "feat: add themed tooltip style to Dark theme"
```

---

### Task 2: Tooltip Styling — Light Theme

**Files:**
- Modify: `src/VoiceDictation/Themes/Light.xaml` (insert before closing `</ResourceDictionary>`, after scrollbar style ~line 288)

**Step 1: Add ToolTip style to Light.xaml**

Insert before the closing `</ResourceDictionary>` tag (line 290):

```xml
    <!-- ToolTip -->
    <Style TargetType="{x:Type ToolTip}">
        <Setter Property="Background" Value="#DCE0E8"/>
        <Setter Property="Foreground" Value="#4C4F69"/>
        <Setter Property="BorderBrush" Value="#BCC0CC"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,5"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type ToolTip}">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6" Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <Border.Effect>
                            <DropShadowEffect Color="Black" BlurRadius="8" ShadowDepth="2" Opacity="0.15"/>
                        </Border.Effect>
                        <ContentPresenter/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

Note: Light theme uses lower shadow opacity (0.15 vs 0.3) since shadows are more noticeable on light backgrounds.

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/VoiceDictation/Themes/Light.xaml
git commit -m "feat: add themed tooltip style to Light theme"
```

---

### Task 3: WPF Tray Context Menu — XAML Window

**Files:**
- Create: `src/VoiceDictation/TrayMenuWindow.xaml`
- Create: `src/VoiceDictation/TrayMenuWindow.xaml.cs`

**Step 1: Create TrayMenuWindow.xaml**

This is a borderless, transparent WPF window that looks like a context menu:

```xml
<Window x:Class="VoiceDictation.TrayMenuWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None"
        AllowsTransparency="True"
        Topmost="True"
        ShowInTaskbar="False"
        ShowActivated="True"
        Focusable="True"
        SizeToContent="WidthAndHeight"
        Background="Transparent">
    <Border Background="{DynamicResource SurfaceBrush}" CornerRadius="8"
            BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"
            Padding="4">
        <Border.Effect>
            <DropShadowEffect Color="Black" BlurRadius="16" ShadowDepth="4" Opacity="0.4"/>
        </Border.Effect>
        <StackPanel x:Name="MenuPanel" Width="200">
            <!-- Status label -->
            <TextBlock x:Name="StatusLabel" Text="Not connected"
                       Foreground="{DynamicResource RedBrush}"
                       FontSize="12" FontWeight="SemiBold"
                       Margin="12,8,12,4"/>
            <Separator Background="{DynamicResource BorderBrush}" Margin="8,4"/>

            <!-- Menu items are created in code-behind -->
        </StackPanel>
    </Border>
</Window>
```

**Step 2: Create TrayMenuWindow.xaml.cs**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VoiceDictation;

public partial class TrayMenuWindow : Window
{
    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? MuteToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ExportRequested;
    public event Action? HistoryRequested;
    public event Action? AboutRequested;
    public event Action? ShowRequested;
    public event Action? ExitRequested;

    private bool _connected;
    private bool _muted;
    private readonly Border _connectItem;
    private readonly TextBlock _connectText;
    private readonly TextBlock _muteCheck;

    public TrayMenuWindow()
    {
        InitializeComponent();

        (_connectItem, _connectText) = AddClickItem("Connect", () =>
        {
            if (_connected) DisconnectRequested?.Invoke();
            else ConnectRequested?.Invoke();
            Close();
        });

        var mutePanel = new StackPanel { Orientation = Orientation.Horizontal };
        _muteCheck = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed
        };
        mutePanel.Children.Add(_muteCheck);
        mutePanel.Children.Add(new TextBlock { Text = "Mute" });
        AddClickItem(mutePanel, () =>
        {
            MuteToggleRequested?.Invoke();
            Close();
        });

        AddSeparator();
        AddClickItem("Settings", () => { SettingsRequested?.Invoke(); Close(); });
        AddClickItem("Export Transcript", () => { ExportRequested?.Invoke(); Close(); });
        AddClickItem("Transcript History", () => { HistoryRequested?.Invoke(); Close(); });
        AddClickItem("About", () => { AboutRequested?.Invoke(); Close(); });
        AddClickItem("Open", () => { ShowRequested?.Invoke(); Close(); });

        AddSeparator();
        AddClickItem("Exit", () => { ExitRequested?.Invoke(); Close(); });

        Deactivated += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public void UpdateState(bool connected, bool recording, bool muted)
    {
        _connected = connected;
        _muted = muted;
        _connectText.Text = connected ? "Disconnect" : "Connect";
        _muteCheck.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;

        if (muted)
        {
            StatusLabel.Text = "Muted";
            StatusLabel.Foreground = (Brush)FindResource("YellowBrush");
        }
        else if (recording)
        {
            StatusLabel.Text = "Recording";
            StatusLabel.Foreground = (Brush)FindResource("RedBrush");
        }
        else if (connected)
        {
            StatusLabel.Text = "Connected";
            StatusLabel.Foreground = (Brush)FindResource("GreenBrush");
        }
        else
        {
            StatusLabel.Text = "Not connected";
            StatusLabel.Foreground = (Brush)FindResource("RedBrush");
        }
    }

    private (Border border, TextBlock textBlock) AddClickItem(string text, Action onClick)
    {
        var tb = new TextBlock { Text = text };
        var border = AddClickItem((UIElement)tb, onClick);
        return (border, tb);
    }

    private Border AddClickItem(UIElement content, Action onClick)
    {
        var tb = content is TextBlock t ? t : null;
        var border = new Border
        {
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = content is TextBlock textBlock
                ? new TextBlock
                {
                    Text = textBlock.Text,
                    Foreground = (Brush)FindResource("TextBrush"),
                    FontSize = 13
                }
                : content
        };

        // For non-TextBlock content (like the mute panel), set foreground on children
        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is TextBlock ptb)
                {
                    ptb.Foreground = (Brush)FindResource("TextBrush");
                    ptb.FontSize = 13;
                }
            }
            border.Child = panel;
        }

        var hoverBrush = (Brush)FindResource("BorderBrush");
        border.MouseEnter += (_, _) => border.Background = hoverBrush;
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) => onClick();
        border.Cursor = Cursors.Hand;

        MenuPanel.Children.Add(border);
        return border;
    }

    private void AddSeparator()
    {
        MenuPanel.Children.Add(new Separator
        {
            Background = (Brush)FindResource("BorderBrush"),
            Margin = new Thickness(8, 4, 8, 4)
        });
    }
}
```

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/VoiceDictation/TrayMenuWindow.xaml src/VoiceDictation/TrayMenuWindow.xaml.cs
git commit -m "feat: add WPF tray context menu window"
```

---

### Task 4: Wire TrayIconManager to use WPF Menu

**Files:**
- Modify: `src/VoiceDictation/Helpers/TrayIconManager.cs`

**Step 1: Replace ContextMenuStrip with WPF tray menu**

Rewrite `TrayIconManager` to:
1. Remove `ContextMenuStrip` and all WinForms menu items
2. Remove `_statusLabel`, `_connectItem`, `_muteItem` fields
3. On right-click, create and show a `TrayMenuWindow` positioned near the cursor
4. Keep `NotifyIcon` for the icon + left-click + status icon overlay
5. Keep all existing events (`ConnectRequested`, `DisconnectRequested`, etc.)
6. Use `System.Windows.Forms.Cursor.Position` to get click position for window placement

Key changes to constructor:
```csharp
// Remove: var menu = new ContextMenuStrip(); ... _trayIcon.ContextMenuStrip = menu;
// Add right-click handler:
_trayIcon.MouseClick += (_, e) =>
{
    if (e.Button == System.Windows.Forms.MouseButtons.Left)
        ShowRequested?.Invoke();
    else if (e.Button == System.Windows.Forms.MouseButtons.Right)
        ShowTrayMenu();
};
```

Add `ShowTrayMenu()` method:
```csharp
private void ShowTrayMenu()
{
    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        var menu = new TrayMenuWindow();
        menu.ConnectRequested += () => ConnectRequested?.Invoke();
        menu.DisconnectRequested += () => DisconnectRequested?.Invoke();
        menu.MuteToggleRequested += () => MuteToggleRequested?.Invoke();
        menu.SettingsRequested += () => SettingsRequested?.Invoke();
        menu.ExportRequested += () => ExportRequested?.Invoke();
        menu.HistoryRequested += () => HistoryRequested?.Invoke();
        menu.AboutRequested += () => AboutRequested?.Invoke();
        menu.ShowRequested += () => ShowRequested?.Invoke();
        menu.ExitRequested += () => ExitRequested?.Invoke();
        menu.UpdateState(_connected, _recording, _muted);

        // Position near cursor (above tray area)
        var pos = System.Windows.Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(menu);
        menu.Left = pos.X / dpi.DpiScaleX - 100;
        menu.Top = pos.Y / dpi.DpiScaleY - 10;

        menu.Loaded += (_, _) =>
        {
            // Adjust so menu appears above the click point
            menu.Top = pos.Y / dpi.DpiScaleY - menu.ActualHeight;
            // Keep on screen
            if (menu.Left < 0) menu.Left = 0;
            if (menu.Top < 0) menu.Top = 0;
        };

        menu.Show();
    });
}
```

Update `Update()` to store state for the WPF menu:
```csharp
private bool _recording;
private bool _muted;

public void Update(bool connected, bool recording, bool muted)
{
    _connected = connected;
    _recording = recording;
    _muted = muted;
    // ... keep the icon overlay logic unchanged ...
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/VoiceDictation/Helpers/TrayIconManager.cs
git commit -m "feat: wire tray icon to WPF context menu, remove WinForms menu"
```

---

### Task 5: Overlay Dissolve Theme Transition

**Files:**
- Modify: `src/VoiceDictation/Helpers/ThemeManager.cs`

**Step 1: Add overlay dissolve to ThemeManager**

Add a public method and the dissolve logic. The key insight: we capture each visible window as a bitmap, overlay it, swap the theme, then fade the overlay out.

Replace `ApplyResolved()` with:

```csharp
private static void ApplyResolved()
{
    var resolved = _currentSetting == "Auto" ? DetectSystemTheme() : _currentSetting;
    var uri = resolved == "Light" ? LightUri : DarkUri;
    var dict = new ResourceDictionary { Source = uri };

    var windows = Application.Current.Windows
        .OfType<Window>()
        .Where(w => w.IsVisible && w.ActualWidth > 0 && w.ActualHeight > 0)
        .ToList();

    // Capture screenshots of all visible windows
    var overlays = new List<(Window window, Image overlay)>();
    foreach (var win in windows)
    {
        var overlay = CaptureOverlay(win);
        if (overlay != null)
            overlays.Add((win, overlay));
    }

    // Swap theme
    var merged = Application.Current.Resources.MergedDictionaries;
    if (merged.Count > 0)
        merged.RemoveAt(0);
    merged.Insert(0, dict);

    // Fade out overlays
    foreach (var (win, overlay) in overlays)
        FadeOutOverlay(win, overlay);
}

private static Image? CaptureOverlay(Window window)
{
    try
    {
        var content = window.Content as UIElement;
        if (content == null) return null;

        var dpi = VisualTreeHelper.GetDpi(content);
        var width = (int)(content.RenderSize.Width * dpi.DpiScaleX);
        var height = (int)(content.RenderSize.Height * dpi.DpiScaleY);
        if (width <= 0 || height <= 0) return null;

        var rtb = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(content);

        var image = new Image
        {
            Source = rtb,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };

        return image;
    }
    catch
    {
        return null;
    }
}

private static void FadeOutOverlay(Window window, Image overlay)
{
    // Find or create an overlay container
    var content = window.Content as UIElement;
    if (content == null) return;

    // Wrap in a Grid if not already
    Grid grid;
    if (window.Content is Grid existingGrid)
    {
        grid = existingGrid;
    }
    else
    {
        grid = new Grid();
        window.Content = null;
        grid.Children.Add(content);
        window.Content = grid;
    }

    // Add overlay on top
    Grid.SetRowSpan(overlay, Math.Max(grid.RowDefinitions.Count, 1));
    Grid.SetColumnSpan(overlay, Math.Max(grid.ColumnDefinitions.Count, 1));
    Panel.SetZIndex(overlay, 9999);
    grid.Children.Add(overlay);

    // Animate fade out
    var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
    {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
    };
    animation.Completed += (_, _) =>
    {
        grid.Children.Remove(overlay);
        // Restore original content if we wrapped it
        if (grid != content && grid.Children.Count == 1 && grid.Children[0] == content)
        {
            grid.Children.Clear();
            window.Content = content;
        }
    };
    overlay.BeginAnimation(UIElement.OpacityProperty, animation);
}
```

Add required usings at top:
```csharp
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/VoiceDictation/Helpers/ThemeManager.cs
git commit -m "feat: add overlay dissolve animation for theme transitions"
```

---

### Task 6: First-Run Wizard — WelcomeWindow XAML

**Files:**
- Create: `src/VoiceDictation/WelcomeWindow.xaml`

**Step 1: Create WelcomeWindow.xaml**

```xml
<Window x:Class="VoiceDictation.WelcomeWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Welcome to Voice Dictation"
        Width="400" Height="380"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        Background="{DynamicResource BackgroundBrush}"
        FontFamily="Segoe UI">

    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="24"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="16"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Title -->
        <StackPanel Grid.Row="0" HorizontalAlignment="Center">
            <TextBlock Text="Voice Dictation" Foreground="{DynamicResource TextBrush}"
                       FontSize="22" FontWeight="Bold" HorizontalAlignment="Center"/>
            <TextBlock Text="Choose your transcription engine to get started."
                       Foreground="{DynamicResource TextDimBrush}"
                       FontSize="13" HorizontalAlignment="Center" Margin="0,6,0,0"/>
        </StackPanel>

        <!-- Provider Selection -->
        <StackPanel Grid.Row="2">
            <TextBlock Text="PROVIDER" Foreground="{DynamicResource LabelBrush}"
                       FontSize="11" FontWeight="SemiBold" Margin="0,0,0,8"/>

            <!-- Whisper Card -->
            <Border x:Name="WhisperCard" Background="{DynamicResource SurfaceBrush}"
                    BorderBrush="{DynamicResource AccentBrush}" BorderThickness="2"
                    CornerRadius="8" Padding="14,10" Margin="0,0,0,8"
                    Cursor="Hand" MouseLeftButtonUp="WhisperCard_Click">
                <StackPanel>
                    <TextBlock Text="Whisper (Local)" Foreground="{DynamicResource TextBrush}"
                               FontSize="14" FontWeight="SemiBold"/>
                    <TextBlock Text="Free, offline, runs on your GPU. No API key needed."
                               Foreground="{DynamicResource TextDimBrush}" FontSize="12"
                               Margin="0,2,0,0"/>
                </StackPanel>
            </Border>

            <!-- Deepgram Card -->
            <Border x:Name="DeepgramCard" Background="{DynamicResource SurfaceBrush}"
                    BorderBrush="{DynamicResource BorderBrush}" BorderThickness="2"
                    CornerRadius="8" Padding="14,10"
                    Cursor="Hand" MouseLeftButtonUp="DeepgramCard_Click">
                <StackPanel>
                    <TextBlock Text="Deepgram (Cloud)" Foreground="{DynamicResource TextBrush}"
                               FontSize="14" FontWeight="SemiBold"/>
                    <TextBlock Text="Fast cloud transcription. Requires an API key."
                               Foreground="{DynamicResource TextDimBrush}" FontSize="12"
                               Margin="0,2,0,0"/>
                </StackPanel>
            </Border>
        </StackPanel>

        <!-- API Key (Deepgram only) -->
        <StackPanel x:Name="ApiKeyPanel" Grid.Row="4" Visibility="Collapsed">
            <TextBlock Text="API KEY" Foreground="{DynamicResource LabelBrush}"
                       FontSize="11" FontWeight="SemiBold" Margin="0,0,0,5"/>
            <TextBox x:Name="ApiKeyBox"
                     Background="{DynamicResource SurfaceBrush}"
                     Foreground="{DynamicResource TextBrush}"
                     BorderBrush="{DynamicResource BorderBrush}"
                     BorderThickness="1" Padding="10,8" FontSize="13"/>
        </StackPanel>

        <!-- Get Started -->
        <Button Grid.Row="6" x:Name="StartButton" Content="Get Started"
                Click="StartButton_Click"
                Background="{DynamicResource AccentBrush}"
                Foreground="{DynamicResource AccentForegroundBrush}"
                FontWeight="SemiBold" FontSize="14"
                BorderThickness="0" Padding="0,12"
                HorizontalAlignment="Stretch" Cursor="Hand"/>
    </Grid>
</Window>
```

**Step 2: Build to verify (will fail — code-behind not yet created)**

Expected: Build error (partial class missing)

---

### Task 7: First-Run Wizard — WelcomeWindow Code-Behind

**Files:**
- Create: `src/VoiceDictation/WelcomeWindow.xaml.cs`

**Step 1: Create WelcomeWindow.xaml.cs**

```csharp
using System.Windows;
using System.Windows.Media;

namespace VoiceDictation;

public partial class WelcomeWindow : Window
{
    private string _selectedProvider = "whisper";

    public string SelectedProvider => _selectedProvider;
    public string EnteredApiKey => ApiKeyBox.Text.Trim();
    public bool Completed { get; private set; }

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void WhisperCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProvider = "whisper";
        WhisperCard.BorderBrush = (Brush)FindResource("AccentBrush");
        DeepgramCard.BorderBrush = (Brush)FindResource("BorderBrush");
        ApiKeyPanel.Visibility = Visibility.Collapsed;
    }

    private void DeepgramCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProvider = "deepgram";
        DeepgramCard.BorderBrush = (Brush)FindResource("AccentBrush");
        WhisperCard.BorderBrush = (Brush)FindResource("BorderBrush");
        ApiKeyPanel.Visibility = Visibility.Visible;
        ApiKeyBox.Focus();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider == "deepgram" && string.IsNullOrWhiteSpace(ApiKeyBox.Text))
        {
            MessageBox.Show("Please enter a Deepgram API key.", "API Key Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ApiKeyBox.Focus();
            return;
        }

        Completed = true;
        Close();
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/VoiceDictation/WelcomeWindow.xaml src/VoiceDictation/WelcomeWindow.xaml.cs
git commit -m "feat: add WelcomeWindow for first-run wizard"
```

---

### Task 8: First-Run Wizard — Wire into App Startup

**Files:**
- Modify: `src/VoiceDictation/Services/SettingsService.cs` (add `IsFirstRun` property)
- Modify: `src/VoiceDictation/MainWindow.xaml.cs` (show wizard before auto-connect)

**Step 1: Add IsFirstRun to SettingsService**

Add after `DefaultJsonPath` (line 75):

```csharp
public bool IsFirstRun => !File.Exists(_jsonPath) && !(_useDefaultPath && File.Exists(TxtPath));
```

**Step 2: Show WelcomeWindow before auto-connect in MainWindow**

In `MainWindow()`, between `LoadSettings()` (line 91) and `ThemeManager.Apply()` (line 92), add:

```csharp
// First-run wizard
if (_settingsService.IsFirstRun)
{
    var welcome = new WelcomeWindow();
    welcome.ShowDialog();
    if (welcome.Completed)
    {
        _settings = _settings with
        {
            Transcription = _settings.Transcription with
            {
                Provider = welcome.SelectedProvider,
                ApiKey = welcome.EnteredApiKey,
            }
        };
        _settingsService.Save(_settings);
    }
}
```

Important: `IsFirstRun` must be checked **before** `Load()` is called — but `Load()` is called inside `LoadSettings()`. So check the flag **before** `LoadSettings()`:

```csharp
var isFirstRun = _settingsService.IsFirstRun;
LoadSettings();
ThemeManager.Apply(_settings.Window.Theme);

if (isFirstRun)
{
    var welcome = new WelcomeWindow();
    welcome.ShowDialog();
    if (welcome.Completed)
    {
        _settings = _settings with
        {
            Transcription = _settings.Transcription with
            {
                Provider = welcome.SelectedProvider,
                ApiKey = welcome.EnteredApiKey,
            }
        };
        _settingsService.Save(_settings);
        UiHelper.SelectComboByTag(ProviderCombo, welcome.SelectedProvider);
    }
}
```

Update the constructor to use `isFirstRun`:
- Move `var isFirstRun = _settingsService.IsFirstRun;` to just before `LoadSettings();`
- Insert the wizard block right after `ThemeManager.Apply(...)` and `SoundFeedback.Init(...)`

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/VoiceDictation/Services/SettingsService.cs src/VoiceDictation/MainWindow.xaml.cs
git commit -m "feat: show first-run wizard on initial startup"
```

---

### Task 9: Manual Smoke Test

**Step 1: Run the app**

Run: `dotnet run --project src/VoiceDictation`

**Step 2: Verify each feature**

1. **Tooltips:** Hover over the Settings gear icon — should show dark/rounded tooltip matching theme
2. **Tray menu:** Right-click the tray icon — should show themed WPF popup (not WinForms menu)
3. **Theme transition:** Open Settings, switch theme — should see smooth fade transition
4. **First-run:** Delete `%LOCALAPPDATA%\VoiceDictation\settings.json`, restart — should show WelcomeWindow with Whisper preselected

**Step 3: Fix any issues found**

**Step 4: Final commit if any fixes needed**
