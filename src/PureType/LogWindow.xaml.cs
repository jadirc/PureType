using System.Windows;

namespace PureType;

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
        // Hide instead of close so it can be reused
        e.Cancel = true;
        Hide();
    }
}
