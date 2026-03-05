using System.Windows;

namespace VoiceDictation;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
    }

    public void AppendLog(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LogTextBox.AppendText(message + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Verstecken statt schließen, damit es wiederverwendet werden kann
        e.Cancel = true;
        Hide();
    }
}
