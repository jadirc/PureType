using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using PureType.Helpers;
using PureType.Services;

namespace PureType;

public partial class SettingsWindow : Window
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly ReplacementService _replacements;
    private readonly string _providerTag;

    // Shortcut recorder state
    private Key _toggleKey;
    private ModifierKeys _toggleModifiers;
    private Key _pttKey;
    private ModifierKeys _pttModifiers;
    private Key _muteKey;
    private ModifierKeys _muteModifiers;
    private Key _langSwitchKey;
    private ModifierKeys _langSwitchModifiers;
    private string? _shortcutBoxPreviousText;

    // Prompt list
    private List<NamedPrompt> _prompts = new();

    public AppSettings ResultSettings { get; private set; } = new();

    public SettingsWindow(AppSettings settings, string providerTag,
                          KeyboardHookService keyboardHook,
                          ReplacementService replacements)
    {
        InitializeComponent();

        _keyboardHook = keyboardHook;
        _replacements = replacements;
        _providerTag = providerTag;

        // Restore dialog size
        if (settings.Window.SettingsWidth.HasValue && settings.Window.SettingsWidth.Value >= MinWidth)
            Width = settings.Window.SettingsWidth.Value;
        if (settings.Window.SettingsHeight.HasValue && settings.Window.SettingsHeight.Value >= MinHeight)
            Height = settings.Window.SettingsHeight.Value;

        PopulateFromSettings(settings);
        SetProviderVisibility(providerTag);
        PopulateWhisperModels(settings.Transcription.WhisperModel);

        _keyboardHook.RecordingWinPlusModifier += OnRecordingWinPlusModifier;
    }

    private void PopulateFromSettings(AppSettings settings)
    {
        // Transcription
        UiHelper.SelectComboByTag(ProviderCombo, settings.Transcription.Provider);
        ApiKeyBox.Password = settings.Transcription.ApiKey;
        KeywordsBox.Text = settings.Transcription.Keywords;
        UiHelper.SelectComboByTag(LanguageCombo, settings.Transcription.Language);

        // Shortcuts
        (_toggleModifiers, _toggleKey) = UiHelper.ParseShortcut(settings.Shortcuts.Toggle, Key.X);
        (_pttModifiers, _pttKey) = UiHelper.ParseShortcut(settings.Shortcuts.Ptt, Key.LeftCtrl);
        ToggleShortcutBox.Text = UiHelper.FormatShortcut(_toggleModifiers, _toggleKey);
        PttShortcutBox.Text = UiHelper.FormatShortcut(_pttModifiers, _pttKey);
        if (!string.IsNullOrEmpty(settings.Shortcuts.Mute))
        {
            (_muteModifiers, _muteKey) = UiHelper.ParseShortcut(settings.Shortcuts.Mute, Key.None);
            MuteShortcutBox.Text = UiHelper.FormatShortcut(_muteModifiers, _muteKey);
        }
        if (!string.IsNullOrEmpty(settings.Shortcuts.LanguageSwitch))
        {
            (_langSwitchModifiers, _langSwitchKey) = UiHelper.ParseShortcut(settings.Shortcuts.LanguageSwitch, Key.None);
            LangSwitchShortcutBox.Text = UiHelper.FormatShortcut(_langSwitchModifiers, _langSwitchKey);
        }
        // Audio
        UiHelper.SelectComboByTag(ToneCombo, settings.Audio.Tone);
        VadCheck.IsChecked = settings.Audio.Vad;
        AutoCapitalizeCheck.IsChecked = settings.Audio.AutoCapitalize;
        UiHelper.SelectComboByTag(InputModeCombo, settings.Audio.InputMode);
        InputDelayBox.Text = settings.Audio.InputDelayMs.ToString();

        // AI Post-Processing
        LlmEnabledCheck.IsChecked = settings.Llm.Enabled;
        LlmSettingsPanel.Visibility = settings.Llm.Enabled ? Visibility.Visible : Visibility.Collapsed;
        LlmApiKeyBox.Password = settings.Llm.ApiKey;
        LlmBaseUrlCombo.Text = settings.Llm.BaseUrl;
        LlmModelCombo.Text = settings.Llm.Model;
        _prompts = settings.Llm.Prompts.ToList();
        PromptListBox.ItemsSource = _prompts;

        // General
        AutostartCheck.IsChecked = IsAutostartEnabled();
        StartMinimizedCheck.IsChecked = settings.Window.StartMinimized;
        ShowOverlayCheck.IsChecked = settings.Window.ShowOverlay;
        UiHelper.SelectComboByTag(ThemeCombo, settings.Window.Theme);
        UiHelper.SelectComboByTag(LogLevelCombo, settings.Window.LogLevel);
    }

    private void SetProviderVisibility(string providerTag)
    {
        var isWhisper = providerTag == "whisper";
        WhisperModelPanel.Visibility = isWhisper ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = isWhisper ? Visibility.Collapsed : Visibility.Visible;
        KeywordsPanel.Visibility = isWhisper ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (ProviderCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        SetProviderVisibility((string)(item.Tag ?? "deepgram"));
    }

    private void PopulateWhisperModels(string selectedModel)
    {
        UiHelper.PopulateWhisperModelCombo(WhisperModelCombo, selectedModel);
    }

    // ── Save / Cancel ─────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var langItem = LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var whisperModelItem = WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var toneItem = ToneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;

        ResultSettings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                ApiKey = ApiKeyBox.Password.Trim(),
                Language = (string)(langItem?.Tag ?? "de"),
                Provider = (string)(providerItem?.Tag ?? _providerTag),
                WhisperModel = (string)(whisperModelItem?.Tag ?? "tiny"),
                Keywords = KeywordsBox.Text.Trim(),
            },
            Shortcuts = new ShortcutSettings
            {
                Toggle = UiHelper.FormatShortcut(_toggleModifiers, _toggleKey),
                Ptt = UiHelper.FormatShortcut(_pttModifiers, _pttKey),
                Mute = _muteKey != Key.None ? UiHelper.FormatShortcut(_muteModifiers, _muteKey) : "",
                LanguageSwitch = _langSwitchKey != Key.None ? UiHelper.FormatShortcut(_langSwitchModifiers, _langSwitchKey) : "",
            },
            Audio = new AudioSettings
            {
                Tone = (string)(toneItem?.Tag ?? "Gentle"),
                Vad = VadCheck.IsChecked == true,
                AutoCapitalize = AutoCapitalizeCheck.IsChecked == true,
                InputMode = (string)((InputModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "Type"),
                InputDelayMs = int.TryParse(InputDelayBox.Text, out var delay) ? Math.Max(0, delay) : 0,
            },
            Llm = new LlmSettings
            {
                Enabled = LlmEnabledCheck.IsChecked == true,
                ApiKey = LlmApiKeyBox.Password.Trim(),
                BaseUrl = LlmBaseUrlCombo.Text.Trim(),
                Model = LlmModelCombo.Text.Trim(),
                Prompts = _prompts.ToList(),
            },
            Window = new WindowSettings
            {
                StartMinimized = StartMinimizedCheck.IsChecked == true,
                ShowOverlay = ShowOverlayCheck.IsChecked == true,
                SettingsWidth = Width,
                SettingsHeight = Height,
                Theme = (ThemeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "Dark",
                LogLevel = (LogLevelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "Information",
            },
        };

        SetAutostart(AutostartCheck.IsChecked == true);
        DialogResult = true;
        MainWindow.SetLogLevel(ResultSettings.Window.LogLevel);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _keyboardHook.RecordingWinPlusModifier -= OnRecordingWinPlusModifier;
        _keyboardHook.SuppressWinKey = false;
        base.OnClosing(e);
    }

    // ── LLM Enabled Toggle ────────────────────────────────────────────────

    private void LlmEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        LlmSettingsPanel.Visibility = LlmEnabledCheck.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Model Download ────────────────────────────────────────────────────

    private async void DownloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (WhisperModelCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var modelName = (string)item.Tag;

        if (WhisperModelManager.IsModelDownloaded(modelName))
        {
            MessageBox.Show("Model is already downloaded.", "Info",
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

            PopulateWhisperModels(modelName);
            MessageBox.Show($"Model '{modelName}' downloaded successfully.", "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Model download failed");
            MessageBox.Show($"Download failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadModelButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    // ── Fetch Models ──────────────────────────────────────────────────────

    private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = LlmApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Log.Warning("Cannot fetch models: no API key");
            return;
        }

        var baseUrl = LlmBaseUrlCombo.Text.Trim();
        var previousModel = LlmModelCombo.Text;

        try
        {
            var models = await FetchModelsAsync(baseUrl, apiKey);
            LlmModelCombo.Items.Clear();
            foreach (var model in models)
                LlmModelCombo.Items.Add(model);

            LlmModelCombo.Text = models.Contains(previousModel) ? previousModel
                : models.Count > 0 ? models[0] : previousModel;

            Log.Information("Fetched {Count} models from {BaseUrl}", models.Count, baseUrl);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch models");
            MessageBox.Show($"Failed to fetch models:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static async Task<List<string>> FetchModelsAsync(string baseUrl, string apiKey)
    {
        using var http = new System.Net.Http.HttpClient();
        var trimmedBase = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1" : baseUrl.TrimEnd('/');
        var url = $"{trimmedBase}/models";

        if (trimmedBase.Contains("anthropic.com", StringComparison.OrdinalIgnoreCase))
        {
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var m in data.EnumerateArray())
            {
                if (m.TryGetProperty("id", out var id))
                    models.Add(id.GetString()!);
            }
        }

        models.Sort(StringComparer.OrdinalIgnoreCase);
        return models;
    }

    // ── Shortcut Recorder ─────────────────────────────────────────────────

    private void ShortcutBox_GotFocus(object sender, RoutedEventArgs e)
    {
        var box = (System.Windows.Controls.TextBox)sender;
        _shortcutBoxPreviousText = box.Text;
        box.Text = "Press a key\u2026";
        box.Foreground = (SolidColorBrush)FindResource("YellowBrush");
        _keyboardHook.SuppressWinKey = true;
    }

    private void ShortcutBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var box = (System.Windows.Controls.TextBox)sender;
        if (box.Text == "Press a key\u2026")
            box.Text = _shortcutBoxPreviousText ?? "";
        box.Foreground = (SolidColorBrush)FindResource("TextBrush");
        _keyboardHook.SuppressWinKey = false;
    }

    private void ShortcutBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var box = (System.Windows.Controls.TextBox)sender;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool winHeld = _keyboardHook.IsWinDown;

        if (key is Key.LWin or Key.RWin)
            return;

        bool isModifierKey = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift;
        if (isModifierKey && !winHeld)
            return;

        var modifiers = Keyboard.Modifiers;
        if (winHeld)
            modifiers |= ModifierKeys.Windows;

        if (isModifierKey)
        {
            if (key is Key.LeftCtrl or Key.RightCtrl) modifiers &= ~ModifierKeys.Control;
            else if (key is Key.LeftAlt or Key.RightAlt) modifiers &= ~ModifierKeys.Alt;
            else if (key is Key.LeftShift or Key.RightShift) modifiers &= ~ModifierKeys.Shift;
        }

        var displayText = UiHelper.FormatShortcut(modifiers, key);

        if (key == Key.Escape)
        {
            box.Text = _shortcutBoxPreviousText ?? "";
            box.Foreground = (SolidColorBrush)FindResource("TextBrush");
            Keyboard.ClearFocus();
            return;
        }

        if (key == Key.Delete || key == Key.Back)
        {
            if (box == MuteShortcutBox)
            {
                _muteKey = Key.None;
                _muteModifiers = ModifierKeys.None;
                box.Text = "";
                box.Foreground = (SolidColorBrush)FindResource("TextBrush");
                Keyboard.ClearFocus();
                return;
            }
            if (box == LangSwitchShortcutBox)
            {
                _langSwitchKey = Key.None;
                _langSwitchModifiers = ModifierKeys.None;
                box.Text = "";
                box.Foreground = (SolidColorBrush)FindResource("TextBrush");
                Keyboard.ClearFocus();
                return;
            }
        }

        if (!AssignShortcut(box, displayText, modifiers, key))
            return;

        box.Text = displayText;
        box.Foreground = (SolidColorBrush)FindResource("TextBrush");
        Keyboard.ClearFocus();
    }

    private void OnRecordingWinPlusModifier(int heldVk)
    {
        var focused = Keyboard.FocusedElement as System.Windows.Controls.TextBox;
        if (focused is not (var box and not null) ||
            (box != ToggleShortcutBox && box != PttShortcutBox && box != MuteShortcutBox && box != LangSwitchShortcutBox))
            return;

        var key = KeyInterop.KeyFromVirtualKey(heldVk);
        var modifiers = ModifierKeys.Windows;
        var displayText = UiHelper.FormatShortcut(modifiers, key);

        if (!AssignShortcut(box, displayText, modifiers, key))
            return;

        box.Text = displayText;
        box.Foreground = (SolidColorBrush)FindResource("TextBrush");
        Keyboard.ClearFocus();
    }

    // ── Text Replacements ─────────────────────────────────────────────────

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

    private void OpenSettingsFileButton_Click(object sender, RoutedEventArgs e)
    {
        var path = SettingsService.DefaultJsonPath;
        if (!System.IO.File.Exists(path))
        {
            MessageBox.Show("Settings file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Process.Start("notepad.exe", path);
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (ThemeCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        ThemeManager.Apply((string)(item.Tag ?? "Dark"));
    }

    private void PromptListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var hasSelection = PromptListBox.SelectedItem != null;
        EditPromptBtn.IsEnabled = hasSelection;
        DeletePromptBtn.IsEnabled = hasSelection;
    }

    private void AddPrompt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PromptEditDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _prompts.Add(dialog.Result);
            PromptListBox.ItemsSource = null;
            PromptListBox.ItemsSource = _prompts;
        }
    }

    private void EditPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (PromptListBox.SelectedItem is not NamedPrompt selected) return;
        var index = _prompts.IndexOf(selected);
        var dialog = new PromptEditDialog(selected) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _prompts[index] = dialog.Result;
            PromptListBox.ItemsSource = null;
            PromptListBox.ItemsSource = _prompts;
        }
    }

    private void DeletePrompt_Click(object sender, RoutedEventArgs e)
    {
        if (PromptListBox.SelectedItem is not NamedPrompt selected) return;
        _prompts.Remove(selected);
        PromptListBox.ItemsSource = null;
        PromptListBox.ItemsSource = _prompts;
    }

    private bool AssignShortcut(System.Windows.Controls.TextBox box, string displayText, ModifierKeys modifiers, Key key)
    {
        var otherBoxes = new[] { ToggleShortcutBox, PttShortcutBox, MuteShortcutBox, LangSwitchShortcutBox }
            .Where(b => b != box);
        if (otherBoxes.Any(b => !string.IsNullOrEmpty(b.Text) && b.Text == displayText))
        {
            box.Text = "Already assigned!";
            box.Foreground = (SolidColorBrush)FindResource("RedBrush");
            return false;
        }

        if (box == ToggleShortcutBox) { _toggleKey = key; _toggleModifiers = modifiers; }
        else if (box == PttShortcutBox) { _pttKey = key; _pttModifiers = modifiers; }
        else if (box == MuteShortcutBox) { _muteKey = key; _muteModifiers = modifiers; }
        else if (box == LangSwitchShortcutBox) { _langSwitchKey = key; _langSwitchModifiers = modifiers; }
        return true;
    }

    private void InputDelayBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    // ── Settings Search ──────────────────────────────────────────────────

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void SearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrEmpty(SearchBox.Text);
        if (SearchPlaceholder != null)
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        if (SearchClearButton != null)
            SearchClearButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        if (!IsLoaded) return;

        var query = SearchBox.Text.Trim().ToLowerInvariant();
        FilterSettings(query);
    }

    private void FilterSettings(string query)
    {
        var mainGrid = (System.Windows.Controls.Grid)Content;
        var scrollViewer = mainGrid.Children.OfType<System.Windows.Controls.ScrollViewer>().First();
        var stackPanel = (System.Windows.Controls.StackPanel)scrollViewer.Content;

        bool isEmpty = string.IsNullOrEmpty(query);

        System.Windows.Controls.TextBlock? currentSectionHeader = null;
        bool anySectionChildVisible = false;

        foreach (UIElement child in stackPanel.Children)
        {
            if (child is System.Windows.Controls.TextBlock tb
                && tb.Tag is string tag && tag == "section")
            {
                if (currentSectionHeader != null)
                {
                    currentSectionHeader.Visibility = (isEmpty || anySectionChildVisible)
                        ? Visibility.Visible : Visibility.Collapsed;
                }

                currentSectionHeader = tb;
                anySectionChildVisible = false;
                continue;
            }

            if (isEmpty)
            {
                RestoreDefaultVisibility(child);
                anySectionChildVisible = true;
            }
            else
            {
                bool matches = MatchesSearch(child, query);
                child.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                if (matches) anySectionChildVisible = true;
            }
        }

        if (currentSectionHeader != null)
        {
            currentSectionHeader.Visibility = (isEmpty || anySectionChildVisible)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static bool MatchesSearch(UIElement element, string query)
    {
        if (element is FrameworkElement fe && fe.Tag is string tag)
            return tag.Contains(query, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void RestoreDefaultVisibility(UIElement child)
    {
        if (child is FrameworkElement fe)
        {
            if (fe == WhisperModelPanel || fe == ApiKeyPanel || fe == KeywordsPanel)
            {
                var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
                var isWhisper = (string)(providerItem?.Tag ?? "deepgram") == "whisper";
                if (fe == WhisperModelPanel)
                    fe.Visibility = isWhisper ? Visibility.Visible : Visibility.Collapsed;
                else
                    fe.Visibility = isWhisper ? Visibility.Collapsed : Visibility.Visible;
                return;
            }

            if (fe == LlmSettingsPanel)
            {
                fe.Visibility = LlmEnabledCheck.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
        }

        child.Visibility = Visibility.Visible;
    }

    // ── Autostart ─────────────────────────────────────────────────────────

    private const string AutostartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValueName = "PureType";

    private static void SetAutostart(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutostartRegistryKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                key.SetValue(AutostartValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AutostartValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Autostart registration failed");
        }
    }

    private static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutostartRegistryKey);
            return key?.GetValue(AutostartValueName) != null;
        }
        catch
        {
            return false;
        }
    }

}
