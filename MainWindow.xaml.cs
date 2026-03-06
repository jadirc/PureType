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

    // ── Hotkeys ───────────────────────────────────────────────────────────
    private readonly KeyboardHookService _keyboardHook = new();


    // Shortcut-Einstellungen (Defaults)
    private Key _toggleKey = Key.F9;
    private ModifierKeys _toggleModifiers = ModifierKeys.None;
    private Key _pttKey = Key.RightCtrl;
    private ModifierKeys _pttModifiers = ModifierKeys.None;

    // ── Zustand ───────────────────────────────────────────────────────────
    private bool _connected;
    private bool _recording;
    private bool _isPttMode;
    private VadService? _vad;
    private string _interimText = "";
    private bool _isLoading = true;
    private string? _savedMicrophoneDevice;
    private string? _savedWhisperModel;

    // ── Autostart ────────────────────────────────────────────────────────
    private const string AutostartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValueName = "VoiceDictation";

    // Pfad zur Einstellungs-Datei (API-Key speichern)
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "VoiceDictation", "settings.txt");

    // ── Farben ────────────────────────────────────────────────────────────
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

        Log.Information("VoiceDictation gestartet");

        SetupTrayIcon();
        LoadSettings();
        // Init sound after LoadSettings so selected tone is applied
        var toneItem = ToneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        SoundFeedback.Init((string?)toneItem?.Tag);
        _isLoading = false;

        _audio.AudioDataAvailable += OnAudioData;
        _audio.AudioLevelChanged += OnAudioLevel;
        PopulateMicrophones();
        PopulateWhisperModels();
        if (_savedWhisperModel != null)
            SelectComboByTag(WhisperModelCombo, _savedWhisperModel);

        var provItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        WhisperModelPanel.Visibility = (string?)provItem?.Tag == "whisper"
            ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) => ConnectButton.Focus();

        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        _keyboardHook.TogglePressed += OnToggleHotkey;
        _keyboardHook.PttKeyDown += OnPttKeyDown;
        _keyboardHook.PttKeyUp += OnPttKeyUp;

        // Auto-connect nach Start (unabhängig von Fenster-Sichtbarkeit)
        var autoProvider = (ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        if (autoProvider == "whisper" || !string.IsNullOrWhiteSpace(ApiKeyBox.Password))
            Dispatcher.BeginInvoke(async () => await ConnectAsync());

        // Fenster anzeigen wenn "Minimiert starten" nicht angehakt
        if (StartMinimizedCheck.IsChecked != true)
            Dispatcher.BeginInvoke(() => ShowFromTray());
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            CenterOnScreen();
            return;
        }

        var lines = File.ReadAllLines(SettingsPath);
        if (lines.Length > 0)
            ApiKeyBox.Password = lines[0].Trim();

        bool hasPosition = false;
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();

            switch (key)
            {
                case "toggle":
                    ParseToggleShortcut(value);
                    break;
                case "ptt":
                    ParsePttShortcut(value);
                    break;
                case "language":
                    SelectComboByTag(LanguageCombo, value);
                    break;
                case "mode":
                    SelectComboByTag(ModeCombo, value);
                    _isPttMode = value == "ptt";
                    break;
                case "tone":
                    SelectComboByTag(ToneCombo, value);
                    break;
                case "microphone":
                    _savedMicrophoneDevice = value;
                    SelectMicrophoneByName(value);
                    break;
                case "keywords":
                    KeywordsBox.Text = value;
                    break;
                case "provider":
                    SelectComboByTag(ProviderCombo, value);
                    break;
                case "whisper_model":
                    _savedWhisperModel = value;
                    break;
                case "vad":
                    VadCheck.IsChecked = value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    break;
                case "start_minimized":
                    StartMinimizedCheck.IsChecked = value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    break;
                case "left":
                    if (double.TryParse(value, out var left)) { Left = left; hasPosition = true; }
                    break;
                case "top":
                    if (double.TryParse(value, out var top)) { Top = top; hasPosition = true; }
                    break;
                case "width":
                    if (double.TryParse(value, out var w) && w >= MinWidth) Width = w;
                    break;
                case "height":
                    if (double.TryParse(value, out var h) && h >= MinHeight) Height = h;
                    break;
            }
        }

        // UI-Felder direkt setzen (Controls existieren nach InitializeComponent)
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
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var langItem = LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var modeItem = ModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var micItem = MicrophoneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var micName = micItem?.Content?.ToString() ?? "";
        var providerItem = ProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var whisperModelItem = WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var lines = new List<string>
        {
            ApiKeyBox.Password.Trim(),
            $"toggle={FormatShortcut(_toggleModifiers, _toggleKey)}",
            $"ptt={FormatShortcut(_pttModifiers, _pttKey)}",
            $"language={(string)(langItem?.Tag ?? "de")}",
            $"mode={(string)(modeItem?.Tag ?? "toggle")}",
            $"tone={(string)((ToneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag ?? "Sanft")}",
            $"microphone={micName}",
            $"keywords={KeywordsBox.Text.Trim()}",
            $"provider={(string)(providerItem?.Tag ?? "deepgram")}",
            $"whisper_model={(string)(whisperModelItem?.Tag ?? "tiny")}",
            $"vad={VadCheck.IsChecked == true}",
            $"start_minimized={StartMinimizedCheck.IsChecked == true}",
            $"left={Left}",
            $"top={Top}",
            $"width={Width}",
            $"height={Height}"
        };
        File.WriteAllLines(SettingsPath, lines);
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tag)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in combo.Items)
        {
            if ((string)item.Tag == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
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

    private void ModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var item = (System.Windows.Controls.ComboBoxItem)ModeCombo.SelectedItem;
        _isPttMode = (string)item.Tag == "ptt";
        if (_recording) StopRecording();
        UpdateHotkeyInfoText();
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

    // ── Mikrofon ──────────────────────────────────────────────────────────

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

    // ── Provider / Whisper Modell ────────────────────────────────────────

    private void PopulateWhisperModels()
    {
        WhisperModelCombo.Items.Clear();
        foreach (var (name, displayName, _) in WhisperModelManager.AvailableModels)
        {
            var suffix = WhisperModelManager.IsModelDownloaded(name) ? " [OK]" : "";
            var item = new System.Windows.Controls.ComboBoxItem
            {
                Content = displayName + suffix,
                Tag = name
            };
            WhisperModelCombo.Items.Add(item);
        }
        if (WhisperModelCombo.Items.Count > 0)
            WhisperModelCombo.SelectedIndex = 0;
    }

    private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading || ProviderCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var isWhisper = (string)item.Tag == "whisper";
        WhisperModelPanel.Visibility = isWhisper ? Visibility.Visible : Visibility.Collapsed;
        if (!_isLoading)
            SaveSettings();
    }

    private async void DownloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (WhisperModelCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var modelName = (string)item.Tag;

        if (WhisperModelManager.IsModelDownloaded(modelName))
        {
            MessageBox.Show("Modell ist bereits heruntergeladen.", "Info",
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
            MessageBox.Show($"Modell '{modelName}' erfolgreich heruntergeladen.", "Fertig",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Model download failed");
            MessageBox.Show($"Download fehlgeschlagen:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadModelButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    // ── Autostart ────────────────────────────────────────────────────────

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
            Log.Warning(ex, "Autostart-Registrierung fehlgeschlagen");
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
        box.Text = "Taste drücken…";
        box.Foreground = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
        _keyboardHook.SuppressWinKey = true;
    }

    private void ShortcutBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var box = (System.Windows.Controls.TextBox)sender;
        if (box.Text == "Taste drücken…")
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
            box.Text = "Bereits vergeben!";
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
        UpdateHotkeyInfoText();
        Keyboard.ClearFocus();
    }

    private void UpdateHotkeyInfoText()
    {
        if (HotkeyInfoText is null) return;
        if (_isPttMode)
            HotkeyInfoText.Text = $"{PttShortcutBox.Text} gedrückt halten für Aufnahme";
        else
            HotkeyInfoText.Text = $"{ToggleShortcutBox.Text} drücken um Aufnahme zu starten/stoppen";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        Log.Information("Anwendung wird beendet");
        _trayIcon?.Dispose();
        _ = DisconnectAsync();
        _keyboardHook.Dispose();
        _audio.Dispose();
        Application.Current.Shutdown();
        Log.CloseAndFlush();
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────

    private async Task ConnectAsync()
    {
        var providerItem = (System.Windows.Controls.ComboBoxItem)ProviderCombo.SelectedItem;
        var providerType = (string)providerItem.Tag;

        SetStatus("Verbinde …", Yellow);
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
                    MessageBox.Show("Bitte zuerst das Whisper-Modell herunterladen.", "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectButton.IsEnabled = true;
                    SetStatus("Nicht verbunden", Red);
                    return;
                }

                _provider = new WhisperService(modelName, language);
            }
            else
            {
                var apiKey = ApiKeyBox.Password.Trim();
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("Bitte einen Deepgram API Key eingeben.", "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectButton.IsEnabled = true;
                    SetStatus("Nicht verbunden", Red);
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

            var label = providerType == "whisper" ? "Whisper (lokal)" : "Deepgram";
            SetStatus($"Verbunden - {label}", Green);
            ConnectButton.Content = "Trennen";
            ConnectButton.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            SaveSettings();
            Log.Information("{Provider} verbunden (Sprache: {Language})", label, language);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Verbindung fehlgeschlagen");
            SetStatus("Verbindung fehlgeschlagen", Red);
            MessageBox.Show($"Fehler:\n{ex.Message}", "Verbindungsfehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async Task DisconnectAsync()
    {
        StopRecording();
        _keyboardHook.Uninstall();

        if (_provider is not null)
        {
            await _provider.DisposeAsync();
            _provider = null;
        }

        _connected = false;
        SetStatus("Nicht verbunden", Red);
        ConnectButton.Content    = "Verbinden";
        ConnectButton.Background = Blue;
        Log.Information("Provider getrennt");
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        _keyboardHook.SetToggleShortcut(_toggleModifiers, _toggleKey);
        _keyboardHook.SetPttShortcut(_pttModifiers, _pttKey);
        _keyboardHook.Install();
    }

    // ── Toggle-Modus ──────────────────────────────────────────────────────

    private void OnToggleHotkey()
    {
        if (_isPttMode) return;
        Dispatcher.Invoke(() =>
        {
            if (_recording) StopRecording();
            else            StartRecording();
        });
    }

    // ── Push-to-Talk ──────────────────────────────────────────────────────

    private void OnPttKeyDown()
    {
        if (!_isPttMode || !_connected) return;
        Dispatcher.Invoke(StartRecording);
    }

    private void OnPttKeyUp()
    {
        if (!_isPttMode) return;
        Dispatcher.Invoke(StopRecording);
    }

    // ── Aufnahme starten/stoppen ──────────────────────────────────────────

    private void StartRecording()
    {
        if (_recording || !_connected) return;
        _recording = true;
        SoundFeedback.PlayStart();
        _audio.Start();
        SetStatus("● Aufnahme läuft", Red);

        if (VadCheck.IsChecked == true)
        {
            _vad = new VadService();
            _vad.SilenceDetected += () => Dispatcher.Invoke(StopRecording);
            _vad.Reset();
        }
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

        // Provider-Puffer flushen, damit letztes Transkript sofort kommt
        if (_provider is not null)
            await _provider.SendFinalizeAsync();

        SoundFeedback.PlayStop();

        if (_connected)
            SetStatus("Verbunden – bereit", Green);
    }

    // ── Audio → Provider ──────────────────────────────────────────────────

    private async void OnAudioData(byte[] chunk)
    {
        if (_provider is null || !_recording) return;
        await _provider.SendAudioAsync(chunk);
        _vad?.ProcessAudio(chunk);
    }

    // ── Transkript erhalten ───────────────────────────────────────────────

    private void OnTranscriptReceived(string text, bool isFinal)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (isFinal)
            {
                _interimText = "";
                InterimText.Text = "";
                AppendTranscript(text);
                try
                {
                    await KeyboardInjector.TypeTextAsync(text);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fehler bei Textinjektion");
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
        if (current == "Hier erscheint das erkannte Transkript …")
            current = "";

        // Letzte ~500 Zeichen behalten
        var newText = (current + text + " ").TrimStart();
        if (newText.Length > 500)
            newText = newText[^500..];

        TranscriptText.Text = newText;
        TranscriptScroll.ScrollToBottom();
    }

    // ── Fehler / Disconnect ───────────────────────────────────────────────

    private void OnError(string message) =>
        Dispatcher.Invoke(() => SetStatus($"Fehler: {message}", Red));

    private void OnDisconnected() =>
        Dispatcher.Invoke(async () =>
        {
            if (_connected) // unerwartet getrennt
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

        _trayIcon.Click += (_, _) => ShowFromTray();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => ShowFromTray());
        menu.Items.Add("Beenden", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            _keyboardHook.Dispose();
            Application.Current.Shutdown();
        });
        _trayIcon.ContextMenuStrip = menu;
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

    // ── Hilfsfunktionen ───────────────────────────────────────────────────

    private void SetStatus(string text, SolidColorBrush color)
    {
        StatusText.Text = text;
        StatusDot.Fill  = color;
    }

}
