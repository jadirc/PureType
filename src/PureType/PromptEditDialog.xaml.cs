using System.Windows;
using System.Windows.Input;
using PureType.Services;

namespace PureType;

public partial class PromptEditDialog : Window
{
    public NamedPrompt Result { get; private set; } = new();
    private string _capturedKey = "";

    public PromptEditDialog()
    {
        InitializeComponent();
    }

    public PromptEditDialog(NamedPrompt existing) : this()
    {
        NameBox.Text = existing.Name;
        _capturedKey = existing.Key;
        KeyBox.Text = existing.Key;
        PromptBox.Text = existing.Prompt;
    }

    private void KeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore pure modifier keys and special keys
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.Escape or Key.Return or Key.Tab)
            return;

        _capturedKey = key.ToString();
        KeyBox.Text = _capturedKey;
    }

    private void KeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        KeyHint.Visibility = Visibility.Collapsed;
    }

    private void KeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(KeyBox.Text))
            KeyHint.Visibility = Visibility.Visible;
    }

    private void TemplateCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (TemplateCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var prompt = (string)(item.Tag ?? "");
        if (!string.IsNullOrEmpty(prompt))
        {
            PromptBox.Text = prompt;
            if (string.IsNullOrEmpty(NameBox.Text))
                NameBox.Text = item.Content?.ToString() ?? "";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Please enter a name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(_capturedKey))
        {
            MessageBox.Show("Please press a key to assign as trigger.", "Missing Key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(PromptBox.Text))
        {
            MessageBox.Show("Please enter a prompt.", "Missing Prompt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new NamedPrompt
        {
            Name = NameBox.Text.Trim(),
            Key = _capturedKey,
            Prompt = PromptBox.Text.Trim(),
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
