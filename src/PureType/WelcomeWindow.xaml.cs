using System.Windows;
using System.Windows.Controls;
using PureType.Helpers;
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
        UiHelper.PopulateWhisperModelCombo(WhisperModelCombo);
        UpdateStartButton();
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
                progress => Dispatcher.BeginInvoke(() =>
                {
                    if (!IsLoaded) return;
                    DownloadProgress.Value = progress * 100;
                    DownloadStatus.Text = $"Downloading\u2026 {progress:P0}";
                }),
                _downloadCts.Token);

            DownloadStatus.Text = "Download complete.";
            UiHelper.PopulateWhisperModelCombo(WhisperModelCombo, modelName);
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
            _downloadCts?.Dispose();
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
        _downloadCts?.Dispose();
        base.OnClosing(e);
    }
}
