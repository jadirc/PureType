using System.IO;
using System.Windows;
using System.Windows.Media;
using VoiceDictation.Helpers;
using VoiceDictation.Services;

namespace VoiceDictation;

public partial class MainWindow : Window
{
    // ── Services ──────────────────────────────────────────────────────────
    private DeepgramService? _deepgram;
    private readonly AudioCaptureService _audio = new();

    // ── Hotkeys ───────────────────────────────────────────────────────────
    private GlobalHotkey? _toggleHotkey;
    private LowLevelKeyboardHook? _pttHook;

    // Virtuelle Tastencodes
    private const uint VK_F9       = 0x78;
    private const int  VK_RCONTROL = 0xA3;

    // ── Zustand ───────────────────────────────────────────────────────────
    private bool _connected;
    private bool _recording;
    private bool _isPttMode;

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
        LoadSettings();

        _audio.AudioDataAvailable += OnAudioData;
    }

    private void LoadSettings()
    {
        if (File.Exists(SettingsPath))
            ApiKeyBox.Password = File.ReadAllText(SettingsPath).Trim();
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, ApiKeyBox.Password.Trim());
    }

    // ── UI Events ─────────────────────────────────────────────────────────

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_connected)
            await ConnectAsync();
        else
            await DisconnectAsync();
    }

    private void ModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var item = (System.Windows.Controls.ComboBoxItem)ModeCombo.SelectedItem;
        _isPttMode = (string)item.Tag == "ptt";

        if (HotkeyInfoText is null) return; // noch nicht geladen

        if (_isPttMode)
            HotkeyInfoText.Text = "Rechte Strg gedrückt halten für Aufnahme";
        else
            HotkeyInfoText.Text = "F9 drücken um Aufnahme zu starten/stoppen";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        _ = DisconnectAsync();
        _pttHook?.Dispose();
        _toggleHotkey?.Dispose();
        _audio.Dispose();
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────

    private async Task ConnectAsync()
    {
        var apiKey = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            MessageBox.Show("Bitte einen Deepgram API Key eingeben.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var langItem = (System.Windows.Controls.ComboBoxItem)LanguageCombo.SelectedItem;
        var language = (string)langItem.Tag;

        SetStatus("Verbinde …", Yellow);
        ConnectButton.IsEnabled = false;

        try
        {
            _deepgram = new DeepgramService(apiKey, language);
            _deepgram.TranscriptReceived += OnTranscriptReceived;
            _deepgram.ErrorOccurred      += OnError;
            _deepgram.Disconnected       += OnDisconnected;

            await _deepgram.ConnectAsync();

            _connected = true;
            RegisterHotkeys();

            SetStatus("Verbunden – bereit", Green);
            ConnectButton.Content = "Trennen";
            ConnectButton.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            SaveSettings();
        }
        catch (Exception ex)
        {
            SetStatus("Verbindung fehlgeschlagen", Red);
            MessageBox.Show($"Deepgram-Fehler:\n{ex.Message}", "Verbindungsfehler",
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
        _pttHook?.Dispose();      _pttHook = null;
        _toggleHotkey?.Dispose(); _toggleHotkey = null;

        if (_deepgram is not null)
        {
            await _deepgram.DisposeAsync();
            _deepgram = null;
        }

        _connected = false;
        SetStatus("Nicht verbunden", Red);
        ConnectButton.Content    = "Verbinden";
        ConnectButton.Background = Blue;
    }

    // ── Hotkeys ───────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        // Toggle-Hotkey immer registrieren
        try
        {
            _toggleHotkey = new GlobalHotkey(this, id: 1, GlobalHotkey.MOD_NONE, VK_F9);
            _toggleHotkey.Pressed += OnToggleHotkey;
        }
        catch (Exception ex)
        {
            AppendTranscript($"[Hotkey-Fehler: {ex.Message}]");
        }

        // PTT-Hook immer aktiv (UI schaltet zwischen den Modi um)
        _pttHook = new LowLevelKeyboardHook(VK_RCONTROL);
        _pttHook.KeyDown += OnPttKeyDown;
        _pttHook.KeyUp   += OnPttKeyUp;
    }

    // ── Toggle-Modus ──────────────────────────────────────────────────────

    private void OnToggleHotkey()
    {
        if (_isPttMode) return; // PTT-Modus ist aktiv

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
        _audio.Start();
        SetStatus("● Aufnahme läuft", Red);
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        _audio.Stop();
        if (_connected)
            SetStatus("Verbunden – bereit", Green);
    }

    // ── Audio → Deepgram ──────────────────────────────────────────────────

    private async void OnAudioData(byte[] chunk)
    {
        if (_deepgram is null || !_recording) return;
        await _deepgram.SendAudioAsync(chunk);
    }

    // ── Transkript erhalten ───────────────────────────────────────────────

    private void OnTranscriptReceived(string text)
    {
        Dispatcher.Invoke(() =>
        {
            // Text ins aktive Fenster tippen (außer dieses Fenster)
            KeyboardInjector.TypeText(text);

            // Vorschau aktualisieren
            AppendTranscript(text);
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

    // ── Hilfsfunktionen ───────────────────────────────────────────────────

    private void SetStatus(string text, SolidColorBrush color)
    {
        StatusText.Text = text;
        StatusDot.Fill  = color;
    }
}
