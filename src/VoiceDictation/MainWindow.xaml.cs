using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
    private System.Drawing.Icon? _baseTrayIcon;

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
        SoundFeedback.Init(_settings.Audio.Tone);

        _audio.AudioDataAvailable += OnAudioData;
        _audio.AudioLevelChanged += OnAudioLevel;
        PopulateMicrophones();
        SelectMicrophoneByName(_settings.Audio.Microphone);

        // Enable settings persistence only after all controls are populated
        _isLoading = false;

        Loaded += (_, _) => ConnectButton.Focus();

        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        ApplyAiTriggerKey();
        _keyboardHook.TogglePressed += OnToggleHotkey;
        _keyboardHook.PttKeyDown += OnPttKeyDown;
        _keyboardHook.PttKeyUp += OnPttKeyUp;

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

        if (_connected)
        {
            _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
            _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        }

        // AI trigger key
        ApplyAiTriggerKey();

        // Sound
        SoundFeedback.Init(_settings.Audio.Tone);
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
            },
        };

        _settingsService.Save(_settings);
    }

    // ── UI Events ─────────────────────────────────────────────────────────

    private void LogButton_Click(object sender, RoutedEventArgs e)
    {
        _logWindow.Show();
        _logWindow.Activate();
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
            // Preserve fields that MainWindow owns (provider, microphone, window position)
            _settings = dialog.ResultSettings with
            {
                Transcription = dialog.ResultSettings.Transcription with
                {
                    Provider = _settings.Transcription.Provider,
                },
                Audio = dialog.ResultSettings.Audio with
                {
                    Microphone = _settings.Audio.Microphone,
                },
                Window = _settings.Window with
                {
                    StartMinimized = dialog.ResultSettings.Window.StartMinimized,
                    SettingsWidth = dialog.ResultSettings.Window.SettingsWidth,
                    SettingsHeight = dialog.ResultSettings.Window.SettingsHeight,
                },
            };
            ApplySettings();
            _settingsService.Save(_settings);
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

    // ── VU Meter ─────────────────────────────────────────────────────────

    private void OnAudioLevel(double level)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var parent = (System.Windows.Controls.Border)VuMeterBar.Parent;
            VuMeterBar.Width = level * parent.ActualWidth;
        });
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
                if (!_aiPostProcessRequested && _settings.Llm.Enabled)
                    _aiPostProcessRequested = aiKeyHeld;
                _recordingSource = RecordingSource.None;
                StopRecording();
            }
            else
            {
                _aiPostProcessRequested = aiKeyHeld && _settings.Llm.Enabled;
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
            _aiPostProcessRequested = aiKeyHeld && _settings.Llm.Enabled;
            _recordingSource = RecordingSource.Ptt;
            StartRecording();
        });
    }

    private void OnPttKeyUp()
    {
        if (_recordingSource != RecordingSource.Ptt) return;
        Dispatcher.Invoke(() =>
        {
            if (!_aiPostProcessRequested && _settings.Llm.Enabled)
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
        SetStatus($"\u25CF Recording{aiLabel}", Red);
        ToastWindow.ShowToast(_aiPostProcessRequested ? "Recording + AI" : "Recording",
            Red.Color, autoClose: false);

        // Add separator between recording sessions in transcript
        var current = TranscriptText.Text;
        if (!string.IsNullOrEmpty(current) && current != "Transcript will appear here \u2026")
            TranscriptText.Text = current + "\n\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\n";

        if (_settings.Audio.Vad && _recordingSource == RecordingSource.Toggle)
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
            SetStatus("Connected \u2013 ready", Green);
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

    // ── System Tray ──────────────────────────────────────────────────────

    private void SetupTrayIcon()
    {
        var iconStream = Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/mic.ico"))?.Stream;

        _baseTrayIcon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Application;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _baseTrayIcon,
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

        menu.Items.Add("Settings", null, (_, _) =>
        {
            Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()));
        });
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
            SetStatus("\u25CF Recording", Red);
        else if (_connected)
            SetStatus("Connected \u2013 ready", Green);
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
            _trayStatusLabel.ForeColor = System.Drawing.Color.FromArgb(0x40, 0xA0, 0x2B);
        }
        else
        {
            _trayStatusLabel.Text = "Not connected";
            _trayStatusLabel.ForeColor = System.Drawing.Color.FromArgb(0xF3, 0x8B, 0xA8);
        }

        _trayConnectItem.Text = _connected ? "Disconnect" : "Connect";

        // Update tray icon with status indicator
        if (_trayIcon != null && _baseTrayIcon != null)
        {
            var color = _connected
                ? System.Drawing.Color.FromArgb(0x40, 0xA0, 0x2B)
                : System.Drawing.Color.FromArgb(0xE6, 0x40, 0x53);
            _trayIcon.Icon = CreateStatusIcon(_baseTrayIcon, color, !_connected);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static System.Drawing.Icon CreateStatusIcon(System.Drawing.Icon baseIcon, System.Drawing.Color color, bool showCross)
    {
        using var bmp = baseIcon.ToBitmap();
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int dotSize = 10;
        int x = bmp.Width - dotSize;
        int y = bmp.Height - dotSize;

        // White outline for contrast
        using var outlineBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        g.FillEllipse(outlineBrush, x - 1, y - 1, dotSize + 2, dotSize + 2);

        // Draw status dot
        using var brush = new System.Drawing.SolidBrush(color);
        g.FillEllipse(brush, x, y, dotSize, dotSize);

        if (showCross)
        {
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2f);
            g.DrawLine(pen, x + 1, y + dotSize - 1, x + dotSize - 1, y + 1);
        }

        var handle = bmp.GetHicon();
        var icon = System.Drawing.Icon.FromHandle(handle);
        // Clone so we can free the GDI handle
        var result = (System.Drawing.Icon)icon.Clone();
        DestroyIcon(handle);
        return result;
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
