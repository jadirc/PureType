using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using Serilog.Core;
using Serilog.Events;
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
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private readonly TrayIconManager _tray;
    private readonly RecordingController _controller;

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
    private Key _muteKey = Key.None;
    private ModifierKeys _muteModifiers = ModifierKeys.None;

    // ── State ────────────────────────────────────────────────────────────
    private bool _connected;
    private bool _muted;
    private bool _isLoading = true;

    // Settings
    private readonly SettingsService _settingsService = new();
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
            "VoiceDictation", "log.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(UiSink)
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        UiSink.SetCallback(_logWindow.AppendLog);

        Log.Information("VoiceDictation started");

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
        _tray.AboutRequested += () => Dispatcher.Invoke(ShowAbout);
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

        SoundFeedback.Init(_settings.Audio.Tone);
        KeyboardInjector.InputDelayMs = _settings.Audio.InputDelayMs;

        _controller = new RecordingController(_audio, _keyboardHook, _replacements);
        _controller.StatusChanged += (text, color) => Dispatcher.Invoke(() => SetStatus(text, new SolidColorBrush(color)));
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
            _tray.Update(_connected, _controller.IsRecording, _muted));
        _controller.AudioLevelChanged += level => Dispatcher.BeginInvoke(() =>
        {
            var parent = (System.Windows.Controls.Border)VuMeterBar.Parent;
            VuMeterBar.Width = level * parent.ActualWidth;
        });
        _controller.RecordingStopped += () => Dispatcher.Invoke(() => VuMeterBar.Width = 0);
        _controller.LlmProcessingRequested += text => Dispatcher.Invoke(() => _ = ProcessWithLlmAsync(text));
        _controller.ClipboardRequested += text => Dispatcher.Invoke(() =>
        {
            System.Windows.Clipboard.SetText(text);
        });
        _controller.ToastRequested += (message, color, autoClose) =>
            ToastWindow.ShowToast(message, color, autoClose);

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

        PopulateMicrophones();
        _audio.StartDevicePolling();
        SelectMicrophoneByName(_settings.Audio.Microphone);

        // Enable settings persistence only after all controls are populated
        _isLoading = false;

        Loaded += (_, _) => ConnectButton.Focus();

        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        ApplyAiTriggerKey();
        _keyboardHook.TogglePressed += aiKeyHeld => Dispatcher.Invoke(() => _controller.HandleToggle(aiKeyHeld));
        _keyboardHook.PttKeyDown += aiKeyHeld => Dispatcher.Invoke(() => _controller.HandlePttDown(aiKeyHeld));
        _keyboardHook.PttKeyUp += () => Dispatcher.Invoke(() => _controller.HandlePttUp());
        _keyboardHook.MutePressed += () => Dispatcher.Invoke(ToggleMute);

        // Auto-connect on startup
        var providerTag = (_settings.Transcription.Provider);
        if (providerTag == "whisper" || !string.IsNullOrWhiteSpace(_settings.Transcription.ApiKey))
            Dispatcher.BeginInvoke(async () => await ConnectAsync());

        // Show window if "Start minimized" is not checked
        if (!_settings.Window.StartMinimized)
            Dispatcher.BeginInvoke(() => ShowFromTray());
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();

        // Parse shortcuts
        (_toggleModifiers, _toggleKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Toggle, _toggleKey);
        (_pttModifiers, _pttKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Ptt, _pttKey);
        if (!string.IsNullOrEmpty(_settings.Shortcuts.Mute))
            (_muteModifiers, _muteKey) = UiHelper.ParseShortcut(_settings.Shortcuts.Mute, Key.None);

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

        if (_connected)
        {
            _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
            _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
            if (_muteKey != Key.None)
                _keyboardHook.SetMuteShortcut(_muteModifiers, _muteKey);
        }

        // AI trigger key
        ApplyAiTriggerKey();

        // Theme
        ThemeManager.Apply(_settings.Window.Theme);

        // Sound
        SoundFeedback.Init(_settings.Audio.Tone);
        KeyboardInjector.InputDelayMs = _settings.Audio.InputDelayMs;

        _controller.Configure(_settings);
    }

    private void ApplyAiTriggerKey()
    {
        var tag = _settings.Shortcuts.AiTriggerKey;
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

    private void SaveSettings()
    {
        var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var micItem = MicrophoneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;

        _settings = _settings with
        {
            Transcription = _settings.Transcription with
            {
                Provider = (string)(providerItem?.Tag ?? "deepgram"),
            },
            Audio = _settings.Audio with
            {
                Microphone = micItem?.Content?.ToString() ?? "",
            },
            Window = new WindowSettings
            {
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height,
                StartMinimized = _settings.Window.StartMinimized,
                Theme = _settings.Window.Theme,
            },
        };

        _settingsService.Save(_settings);
    }

    // ── UI Events ─────────────────────────────────────────────────────────

    private void ExportButton_Click(object sender, RoutedEventArgs e) => ExportTranscript();
    private void HistoryButton_Click(object sender, RoutedEventArgs e) => ShowTranscriptHistory();

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

    private void ShowAbout()
    {
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
                    SettingsWidth = dialog.ResultSettings.Window.SettingsWidth,
                    SettingsHeight = dialog.ResultSettings.Window.SettingsHeight,
                    Theme = dialog.ResultSettings.Window.Theme,
                },
            };

            // Sync MainWindow provider combo
            if (oldProvider != newProvider)
                UiHelper.SelectComboByTag(ProviderCombo, newProvider);

            ApplySettings();
            _settingsService.Save(_settings);

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

                _provider = new WhisperService(modelName, language);
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

            var label = providerType == "whisper" ? "Whisper (local)" : "Deepgram";
            SetStatus($"Connected - {label}", Green);
            ConnectButton.Content = "Disconnect";
            ConnectButton.Background = Red;
            SaveSettings();
            Log.Information("{Provider} connected (Language: {Language})", label, language);
            _tray.Update(_connected, _controller.IsRecording, _muted);
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
        _keyboardHook.Uninstall();

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
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        if (_muteKey != Key.None)
            _keyboardHook.SetMuteShortcut(_muteModifiers, _muteKey);
        _keyboardHook.Install();
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
            if (attempt == 1)
                ToastWindow.ShowToast("Connection lost \u2013 reconnecting\u2026",
                    Yellow.Color, autoClose: false);
        });

    private void OnReconnected() =>
        Dispatcher.Invoke(() =>
        {
            SetStatus("Connected \u2013 ready", Green);
            _tray.Update(_connected, _controller.IsRecording, _muted);
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
            var baseUrl = _settings.Llm.BaseUrl;
            var apiKey = _settings.Llm.ApiKey;
            var model = _settings.Llm.Model;
            var prompt = _settings.Llm.Prompt;

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
            ToastWindow.ShowToast("Waiting for AI response \u2026",
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
