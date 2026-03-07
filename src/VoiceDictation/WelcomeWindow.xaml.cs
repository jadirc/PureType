using System.Windows;

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
        WhisperCard.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        DeepgramCard.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        ApiKeyPanel.Visibility = Visibility.Collapsed;
    }

    private void DeepgramCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProvider = "deepgram";
        DeepgramCard.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        WhisperCard.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
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
