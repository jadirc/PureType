using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PureType;

public partial class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _closeTimer;

    private static Color ThemeColor(string key) =>
        ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    private ToastWindow(string message, Color dotColor)
    {
        InitializeComponent();

        MessageText.Text = message;
        Dot.Fill = new SolidColorBrush(dotColor);

        var workArea = SystemParameters.WorkArea;
        Loaded += (_, _) =>
        {
            Left = workArea.Right - ActualWidth - 16;
            Top = workArea.Bottom - ActualHeight - 16;
        };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) =>
            {
                if (_current == this) _current = null;
                Close();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        };
    }

    public static void ShowToast(string message, bool isRecording)
    {
        ShowToast(message, isRecording ? ThemeColor("RedBrush") : ThemeColor("GreenBrush"), autoClose: true);
    }

    public static void ShowToast(string message, Color dotColor, bool autoClose)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _current?.Close();
            _current = new ToastWindow(message, dotColor);
            _current.Show();
            if (autoClose)
                _current._closeTimer.Start();
        });
    }

}
