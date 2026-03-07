using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Serilog;
using VoiceDictation.Helpers;
using VoiceDictation.Services;

namespace VoiceDictation;

public partial class MainWindow : Window
{
    // ── Services ──────────────────────────────────────────────────────────
    private ITranscriptionProvider? _provider;
    private readonly AudioCaptureService _audio = new();
    private readonly LogWindow _logWindow = new();
    private static readonly LogWindowSink UiSink = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ToolStripLabel? _trayStatusLabel;
    private System.Windows.Forms.ToolStripMenuItem? _trayConnectItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayMuteItem;

    // ── Hotkeys ───────────────────────────────────────────────────────────
    private readonly KeyboardHookService _keyboardHook = new();
    private readonly ReplacementService _replacements = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "VoiceDictation", "replacements.txt"));


    // Shortcut settings (defaults)
    private Key _toggleKey = Key.X;
    private ModifierKeys _toggleModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    private Key _pttKey = Key.LeftCtrl;
    private ModifierKeys _pttModifiers = ModifierKeys.Windows;

    // ── State ────────────────────────────────────────────────────────────
    private bool _connected;
    private bool _recording;
    private enum RecordingSource { None, Toggle, Ptt }
    private RecordingSource _recordingSource = RecordingSource.None;
    private VadService? _vad;
    private bool _muted;
    private string _interimText = "";
    private bool _isLoading = true;
    private readonly List<string> _sessionChunks = new();
    private bool _aiPostProcessRequested;
    private string? _savedMicrophoneDevice;
    private string? _savedWhisperModel;

    // ── Autostart ────────────────────────────────────────────────────────
    private const string AutostartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValueName = "VoiceDictation";

    // Settings
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();

    // ── Colors ────────────────────────────────────────────────────────────
    private static readonly SolidColorBrush Red    = new(Color.FromRgb(0xF3, 0x8B, 0xA8));
    private static readonly SolidColorBrush Green  = new(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly SolidColorBrush Yellow = new(Color.FromRgb(0xF9, 0xE2, 0xAF));
    private static readonly SolidColorBrush Blue   = new(Color.FromRgb(0x89, 0xB4, 0xFA));

    // ── Init ──────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceDictation", "log.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(UiSink)
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        UiSink.SetCallback(_logWindow.AppendLog);

        Log.Information("VoiceDictation started");

        SetupTrayIcon();
        LoadSettings();
        // Init sound after LoadSettings so selected tone is applied
        var toneItem = ToneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        SoundFeedback.Init((string?)toneItem?.Tag);

        _audio.AudioDataAvailable += OnAudioData;
        _audio.AudioLevelChanged += OnAudioLevel;
        PopulateMicrophones();
        PopulateWhisperModels();
        if (_savedWhisperModel != null)
            SelectComboByTag(WhisperModelCombo, _savedWhisperModel);

        var provItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var isWhisper = (string?)provItem?.Tag == "whisper";
        WhisperModelPanel.Visibility = isWhisper ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = isWhisper ? Visibility.Collapsed : Visibility.Visible;
        KeywordsPanel.Visibility = isWhisper ? Visibility.Collapsed : Visibility.Visible;

        // Enable settings persistence only after all controls are populated
        _isLoading = false;

        Loaded += (_, _) => ConnectButton.Focus();

        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        ApplyAiTriggerKey();
        _keyboardHook.TogglePressed += OnToggleHotkey;
        _keyboardHook.PttKeyDown += OnPttKeyDown;
        _keyboardHook.PttKeyUp += OnPttKeyUp;
        _keyboardHook.RecordingWinPlusModifier += OnRecordingWinPlusModifier;

        // Auto-connect on startup (independent of window visibility)
        var autoProvider = (ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        if (autoProvider == "whisper" || !string.IsNullOrWhiteSpace(ApiKeyBox.Password))
            Dispatcher.BeginInvoke(async () => await ConnectAsync());

        // Show window if "Start minimized" is not checked
        if (StartMinimizedCheck.IsChecked != true)
            Dispatcher.BeginInvoke(() => ShowFromTray());
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();

        ApiKeyBox.Password = _settings.Transcription.ApiKey;

        ParseToggleShortcut(_settings.Shortcuts.Toggle);
        ParsePttShortcut(_settings.Shortcuts.Ptt);
        SelectComboByTag(AiTriggerKeyCombo, _settings.Shortcuts.AiTriggerKey);

        SelectComboByTag(LanguageCombo, _settings.Transcription.Language);
        SelectComboByTag(ProviderCombo, _settings.Transcription.Provider);
        _savedWhisperModel = _settings.Transcription.WhisperModel;
        KeywordsBox.Text = _settings.Transcription.Keywords;

        _savedMicrophoneDevice = _settings.Audio.Microphone;
        SelectMicrophoneByName(_settings.Audio.Microphone);
        SelectComboByTag(ToneCombo, _settings.Audio.Tone);
        VadCheck.IsChecked = _settings.Audio.Vad;

        LlmEnabledCheck.IsChecked = _settings.Llm.Enabled;
        LlmSettingsPanel.Visibility = _settings.Llm.Enabled ? Visibility.Visible : Visibility.Collapsed;
        LlmApiKeyBox.Password = _settings.Llm.ApiKey;
        LlmBaseUrlCombo.Text = _settings.Llm.BaseUrl;
        LlmModelCombo.Text = _settings.Llm.Model;
        LlmPromptBox.Text = _settings.Llm.Prompt;

        bool hasPosition = false;
        if (_settings.Window.Left.HasValue) { Left = _settings.Window.Left.Value; hasPosition = true; }
        if (_settings.Window.Top.HasValue) { Top = _settings.Window.Top.Value; hasPosition = true; }
        if (_settings.Window.Width.HasValue && _settings.Window.Width.Value >= MinWidth) Width = _settings.Window.Width.Value;
        if (_settings.Window.Height.HasValue && _settings.Window.Height.Value >= MinHeight) Height = _settings.Window.Height.Value;
        StartMinimizedCheck.IsChecked = _settings.Window.StartMinimized;

        ToggleShortcutBox.Text = FormatShortcut(_toggleModifiers, _toggleKey);
        PttShortcutBox.Text = FormatShortcut(_pttModifiers, _pttKey);
        AutostartCheck.IsChecked = IsAutostartEnabled();

        if (!hasPosition)
            CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2 + screen.Left;
        Top = (screen.Height - Height) / 2 + screen.Top;
    }

    private void ParseToggleShortcut(string value)
    {
        (_toggleModifiers, _toggleKey) = ParseShortcut(value, _toggleKey);
    }

    private void ParsePttShortcut(string value)
    {
        (_pttModifiers, _pttKey) = ParseShortcut(value, _pttKey);
    }

    private static (ModifierKeys mods, Key key) ParseShortcut(string value, Key defaultKey)
    {
        var mods = ModifierKeys.None;
        var key = defaultKey;
        var parts = value.Split('+');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Windows;
            else if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Control;
            else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Alt;
            else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Shift;
            else
            {
                var mappedKey = trimmed switch
                {
                    "L-Ctrl" => Key.LeftCtrl,
                    "R-Ctrl" => Key.RightCtrl,
                    "L-Alt" => Key.LeftAlt,
                    "R-Alt" => Key.RightAlt,
                    "L-Shift" => Key.LeftShift,
                    "R-Shift" => Key.RightShift,
                    _ => Enum.TryParse<Key>(trimmed, out var k) ? k : (Key?)null
                };
                if (mappedKey.HasValue)
                    key = mappedKey.Value;
            }
        }
        return (mods, key);
    }

    private void SaveSettings()
    {
        var langItem = LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var micItem = MicrophoneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var whisperModelItem = WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;

        _settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                ApiKey = ApiKeyBox.Password.Trim(),
                Language = (string)(langItem?.Tag ?? "de"),
                Provider = (string)(providerItem?.Tag ?? "deepgram"),
                WhisperModel = (string)(whisperModelItem?.Tag ?? "tiny"),
                Keywords = KeywordsBox.Text.Trim(),
            },
            Shortcuts = new ShortcutSettings
            {
                Toggle = FormatShortcut(_toggleModifiers, _toggleKey),
                Ptt = FormatShortcut(_pttModifiers, _pttKey),
                AiTriggerKey = (string)((AiTriggerKeyCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "shift"),
            },
            Audio = new AudioSettings
            {
                Microphone = micItem?.Content?.ToString() ?? "",
                Tone = (string)((ToneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "Gentle"),
                Vad = VadCheck.IsChecked == true,
            },
            Llm = new LlmSettings
            {
                Enabled = LlmEnabledCheck.IsChecked == true,
                ApiKey = LlmApiKeyBox.Password.Trim(),
                BaseUrl = LlmBaseUrlCombo.Text.Trim(),
                Model = LlmModelCombo.Text.Trim(),
                Prompt = LlmPromptBox.Text.Trim(),
            },
            Window = new WindowSettings
            {
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height,
                StartMinimized = StartMinimizedCheck.IsChecked == true,
            },
        };

        _settingsService.Save(_settings);
    }

    private static bool SelectComboByTag(System.Windows.Controls.ComboBox combo, string tag)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in combo.Items)
        {
            if ((string)item.Tag == tag)
            {
                combo.SelectedItem = item;
                return true;
            }
        }
        return false;
    }

    private static string FormatShortcut(ModifierKeys mod, Key key)
    {
        var parts = new List<string>();
        if (mod.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (mod.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mod.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mod.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        // Friendly names for modifier keys used as main key
        var keyName = key switch
        {
            Key.LeftCtrl => "L-Ctrl",
            Key.RightCtrl => "R-Ctrl",
            Key.LeftAlt => "L-Alt",
            Key.RightAlt => "R-Alt",
            Key.LeftShift => "L-Shift",
            Key.RightShift => "R-Shift",
            _ => key.ToString()
        };
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    // ── UI Events ─────────────────────────────────────────────────────────

    private void LogButton_Click(object sender, RoutedEventArgs e)
    {
        _logWindow.Show();
        _logWindow.Activate();
    }

    private bool _transcriptCollapsed;

    private void TranscriptHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _transcriptCollapsed = !_transcriptCollapsed;
        TranscriptScroll.Visibility = _transcriptCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TranscriptArrow.Text = _transcriptCollapsed ? "▸" : "▾";

        // When collapsed, transcript row becomes Auto (just header); settings get all space
        var grid = (System.Windows.Controls.Grid)TranscriptBorder.Parent;
        var rowDef = grid.RowDefinitions[4]; // Row 4 = Transcript
        rowDef.Height = _transcriptCollapsed
            ? GridLength.Auto
            : new GridLength(3, GridUnitType.Star);
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            SaveSettings();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_connected)
            await ConnectAsync();
        else
            await DisconnectAsync();
    }

    private void LanguageCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_isLoading)
            SaveSettings();
    }


    private void ToneCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var item = (System.Windows.Controls.ComboBoxItem)ToneCombo.SelectedItem;
        var preset = (string)item.Tag;
        SoundFeedback.Init(preset);
        SoundFeedback.PlayStart(); // preview
        SaveSettings();
    }

    // ── Microphone ────────────────────────────────────────────────────────

    private void PopulateMicrophones()
    {
        MicrophoneCombo.Items.Clear();
        var devices = AudioCaptureService.GetDevices();
        foreach (var (number, name) in devices)
        {
            var item = new System.Windows.Controls.ComboBoxItem
            {
                Content = name,
                Tag = number
            };
            MicrophoneCombo.Items.Add(item);
        }
        if (MicrophoneCombo.Items.Count > 0)
            MicrophoneCombo.SelectedIndex = 0;
    }

    private void SelectMicrophoneByName(string name)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in MicrophoneCombo.Items)
        {
            if (item.Content?.ToString() == name)
            {
                MicrophoneCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void MicrophoneCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MicrophoneCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is int deviceNumber)
        {
            _audio.SetDevice(deviceNumber);
            if (!_isLoading)
                SaveSettings();
        }
    }

    // ── VU Meter ─────────────────────────────────────────────────────────

    private void OnAudioLevel(double level)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var parent = (System.Windows.Controls.Border)VuMeterBar.Parent;
            VuMeterBar.Width = level * parent.ActualWidth;
        });
    }

    // ── Keywords ─────────────────────────────────────────────────────────

    private void KeywordsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            SaveSettings();
    }

    // ── Provider / Whisper Model ────────────────────────────────────────

    private void PopulateWhisperModels()
    {
        // Remember current selection
        var previousTag = (WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;

        WhisperModelCombo.Items.Clear();
        foreach (var (name, displayName, _) in WhisperModelManager.AvailableModels)
        {
            var isDownloaded = WhisperModelManager.IsModelDownloaded(name);
            var suffix = isDownloaded ? " \u2713" : "";
            var item = new System.Windows.Controls.ComboBoxItem
            {
                Content = displayName + suffix,
                Tag = name,
                FontWeight = isDownloaded ? FontWeights.SemiBold : FontWeights.Normal
            };
            WhisperModelCombo.Items.Add(item);
        }

        // Restore previous selection, or fall back to first downloaded, or first item
        if (previousTag != null && SelectComboByTag(WhisperModelCombo, previousTag)) { }
        else
        {
            // Select first downloaded model
            foreach (System.Windows.Controls.ComboBoxItem item in WhisperModelCombo.Items)
            {
                if (WhisperModelManager.IsModelDownloaded((string)item.Tag))
                {
                    WhisperModelCombo.SelectedItem = item;
                    break;
                }
            }
            if (WhisperModelCombo.SelectedItem == null && WhisperModelCombo.Items.Count > 0)
                WhisperModelCombo.SelectedIndex = 0;
        }
    }

    private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading || ProviderCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var isWhisper = (string)item.Tag == "whisper";
        WhisperModelPanel.Visibility = isWhisper ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = isWhisper ? Visibility.Collapsed : Visibility.Visible;
        KeywordsPanel.Visibility = isWhisper ? Visibility.Collapsed : Visibility.Visible;
        if (!_isLoading)
            SaveSettings();
    }

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

            PopulateWhisperModels();
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

    // ── Autostart ────────────────────────────────────────────────────────

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

    private void VadCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            SaveSettings();
    }

    private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
        {
            SetAutostart(AutostartCheck.IsChecked == true);
            SaveSettings();
        }
    }

    private void StartMinimizedCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            SaveSettings();
    }

    private void LlmEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        LlmSettingsPanel.Visibility = LlmEnabledCheck.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        SaveSettings();
    }

    private void LlmBaseUrlCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        SaveSettings();
        // Auto-fetch models when endpoint changes (only if API key is set)
        if (!string.IsNullOrWhiteSpace(LlmApiKeyBox.Password))
            Dispatcher.BeginInvoke(() => FetchModelsButton_Click(sender, new RoutedEventArgs()));
    }

    private void LlmApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoading) SaveSettings();
    }

    private void LlmSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoading) SaveSettings();
    }

    private void AiTriggerKeyCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        ApplyAiTriggerKey();
        SaveSettings();
    }

    private void ApplyAiTriggerKey()
    {
        var item = AiTriggerKeyCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var tag = (string)(item?.Tag ?? "shift");
        var (vk1, vk2) = tag switch
        {
            "shift" => (KeyboardHookService.VK_LSHIFT, KeyboardHookService.VK_RSHIFT),
            "ctrl"  => (KeyboardHookService.VK_LCONTROL, KeyboardHookService.VK_RCONTROL),
            "alt"   => (KeyboardHookService.VK_LMENU, KeyboardHookService.VK_RMENU),
            "caps"  => (KeyboardHookService.VK_CAPITAL, 0),
            _       => (KeyboardHookService.VK_LSHIFT, KeyboardHookService.VK_RSHIFT)
        };
        _keyboardHook.SetAiTriggerKey(vk1, vk2);
    }

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

            // Restore previous selection or pick first
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

    private static async Task<List<string>> FetchModelsAsync(string baseUrl, string apiKey)
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
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                    models.Add(id.GetString()!);
            }
        }

        models.Sort(StringComparer.OrdinalIgnoreCase);
        return models;
    }

    private static void SetAutostart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey, writable: true);
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
            using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey);
            return key?.GetValue(AutostartValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    // ── Shortcut Recorder ────────────────────────────────────────────────

    private string? _shortcutBoxPreviousText;

    private void ShortcutBox_GotFocus(object sender, RoutedEventArgs e)
    {
        var box = (System.Windows.Controls.TextBox)sender;
        _shortcutBoxPreviousText = box.Text;
        box.Text = "Press a key…";
        box.Foreground = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
        _keyboardHook.SuppressWinKey = true;
    }

    private void ShortcutBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var box = (System.Windows.Controls.TextBox)sender;
        if (box.Text == "Press a key…")
            box.Text = _shortcutBoxPreviousText ?? "";
        box.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
        _keyboardHook.SuppressWinKey = false;
    }

    private void ShortcutBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var box = (System.Windows.Controls.TextBox)sender;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool winHeld = _keyboardHook.IsWinDown;

        // Win key itself is always ignored (tracked by interceptor)
        if (key is Key.LWin or Key.RWin)
            return;

        // Other modifier keys: allow as "main key" when Win is held, otherwise wait for a real key
        bool isModifierKey = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift;
        if (isModifierKey && !winHeld)
            return;

        var modifiers = Keyboard.Modifiers;
        if (winHeld)
            modifiers |= ModifierKeys.Windows;

        // When a modifier key is used as the main key, remove it from modifiers to avoid duplication
        // e.g. Win+Ctrl: modifiers should be Windows only, key is LeftCtrl
        if (isModifierKey)
        {
            if (key is Key.LeftCtrl or Key.RightCtrl) modifiers &= ~ModifierKeys.Control;
            else if (key is Key.LeftAlt or Key.RightAlt) modifiers &= ~ModifierKeys.Alt;
            else if (key is Key.LeftShift or Key.RightShift) modifiers &= ~ModifierKeys.Shift;
        }

        var displayText = FormatShortcut(modifiers, key);

        if (key == Key.Escape)
        {
            box.Text = _shortcutBoxPreviousText ?? "";
            box.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
            Keyboard.ClearFocus();
            return;
        }

        bool isToggleBox = box == ToggleShortcutBox;

        var otherBox = isToggleBox ? PttShortcutBox : ToggleShortcutBox;
        if (otherBox.Text == displayText)
        {
            box.Text = "Already assigned!";
            box.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            return;
        }

        if (isToggleBox)
        {
            _toggleKey = key;
            _toggleModifiers = modifiers;

            if (_connected)
                _keyboardHook.SetToggleShortcut(modifiers, key);
        }
        else
        {
            _pttKey = key;
            _pttModifiers = modifiers;

            if (_connected)
                _keyboardHook.SetPttShortcut(modifiers, key);
        }

        box.Text = displayText;
        box.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
        SaveSettings();
        Keyboard.ClearFocus();
    }


    private void OnRecordingWinPlusModifier(int heldVk)
    {
        // Find the focused shortcut TextBox (if any)
        var focused = Keyboard.FocusedElement as System.Windows.Controls.TextBox;
        if (focused is not (var box and not null) || (box != ToggleShortcutBox && box != PttShortcutBox))
            return;

        var key = KeyInterop.KeyFromVirtualKey(heldVk);
        var modifiers = ModifierKeys.Windows;

        var displayText = FormatShortcut(modifiers, key);

        bool isToggleBox = box == ToggleShortcutBox;
        var otherBox = isToggleBox ? PttShortcutBox : ToggleShortcutBox;
        if (otherBox.Text == displayText)
        {
            box.Text = "Already assigned!";
            box.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            return;
        }

        if (isToggleBox)
        {
            _toggleKey = key;
            _toggleModifiers = modifiers;
            if (_connected)
                _keyboardHook.SetToggleShortcut(modifiers, key);
        }
        else
        {
            _pttKey = key;
            _pttModifiers = modifiers;
            if (_connected)
                _keyboardHook.SetPttShortcut(modifiers, key);
        }

        box.Text = displayText;
        box.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
        SaveSettings();
        Keyboard.ClearFocus();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        Log.Information("Application shutting down");
        _trayIcon?.Dispose();
        _ = DisconnectAsync();
        _keyboardHook.Dispose();
        _audio.Dispose();
        _replacements.Dispose();
        Application.Current.Shutdown();
        Log.CloseAndFlush();
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────

    private async Task ConnectAsync()
    {
        var providerItem = (System.Windows.Controls.ComboBoxItem)ProviderCombo.SelectedItem;
        var providerType = (string)providerItem.Tag;

        SetStatus("Connecting …", Yellow);
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
                    MessageBox.Show("Please download the Whisper model first.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectButton.IsEnabled = true;
                    SetStatus("Not connected", Red);
                    return;
                }

                _provider = new WhisperService(modelName, language);
            }
            else
            {
                var apiKey = ApiKeyBox.Password.Trim();
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("Please enter a Deepgram API key.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectButton.IsEnabled = true;
                    SetStatus("Not connected", Red);
                    return;
                }
                var keywords = KeywordsBox.Text
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToArray();
                _provider = new DeepgramService(apiKey, language, keywords.Length > 0 ? keywords : null);
            }

            _provider.TranscriptReceived += OnTranscriptReceived;
            _provider.ErrorOccurred += OnError;
            _provider.Disconnected += OnDisconnected;

            await _provider.ConnectAsync();

            _connected = true;
            _audio.Initialize();
            RegisterHotkeys();

            var label = providerType == "whisper" ? "Whisper (local)" : "Deepgram";
            SetStatus($"Connected - {label}", Green);
            ConnectButton.Content = "Disconnect";
            ConnectButton.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            SaveSettings();
            Log.Information("{Provider} connected (Language: {Language})", label, language);
            UpdateTrayMenu();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Connection failed");
            SetStatus("Connection failed", Red);
            MessageBox.Show($"Error:\n{ex.Message}", "Connection Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async Task DisconnectAsync()
    {
        if (!_connected) return;
        _connected = false;

        StopRecording();
        _keyboardHook.Uninstall();

        if (_provider is not null)
        {
            await _provider.DisposeAsync();
            _provider = null;
        }
        SetStatus("Not connected", Red);
        ConnectButton.Content    = "Connect";
        ConnectButton.Background = Blue;
        Log.Information("Provider disconnected");
        UpdateTrayMenu();
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        _keyboardHook.Install();
    }

    // ── Toggle Mode ───────────────────────────────────────────────────────

    private void OnToggleHotkey(bool aiKeyHeld)
    {
        Dispatcher.Invoke(() =>
        {
            if (_recording)
            {
                if (_recordingSource != RecordingSource.Toggle) return;
                // Also check AI key at stop time
                if (!_aiPostProcessRequested && LlmEnabledCheck.IsChecked == true)
                    _aiPostProcessRequested = aiKeyHeld;
                _recordingSource = RecordingSource.None;
                StopRecording();
            }
            else
            {
                _aiPostProcessRequested = aiKeyHeld && LlmEnabledCheck.IsChecked == true;
                _recordingSource = RecordingSource.Toggle;
                StartRecording();
            }
        });
    }

    // ── Push-to-Talk ──────────────────────────────────────────────────────

    private void OnPttKeyDown(bool aiKeyHeld)
    {
        if (!_connected || _recording) return;
        Dispatcher.Invoke(() =>
        {
            _aiPostProcessRequested = aiKeyHeld && LlmEnabledCheck.IsChecked == true;
            _recordingSource = RecordingSource.Ptt;
            StartRecording();
        });
    }

    private void OnPttKeyUp()
    {
        if (_recordingSource != RecordingSource.Ptt) return;
        Dispatcher.Invoke(() =>
        {
            // Also check AI key at stop time (user may press it after PTT started)
            if (!_aiPostProcessRequested && LlmEnabledCheck.IsChecked == true)
                _aiPostProcessRequested = _keyboardHook.IsAiKeyHeld();
            _recordingSource = RecordingSource.None;
            StopRecording();
        });
    }

    // ── Start/Stop Recording ───────────────────────────────────────────────

    private void StartRecording()
    {
        if (_recording || !_connected) return;
        _sessionChunks.Clear();
        _recording = true;
        SoundFeedback.PlayStart();
        _audio.Start();
        var aiLabel = _aiPostProcessRequested ? " + AI" : "";
        SetStatus($"● Recording{aiLabel}", Red);
        ToastWindow.ShowToast(_aiPostProcessRequested ? "Recording + AI" : "Recording",
            Red.Color, autoClose: false);

        // Add separator between recording sessions in transcript
        var current = TranscriptText.Text;
        if (!string.IsNullOrEmpty(current) && current != "Transcript will appear here …")
            TranscriptText.Text = current + "\n────────────────────\n";

        if (VadCheck.IsChecked == true && _recordingSource == RecordingSource.Toggle)
        {
            _vad = new VadService();
            _vad.SilenceDetected += () => Dispatcher.Invoke(StopRecording);
            _vad.Reset();
        }
        UpdateTrayMenu();
    }

    private async void StopRecording()
    {
        if (!_recording) return;
        _audio.Stop();
        _recording = false;
        _vad = null;

        _interimText = "";
        InterimText.Text = "";
        VuMeterBar.Width = 0;

        // Flush provider buffer so the last transcript arrives immediately
        if (_provider is not null)
            await _provider.SendFinalizeAsync();

        // Yield to Dispatcher so pending TranscriptReceived callbacks populate _sessionChunks
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

        SoundFeedback.PlayStop();
        if (!_aiPostProcessRequested)
            ToastWindow.ShowToast("Recording stopped", false);

        if (_aiPostProcessRequested && _sessionChunks.Count > 0)
        {
            _aiPostProcessRequested = false;
            var fullText = string.Join("", _sessionChunks);
            _ = ProcessWithLlmAsync(fullText);
        }

        if (_connected)
            SetStatus("Connected – ready", Green);
        UpdateTrayMenu();
    }

    // ── Audio → Provider ───────────────────────────────────────────────────

    private async void OnAudioData(byte[] chunk)
    {
        if (_provider is null || !_recording) return;
        if (!_muted)
            await _provider.SendAudioAsync(chunk);
        _vad?.ProcessAudio(chunk);
    }

    // ── Transcript Received ────────────────────────────────────────────────

    private void OnTranscriptReceived(string text, bool isFinal)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (isFinal)
            {
                _interimText = "";
                InterimText.Text = "";
                var processed = _replacements.Apply(text);
                AppendTranscript(processed);
                _sessionChunks.Add(processed);

                // When AI post-processing is pending, don't type yet — LLM will type the result
                if (!_aiPostProcessRequested)
                {
                    try
                    {
                        await KeyboardInjector.TypeTextAsync(processed);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Text injection error");
                    }
                }
            }
            else
            {
                _interimText = text;
                InterimText.Text = text;
                TranscriptScroll.ScrollToBottom();
            }
        });
    }

    private void AppendTranscript(string text)
    {
        var current = TranscriptText.Text;
        if (current == "Transcript will appear here …")
            current = "";

        // Keep last ~500 characters
        var newText = (current + text).TrimStart();
        if (newText.Length > 500)
            newText = newText[^500..];

        TranscriptText.Text = newText;
        TranscriptScroll.ScrollToBottom();
    }

    // ── Error / Disconnect ─────────────────────────────────────────────────

    private void OnError(string message) =>
        Dispatcher.Invoke(() => SetStatus($"Error: {message}", Red));

    private void OnDisconnected() =>
        Dispatcher.Invoke(async () =>
        {
            if (_connected) // unexpectedly disconnected
                await DisconnectAsync();
        });

    // ── System Tray ──────────────────────────────────────────────────────

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

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                ShowFromTray();
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();

        // Status label (non-clickable)
        _trayStatusLabel = new System.Windows.Forms.ToolStripLabel("Not connected")
        {
            ForeColor = System.Drawing.Color.FromArgb(0xF3, 0x8B, 0xA8)
        };
        menu.Items.Add(_trayStatusLabel);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        // Connect / Disconnect toggle
        _trayConnectItem = new System.Windows.Forms.ToolStripMenuItem("Connect", null, async (_, _) =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (_connected)
                    await DisconnectAsync();
                else
                    await ConnectAsync();
            });
        });
        menu.Items.Add(_trayConnectItem);

        // Mute toggle
        _trayMuteItem = new System.Windows.Forms.ToolStripMenuItem("Mute", null, (_, _) =>
        {
            Dispatcher.Invoke(ToggleMute);
        })
        {
            CheckOnClick = false
        };
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

    private void ToggleMute()
    {
        _muted = !_muted;
        if (_trayMuteItem != null)
            _trayMuteItem.Checked = _muted;

        if (_muted)
            SetStatus("Muted", Yellow);
        else if (_recording)
            SetStatus("● Recording", Red);
        else if (_connected)
            SetStatus("Connected – ready", Green);
        else
            SetStatus("Not connected", Red);

        Log.Information("Mute {State}", _muted ? "enabled" : "disabled");
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        if (_trayStatusLabel == null || _trayConnectItem == null) return;

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
            _trayStatusLabel.ForeColor = System.Drawing.Color.FromArgb(0xF3, 0x8B, 0xA8);
        }

        _trayConnectItem.Text = _connected ? "Disconnect" : "Connect";
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        ConnectButton.Focus();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Visibility = Visibility.Hidden;
            ShowInTaskbar = false;
        }
    }

    // ── LLM Post-Processing ────────────────────────────────────────────────

    private async Task ProcessWithLlmAsync(string text)
    {
        try
        {
            var baseUrl = LlmBaseUrlCombo.Text.Trim();
            var apiKey = LlmApiKeyBox.Password.Trim();
            var model = LlmModelCombo.Text.Trim();
            var prompt = LlmPromptBox.Text.Trim();

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
            {
                Log.Warning("LLM post-processing skipped: missing API key or model");
                return;
            }

            bool isAnthropic = baseUrl.Contains("anthropic.com", StringComparison.OrdinalIgnoreCase);
            ILlmClient client = isAnthropic
                ? new AnthropicLlmClient(apiKey, model)
                : new OpenAiLlmClient(apiKey, baseUrl, model);

            Log.Information("Sending {Length} chars to LLM ({BaseUrl}/{Model})", text.Length, baseUrl, model);
            ToastWindow.ShowToast("Waiting for AI response …",
                Yellow.Color, autoClose: false);

            var result = await client.ProcessAsync(prompt, text);
            var processed = _replacements.Apply(result);

            Log.Information("LLM result: {Length} chars", processed.Length);
            ToastWindow.ShowToast("AI processing complete", false);

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
        catch (Exception ex)
        {
            Log.Error(ex, "LLM post-processing failed");
            ToastWindow.ShowToast("AI processing failed", true);
        }
    }

    // ── Helper Functions ──────────────────────────────────────────────────

    private void SetStatus(string text, SolidColorBrush color)
    {
        StatusText.Text = text;
        StatusDot.Fill  = color;
    }

}
