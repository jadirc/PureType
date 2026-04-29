using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using PureType.Helpers;
using PureType.Services;

namespace PureType;

public partial class MainWindow : Window
{
    // ── Services ──────────────────────────────────────────────────────────
    private ITranscriptionProvider? _provider;
    private readonly AudioCaptureService _audio = new();
    private readonly LogWindow _logWindow = new();
    private static readonly LogWindowSink UiSink = new();
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private readonly TrayIconManager _tray;
    private readonly RecordingController _controller;
    private StatusOverlayWindow? _overlay;

    // ── Hotkeys ───────────────────────────────────────────────────────────
    private readonly KeyboardHookService _keyboardHook = new();
    private readonly ReplacementService _replacements = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "PureType", "replacements.json"));

    // Shortcut settings (defaults)
    private Key _toggleKey = Key.X;
    private ModifierKeys _toggleModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    private Key _pttKey = Key.LeftCtrl;
    private ModifierKeys _pttModifiers = ModifierKeys.Windows;
    private Key _muteKey = Key.None;
    private ModifierKeys _muteModifiers = ModifierKeys.None;
    private Key _langSwitchKey = Key.None;
    private ModifierKeys _langSwitchModifiers = ModifierKeys.None;
    private Key _clipboardAiKey = Key.None;
    private ModifierKeys _clipboardAiModifiers = ModifierKeys.None;

    // ── State ────────────────────────────────────────────────────────────
    private bool _connected;
    private bool _muted;
    private bool _isLoading = true;

    // Settings
    private readonly SettingsService _settingsService = new();
    private readonly StatsService _stats = new();
    private AppSettings _settings = new();

    // ── Colors (resolved from theme resources) ────────────────────────────
    private SolidColorBrush Red    => (SolidColorBrush)FindResource("RedBrush");
    private SolidColorBrush Green  => (SolidColorBrush)FindResource("GreenBrush");
    private SolidColorBrush Yellow => (SolidColorBrush)FindResource("YellowBrush");
    private SolidColorBrush Blue   => (SolidColorBrush)FindResource("AccentBrush");

    // ── Init ──────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PureType", "log.txt");

        var iconStream = Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/mic.ico"))?.Stream;
        var baseIcon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Application;

        _tray = new TrayIconManager(baseIcon);
        _tray.ConnectRequested += () => Dispatcher.InvokeAsync(async () => await ConnectAsync());
        _tray.DisconnectRequested += () => Dispatcher.InvokeAsync(async () => await DisconnectAsync());
        _tray.MuteToggleRequested += () => Dispatcher.Invoke(ToggleMute);
        _tray.SettingsRequested += () => Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()));
        _tray.ExportRequested += () => Dispatcher.Invoke(ExportTranscript);
        _tray.HistoryRequested += () => Dispatcher.Invoke(ShowTranscriptHistory);
        _tray.StatsRequested += () => Dispatcher.Invoke(ShowStats);
        _tray.AboutRequested += () => Dispatcher.BeginInvoke(ShowAbout);
        _tray.ShowRequested += () => Dispatcher.Invoke(ShowFromTray);
        _tray.ExitRequested += () =>
        {
            _audio.StopDevicePolling();
            _tray.Dispose();
            _keyboardHook.Dispose();
            Application.Current.Shutdown();
        };

        var isFirstRun = _settingsService.IsFirstRun;
        LoadSettings();

        LevelSwitch.MinimumLevel = Enum.TryParse<LogEventLevel>(_settings.Window.LogLevel, out var lvl)
            ? lvl : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .WriteTo.Sink(UiSink)
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        UiSink.SetCallback(_logWindow.AppendLog);

        Log.Information("PureType started");

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
                        WhisperModel = welcome.SelectedWhisperModel,
                    }
                };
                _settingsService.Save(_settings);
                UiHelper.SelectComboByTag(ProviderCombo, welcome.SelectedProvider);
            }
        }

        SoundFeedback.Init(_settings.Audio.Tone);
        KeyboardInjector.InputDelayMs = _settings.Audio.InputDelayMs;

        _controller = new RecordingController(_audio, _replacements);
        _controller.StatusChanged += (text, color) => Dispatcher.Invoke(() =>
        {
            SetStatus(text, new SolidColorBrush(color));
            _overlay?.UpdateState(_controller.IsRecording, _muted, text, color);
        });
        _controller.TranscriptUpdated += text => Dispatcher.Invoke(() =>
        {
            if (text == "\0separator")
            {
                var current = TranscriptText.Text;
                if (!string.IsNullOrEmpty(current) && current != "Transcript will appear here \u2026")
                    TranscriptText.Text = current + "\n\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\n";
                return;
            }
            AppendTranscript(text);
        });
        _controller.InterimTextUpdated += text => Dispatcher.Invoke(() =>
        {
            InterimText.Text = text;
            if (!string.IsNullOrEmpty(text)) TranscriptScroll.ScrollToBottom();
        });
        _controller.RecordingStateChanged += () => Dispatcher.Invoke(() =>
        {
            _tray.Update(_connected, _controller.IsRecording, _muted);
            _keyboardHook.SetPromptKeyDetection(_controller.IsRecording);
            UpdateOverlay();
        });
        _controller.AudioLevelChanged += level => Dispatcher.BeginInvoke(() =>
        {
            var parent = (System.Windows.Controls.Border)VuMeterBar.Parent;
            VuMeterBar.Width = level * parent.ActualWidth;
        });
        _controller.RecordingStopped += () => Dispatcher.Invoke(() => VuMeterBar.Width = 0);
        _controller.LlmProcessingRequested += (text, prompt) => Dispatcher.Invoke(() => _ = ProcessWithLlmAsync(text, prompt));
        _controller.AutoCorrectionRequested += text => Dispatcher.Invoke(() => _ = ProcessAutoCorrectAsync(text));
        _controller.ClipboardRequested += text => Dispatcher.Invoke(() =>
        {
            System.Windows.Clipboard.SetText(text);
        });
        _controller.ToastRequested += (message, color, autoClose) =>
            ToastWindow.ShowToast(message, color, autoClose);
        _controller.SetStatsService(_stats);
        _controller.StatsUpdated += () => Dispatcher.Invoke(UpdateStatsLine);

        _audio.DevicesChanged += devices => Dispatcher.Invoke(() =>
        {
            Log.Information("Audio devices changed, {Count} devices found", devices.Count);
            PopulateMicrophones();
            ToastWindow.ShowToast("Microphone list updated", Colors.Blue, true);
        });

        _audio.DeviceError += msg => Dispatcher.Invoke(() =>
        {
            Log.Warning("Audio device error: {Error}", msg);
            ToastWindow.ShowToast("Microphone disconnected", Colors.Red, true);
        });

        _audio.ZeroAudioDetected += () => Dispatcher.Invoke(() =>
        {
            Log.Warning("Microphone delivering zero audio data — reinitializing device");
            ToastWindow.ShowToast("Microphone silent — reinitializing\u2026", Colors.Orange, true);
        });

        PopulateMicrophones();
        _audio.StartDevicePolling();
        SelectMicrophoneByName(_settings.Audio.Microphone);
        UiHelper.SelectComboByTag(InputModeComboMain, _settings.Audio.InputMode);
        UiHelper.SelectComboByTag(LanguageComboMain, _settings.Transcription.Language);
        AutoCorrectionCheckMain.IsChecked = _settings.AutoCorrection.Enabled;

        // Enable settings persistence only after all controls are populated
        _isLoading = false;
        UpdateStatsLine();

        Loaded += (_, _) => ConnectButton.Focus();

        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        ApplyPromptKeys();
        _keyboardHook.TogglePressed += () => Dispatcher.Invoke(() => _controller.HandleToggle());
        _keyboardHook.PttKeyDown += () => Dispatcher.Invoke(() => _controller.HandlePttDown());
        _keyboardHook.PromptKeyPressed += vkCode => Dispatcher.Invoke(() => _controller.HandlePromptKeyPressed(vkCode));
        _keyboardHook.PttKeyUp += () => Dispatcher.Invoke(() => _controller.HandlePttUp());
        _keyboardHook.MutePressed += () => Dispatcher.Invoke(ToggleMute);
        _keyboardHook.LanguageSwitchPressed += () => Dispatcher.Invoke(CycleLanguage);
        _keyboardHook.ClipboardAiPressed += () => Dispatcher.Invoke(() => _ = HandleClipboardAiAsync());

        if (_clipboardAiKey != Key.None)
            _keyboardHook.SetClipboardAiShortcut(_clipboardAiModifiers, _clipboardAiKey);

        _keyboardHook.Install();

        if (_settings.Window.ShowOverlay)
        {
            _overlay = CreateOverlay();
        }

        // Auto-connect on startup
        var providerTag = (_settings.Transcription.Provider);
        if (providerTag == "whisper" || providerTag == "voxtral" || !string.IsNullOrWhiteSpace(_settings.Transcription.ApiKey))
            Dispatcher.BeginInvoke(async () => await ConnectAsync());

        // Show window if "Start minimized" is not checked
        if (!_settings.Window.StartMinimized)
            Dispatcher.BeginInvoke(() => ShowFromTray());

        _ = CheckForUpdatesAsync();
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();

        // Parse shortcuts
        (_toggleModifiers, _toggleKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Toggle, _toggleKey);
        (_pttModifiers, _pttKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Ptt, _pttKey);
        if (!string.IsNullOrEmpty(_settings.Shortcuts.Mute))
            (_muteModifiers, _muteKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Mute, Key.None);
        if (!string.IsNullOrEmpty(_settings.Shortcuts.LanguageSwitch))
            (_langSwitchModifiers, _langSwitchKey) = UiHelper.ParseShortcut(_settings.Shortcuts.LanguageSwitch, Key.None);
        if (!string.IsNullOrEmpty(_settings.Shortcuts.ClipboardAi))
            (_clipboardAiModifiers, _clipboardAiKey) = UiHelper.ParseShortcut(_settings.Shortcuts.ClipboardAi, Key.None);

        // Provider combo
        UiHelper.SelectComboByTag(ProviderCombo, _settings.Transcription.Provider);

        // Window position
        bool hasPosition = false;
        if (_settings.Window.Left.HasValue) { Left = _settings.Window.Left.Value; hasPosition = true; }
        if (_settings.Window.Top.HasValue) { Top = _settings.Window.Top.Value; hasPosition = true; }
        if (_settings.Window.Width.HasValue && _settings.Window.Width.Value >= MinWidth) Width = _settings.Window.Width.Value;
        if (_settings.Window.Height.HasValue && _settings.Window.Height.Value >= MinHeight) Height = _settings.Window.Height.Value;

        if (!hasPosition)
            CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2 + screen.Left;
        Top = (screen.Height - Height) / 2 + screen.Top;
    }

    private void ApplySettings()
    {
        // Parse and register shortcuts
        (_toggleModifiers, _toggleKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Toggle, _toggleKey);
        (_pttModifiers, _pttKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Ptt, _pttKey);
        if (!string.IsNullOrEmpty(_settings.Shortcuts.Mute))
            (_muteModifiers, _muteKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Mute, Key.None);
        else
            _muteKey = Key.None;

        if (!string.IsNullOrEmpty(_settings.Shortcuts.LanguageSwitch))
        {
            (_langSwitchModifiers, _langSwitchKey) = UiHelper.ParseShortcut(_settings.Shortcuts.LanguageSwitch, Key.None);
        }
        else
            _langSwitchKey = Key.None;

        if (_connected)
        {
            _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
            _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
            if (_muteKey != Key.None)
                _keyboardHook.SetMuteShortcut(_muteModifiers, _muteKey);
            if (_langSwitchKey != Key.None)
                _keyboardHook.SetLanguageSwitchShortcut(_langSwitchModifiers, _langSwitchKey);
        }

        if (!string.IsNullOrEmpty(_settings.Shortcuts.ClipboardAi))
        {
            (_clipboardAiModifiers, _clipboardAiKey) = UiHelper.ParseShortcut(_settings.Shortcuts.ClipboardAi, Key.None);
        }
        else
            _clipboardAiKey = Key.None;

        // ClipboardAi works regardless of connection state
        if (_clipboardAiKey != Key.None)
            _keyboardHook.SetClipboardAiShortcut(_clipboardAiModifiers, _clipboardAiKey);
        else
            _keyboardHook.SetClipboardAiShortcut(ModifierKeys.None, Key.None);

        // Prompt keys
        ApplyPromptKeys();

        // Theme
        ThemeManager.Apply(_settings.Window.Theme);

        // Sound
        SoundFeedback.Init(_settings.Audio.Tone);
        KeyboardInjector.InputDelayMs = _settings.Audio.InputDelayMs;

        _controller.Configure(_settings);
    }

    private void ApplyPromptKeys()
    {
        var vkeys = new HashSet<int>();
        foreach (var p in _settings.Llm.Prompts)
        {
            var vk = RecordingController.VKeyFromString(p.Key);
            if (vk != 0) vkeys.Add(vk);
        }
        _keyboardHook.SetPromptKeys(vkeys);
    }

    private void SaveSettings()
    {
        var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var micItem = MicrophoneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;

        _settings = _settings with
        {
            Transcription = _settings.Transcription with
            {
                Provider = (string)(providerItem?.Tag ?? "deepgram"),
                Language = (string)((LanguageComboMain.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "de"),
            },
            Audio = _settings.Audio with
            {
                Microphone = micItem?.Content?.ToString() ?? "",
                InputMode = (string)((InputModeComboMain.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "Type"),
            },
            Window = _settings.Window with
            {
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height,
            },
        };

        _settingsService.Save(_settings);
    }

    // ── UI Events ─────────────────────────────────────────────────────────

    private void ExportButton_Click(object sender, RoutedEventArgs e) => ExportTranscript();
    private void HistoryButton_Click(object sender, RoutedEventArgs e) => ShowTranscriptHistory();
    private void StatsButton_Click(object sender, RoutedEventArgs e) => ShowStats();

    private void ExportTranscript()
    {
        var log = _controller.TranscriptLog;
        if (log.Count == 0)
        {
            MessageBox.Show("No transcript to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            FileName = $"transcript_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt"
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new System.Text.StringBuilder();
        foreach (var (timestamp, text) in log)
            sb.AppendLine($"[{timestamp:HH:mm:ss}] {text}");

        System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
    }

    private void LogButton_Click(object sender, RoutedEventArgs e)
    {
        _logWindow.Show();
        _logWindow.Activate();
    }

    private void ShowTranscriptHistory()
    {
        var history = new TranscriptHistoryWindow { Owner = this };
        history.Show();
    }

    private void ShowStats()
    {
        var statsWindow = new StatsWindow(_stats) { Owner = this };
        statsWindow.Show();
    }

    private void ShowAbout()
    {
        ShowFromTray();
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_connected)
            await ConnectAsync();
        else
            await DisconnectAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var providerTag = (string)(providerItem?.Tag ?? "deepgram");

        var dialog = new SettingsWindow(_settings, providerTag, _keyboardHook, _replacements);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            var oldProvider = _settings.Transcription.Provider;
            var newProvider = dialog.ResultSettings.Transcription.Provider;

            // Preserve fields that MainWindow owns (microphone, window position)
            _settings = dialog.ResultSettings with
            {
                Audio = dialog.ResultSettings.Audio with
                {
                    Microphone = _settings.Audio.Microphone,
                },
                Window = _settings.Window with
                {
                    StartMinimized = dialog.ResultSettings.Window.StartMinimized,
                    ShowOverlay = dialog.ResultSettings.Window.ShowOverlay,
                    SettingsWidth = dialog.ResultSettings.Window.SettingsWidth,
                    SettingsHeight = dialog.ResultSettings.Window.SettingsHeight,
                    Theme = dialog.ResultSettings.Window.Theme,
                    LogLevel = dialog.ResultSettings.Window.LogLevel,
                },
            };

            // Sync MainWindow combos
            if (oldProvider != newProvider)
                UiHelper.SelectComboByTag(ProviderCombo, newProvider);
            UiHelper.SelectComboByTag(InputModeComboMain, _settings.Audio.InputMode);
            UiHelper.SelectComboByTag(LanguageComboMain, _settings.Transcription.Language);
            AutoCorrectionCheckMain.IsChecked = _settings.AutoCorrection.Enabled;

            ApplySettings();
            _settingsService.Save(_settings);

            // Toggle overlay based on setting
            if (_settings.Window.ShowOverlay && _overlay is null)
            {
                _overlay = CreateOverlay();
                if (_connected) { _overlay.Show(); UpdateOverlay(); }
            }
            else if (!_settings.Window.ShowOverlay && _overlay is not null)
            {
                _overlay.Close();
                _overlay = null;
            }

            // Reconnect if provider changed while connected
            if (oldProvider != newProvider && _connected)
            {
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await DisconnectAsync();
                        await ConnectAsync();
                    });
                });
            }
        }
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

    // ── Provider ─────────────────────────────────────────────────────────

    private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_isLoading)
            SaveSettings();
    }

    // ── Input Mode ──────────────────────────────────────────────────────

    private void InputModeComboMain_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_isLoading)
        {
            SaveSettings();
            ApplySettings();
        }
    }

    // ── Auto-Correction Toggle ────────────────────────────────────────

    private void AutoCorrectionCheckMain_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        var enabled = AutoCorrectionCheckMain.IsChecked == true;
        _settings = _settings with
        {
            AutoCorrection = _settings.AutoCorrection with { Enabled = enabled }
        };
        _controller.Configure(_settings);
        _settingsService.Save(_settings);
        UpdateOverlay();
    }

    // ── Language ────────────────────────────────────────────────────────

    private async void LanguageComboMain_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var tag = (string)((LanguageComboMain.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "de");

        // Skip if unchanged
        if (tag == _settings.Transcription.Language) return;

        _settings = _settings with
        {
            Transcription = _settings.Transcription with { Language = tag }
        };
        _settingsService.Save(_settings);

        if (!_connected) return;

        var displayName = tag switch
        {
            "de" => "German (de)",
            "en" => "English (en)",
            _ => "Automatic"
        };

        try
        {
            if (_provider is WhisperService whisper)
            {
                await whisper.SetLanguageAsync(tag);
                ToastWindow.ShowToast($"Language: {displayName}", Green.Color, autoClose: true);
            }
            else
            {
                ToastWindow.ShowToast($"Language: {displayName} (reconnecting\u2026)", Yellow.Color, autoClose: false);
                await DisconnectAsync();
                await ConnectAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Language switch failed");
            ToastWindow.ShowToast("Language switch failed", Red.Color, autoClose: true);
        }
    }

    // ── Window Closing ───────────────────────────────────────────────────

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        TranscriptHistoryWindow.SaveSession(_controller.TranscriptLog);
        Log.Information("Application shutting down");
        _audio.StopDevicePolling();
        _tray.Dispose();
        _ = DisconnectAsync();
        _keyboardHook.Dispose();
        _audio.Dispose();
        _replacements.Dispose();
        _overlay?.Close();
        Application.Current.Shutdown();
        Log.CloseAndFlush();
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────

    private async Task ConnectAsync()
    {
        var providerItem = (System.Windows.Controls.ComboBoxItem)ProviderCombo.SelectedItem;
        var providerType = (string)providerItem.Tag;

        SetStatus("Connecting \u2026", Yellow);
        ConnectButton.IsEnabled = false;

        try
        {
            var language = _settings.Transcription.Language;

            if (providerType == "whisper")
            {
                var modelName = _settings.Transcription.WhisperModel;

                if (!WhisperModelManager.IsModelDownloaded(modelName))
                {
                    MessageBox.Show("Please download the Whisper model first.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectButton.IsEnabled = true;
                    SetStatus("Not connected", Red);
                    return;
                }

                var whisperKeywords = _settings.Transcription.Keywords;
                var tuning = _settings.Transcription.WhisperTuning;
                Log.Information("Creating WhisperService with keywords={Keywords}, sampling={Sampling}, beamSize={BeamSize}, entropy={Entropy}",
                    whisperKeywords, tuning.SamplingStrategy, tuning.BeamSize, tuning.EntropyThreshold);
                _provider = new WhisperService(modelName, language, whisperKeywords, tuning);
            }
            else if (providerType == "voxtral")
            {
                var mistralKey = ResolveMistralApiKey();
                if (string.IsNullOrEmpty(mistralKey))
                {
                    MessageBox.Show("Please configure a Mistral API key in AI Post-Processing settings first.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectButton.IsEnabled = true;
                    SetStatus("Not connected", Red);
                    return;
                }

                var model = _settings.Transcription.VoxtralModel;
                Log.Information("Creating VoxtralService with model={Model}", model);
                _provider = new VoxtralService(mistralKey, model, language);
            }
            else
            {
                var apiKey = _settings.Transcription.ApiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("Please enter a Deepgram API key.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectButton.IsEnabled = true;
                    SetStatus("Not connected", Red);
                    return;
                }
                var keywords = _settings.Transcription.Keywords
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToArray();
                _provider = new DeepgramService(apiKey, language, keywords.Length > 0 ? keywords : null);
            }

            _provider.ErrorOccurred += OnError;
            _provider.Disconnected += OnDisconnected;

            _provider.SilenceSkipped += () => Dispatcher.Invoke(() =>
                ToastWindow.ShowToast("No speech detected", Colors.Orange, true));

            if (_provider is DeepgramService deepgram)
            {
                deepgram.Reconnecting += OnReconnecting;
                deepgram.Reconnected += OnReconnected;
            }

            await _provider.ConnectAsync();

            _connected = true;
            _controller.SetProvider(_provider, true);
            _controller.Configure(_settings);
            _audio.Initialize();
            RegisterHotkeys();

            var label = providerType switch
            {
                "whisper" => "Whisper (local)",
                "voxtral" => "Voxtral (cloud)",
                _ => "Deepgram",
            };
            SetStatus($"Connected - {label}", Green);
            ConnectButton.Content = "Disconnect";
            ConnectButton.Background = Red;
            SaveSettings();
            Log.Information("{Provider} connected (Language: {Language})", label, language);
            _tray.Update(_connected, _controller.IsRecording, _muted);
            _overlay?.Show();
            _overlay?.UpdateState(false, false, $"Connected \u2013 {label}", Green.Color);
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

        _controller.StopRecording();

        if (_provider is not null)
        {
            _controller.SetProvider(null, false);
            await _provider.DisposeAsync();
            _provider = null;
        }
        SetStatus("Not connected", Red);
        ConnectButton.Content    = "Connect";
        ConnectButton.Background = Blue;
        Log.Information("Provider disconnected");
        _tray.Update(_connected, _controller.IsRecording, _muted);
        _overlay?.Hide();
    }

    private string? ResolveMistralApiKey()
    {
        foreach (var kvp in _settings.Llm.EndpointKeys)
        {
            if (kvp.Key.Contains("api.mistral.ai", StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return null;
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        if (_muteKey != Key.None)
            _keyboardHook.SetMuteShortcut(_muteModifiers, _muteKey);
        if (_langSwitchKey != Key.None)
            _keyboardHook.SetLanguageSwitchShortcut(_langSwitchModifiers, _langSwitchKey);
    }

    // ── Transcript Display ────────────────────────────────────────────────

    private void AppendTranscript(string text)
    {
        var current = TranscriptText.Text;
        if (current == "Transcript will appear here \u2026")
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

    private void OnReconnecting(int attempt, int maxAttempts) =>
        Dispatcher.Invoke(() =>
        {
            SetStatus($"Reconnecting ({attempt}/{maxAttempts})\u2026", Yellow);
            _tray.Update(_connected, _controller.IsRecording, _muted);
            _overlay?.UpdateState(false, false, $"Reconnecting ({attempt}/{maxAttempts})\u2026", Yellow.Color);
            if (attempt == 1)
                ToastWindow.ShowToast("Connection lost \u2013 reconnecting\u2026",
                    Yellow.Color, autoClose: false);
        });

    private void OnReconnected() =>
        Dispatcher.Invoke(() =>
        {
            SetStatus("Connected \u2013 ready", Green);
            _tray.Update(_connected, _controller.IsRecording, _muted);
            UpdateOverlay();
            ToastWindow.ShowToast("Reconnected", Green.Color, autoClose: true);
            SoundFeedback.PlayReconnect();
        });

    private void ToggleMute()
    {
        _muted = !_muted;
        _controller.IsMuted = _muted;

        if (_muted)
            SetStatus("Muted", Yellow);
        else if (_controller.IsRecording)
            SetStatus("\u25CF Recording", Red);
        else if (_connected)
            SetStatus("Connected \u2013 ready", Green);
        else
            SetStatus("Not connected", Red);

        Log.Information("Mute {State}", _muted ? "enabled" : "disabled");
        _tray.Update(_connected, _controller.IsRecording, _muted);
        UpdateOverlay();
    }

    private async void CycleLanguage()
    {
        if (!_connected || _controller.IsRecording) return;

        // Rotate: de → en → (auto) → de
        // Note: must match the language tags in SettingsWindow.xaml LanguageCombo
        var languages = new[] { "de", "en", "" };
        var current = _settings.Transcription.Language;
        var index = Array.IndexOf(languages, current);
        var next = languages[(index + 1) % languages.Length];

        _settings = _settings with
        {
            Transcription = _settings.Transcription with { Language = next }
        };
        _settingsService.Save(_settings);
        UiHelper.SelectComboByTag(LanguageComboMain, next);

        var displayName = next switch
        {
            "de" => "German (de)",
            "en" => "English (en)",
            _ => "Automatic"
        };

        try
        {
            if (_provider is WhisperService whisper)
            {
                await whisper.SetLanguageAsync(next);
                ToastWindow.ShowToast($"Language: {displayName}", Green.Color, autoClose: true);
                Log.Information("Language switched to {Language} (Whisper, instant)", displayName);
            }
            else
            {
                // Deepgram: must reconnect (language is in WebSocket URL)
                ToastWindow.ShowToast($"Language: {displayName} (reconnecting\u2026)", Yellow.Color, autoClose: false);
                Log.Information("Language switched to {Language} (Deepgram, reconnecting)", displayName);
                await DisconnectAsync();
                await ConnectAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Language switch failed");
            ToastWindow.ShowToast("Language switch failed", Red.Color, autoClose: true);
        }
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

    private async Task ProcessWithLlmAsync(string text, NamedPrompt namedPrompt, bool clipboardOnly = false)
    {
        try
        {
            var baseUrl = _settings.Llm.BaseUrl;
            var apiKey = _settings.Llm.ApiKey;
            var model = _settings.Llm.Model;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
            {
                Log.Warning("LLM post-processing skipped: missing API key or model");
                return;
            }

            bool isAnthropic = baseUrl.Contains("anthropic.com", StringComparison.OrdinalIgnoreCase);
            ILlmClient client = isAnthropic
                ? new AnthropicLlmClient(apiKey, model)
                : new OpenAiLlmClient(apiKey, baseUrl, model);

            var sttSec = _controller.LastSttDuration.TotalSeconds;
            var sttLabel = sttSec > 0 ? $"STT: {sttSec:F1}s \u2014 " : "";

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

            if (clipboardOnly)
            {
                System.Windows.Clipboard.SetText(processed);
                Log.Information("LLM result copied to clipboard");
                ToastWindow.ShowToast("Copied to clipboard", Green.Color, autoClose: true);
            }
            else
            {
                await OutputText(processed);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LLM post-processing failed");
            ToastWindow.ShowToast("AI processing failed", true);
        }
    }

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

            var sttSec = _controller.LastSttDuration.TotalSeconds;
            var sttLabel = sttSec > 0 ? $"STT: {sttSec:F1}s \u2014 " : "";

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

        // 4. Focus restore delay
        await Task.Delay(200);

        // 5. Process with LLM
        await ProcessWithLlmAsync(clipboardText, picker.SelectedPrompt, picker.ShiftHeld);
    }

    // ── Update Check ──────────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync()
    {
        var result = await UpdateChecker.CheckAsync();
        if (result != null)
        {
            ToastWindow.ShowToast($"Update available: {result.TagName}", Colors.Blue, autoClose: true);
        }
    }

    // ── Helper Functions ──────────────────────────────────────────────────

    private StatusOverlayWindow CreateOverlay()
    {
        var overlay = new StatusOverlayWindow
        {
            RestoreLeft = _settings.Window.OverlayLeft,
            RestoreTop = _settings.Window.OverlayTop,
        };

        overlay.PositionChanged += (left, top) =>
        {
            _settings = _settings with
            {
                Window = _settings.Window with { OverlayLeft = left, OverlayTop = top }
            };
            _settingsService.Save(_settings);
        };

        overlay.HideRequested += () =>
        {
            _settings = _settings with
            {
                Window = _settings.Window with { ShowOverlay = false }
            };
            _settingsService.Save(_settings);
            _overlay?.Close();
            _overlay = null;
        };

        return overlay;
    }

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

    private void UpdateStatsLine()
    {
        var s = _stats.GetStats();
        var minutes = s.TodaySeconds / 60;
        StatsLine.Text = $"{s.TodayWords} words today  |  {s.TodaySessions} sessions  |  {minutes} min";
    }

    private void SetStatus(string text, SolidColorBrush color)
    {
        StatusText.Text = text;
        StatusDot.Fill  = color;
    }

    internal static void SetLogLevel(string level)
    {
        if (Enum.TryParse<LogEventLevel>(level, out var lvl))
            LevelSwitch.MinimumLevel = lvl;
    }

}
