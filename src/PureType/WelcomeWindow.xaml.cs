using System.Windows;
using System.Windows.Controls;
using PureType.Services;
using Serilog;

namespace PureType;

public partial class WelcomeWindow : Window
{
    private string _selectedProvider = "whisper";
    private CancellationTokenSource? _downloadCts;

    public string SelectedProvider => _selectedProvider;
    public string EnteredApiKey => ApiKeyBox.Text.Trim();
    public string SelectedWhisperModel =>
        (WhisperModelCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "base";
    public bool Completed { get; private set; }

    public WelcomeWindow()
    {
        InitializeComponent();
        PopulateWhisperModels();
        UpdateStartButton();
    }

    private void PopulateWhisperModels()
    {
        WhisperModelCombo.Items.Clear();
        foreach (var (name, displayName, _) in WhisperModelManager.AvailableModels)
        {
            var isDownloaded = WhisperModelManager.IsModelDownloaded(name);
            var suffix = isDownloaded ? " \u2713" : "";
            var item = new ComboBoxItem
            {
                Content = displayName + suffix,
                Tag = name,
                FontWeight = isDownloaded ? FontWeights.SemiBold : FontWeights.Normal
            };
            WhisperModelCombo.Items.Add(item);
        }

        // Select first downloaded model, or default to "base"
        foreach (ComboBoxItem item in WhisperModelCombo.Items)
        {
            if (WhisperModelManager.IsModelDownloaded((string)item.Tag))
            {
                WhisperModelCombo.SelectedItem = item;
                return;
            }
        }

        // No model downloaded — select "base" as default
        foreach (ComboBoxItem item in WhisperModelCombo.Items)
        {
            if ((string)item.Tag == "base")
            {
                WhisperModelCombo.SelectedItem = item;
                return;
            }
        }

        if (WhisperModelCombo.Items.Count > 0)
            WhisperModelCombo.SelectedIndex = 0;
    }

    private void WhisperCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProvider = "whisper";
        WhisperCard.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        DeepgramCard.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        WhisperModelPanel.Visibility = Visibility.Visible;
        ApiKeyPanel.Visibility = Visibility.Collapsed;
        UpdateStartButton();
    }

    private void DeepgramCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProvider = "deepgram";
        DeepgramCard.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        WhisperCard.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        WhisperModelPanel.Visibility = Visibility.Collapsed;
        ApiKeyPanel.Visibility = Visibility.Visible;
        ApiKeyBox.Focus();
        UpdateStartButton();
    }

    private async void DownloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (WhisperModelCombo.SelectedItem is not ComboBoxItem item) return;
        var modelName = (string)item.Tag;

        if (WhisperModelManager.IsModelDownloaded(modelName))
        {
            DownloadStatus.Text = "Model is already downloaded.";
            UpdateStartButton();
            return;
        }

        _downloadCts = new CancellationTokenSource();
        DownloadModelButton.IsEnabled = false;
        WhisperModelCombo.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        DownloadStatus.Text = "Downloading\u2026";

        try
        {
            await WhisperModelManager.DownloadModelAsync(modelName,
                progress => Dispatcher.Invoke(() =>
                {
                    DownloadProgress.Value = progress * 100;
                    DownloadStatus.Text = $"Downloading\u2026 {progress:P0}";
                }),
                _downloadCts.Token);

            DownloadStatus.Text = "Download complete.";
            PopulateWhisperModels();

            // Re-select the just-downloaded model
            foreach (ComboBoxItem ci in WhisperModelCombo.Items)
            {
                if ((string)ci.Tag == modelName)
                {
                    WhisperModelCombo.SelectedItem = ci;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            DownloadStatus.Text = "Download cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Model download failed in wizard");
            DownloadStatus.Text = "Download failed.";
            MessageBox.Show($"Download failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadModelButton.IsEnabled = true;
            WhisperModelCombo.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
            _downloadCts = null;
            UpdateStartButton();
        }
    }

    private void UpdateStartButton()
    {
        if (_selectedProvider == "whisper")
        {
            var modelItem = WhisperModelCombo.SelectedItem as ComboBoxItem;
            var modelName = modelItem?.Tag as string;
            var hasModel = modelName != null && WhisperModelManager.IsModelDownloaded(modelName);
            StartButton.IsEnabled = hasModel;
        }
        else
        {
            StartButton.IsEnabled = true;
        }
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _downloadCts?.Cancel();
        base.OnClosing(e);
    }
}
