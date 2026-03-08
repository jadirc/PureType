using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PureType;

public partial class ToastWindow : Window
{
    private static ToastWindow? _current;
    private static string? _currentMessage;
    private readonly DispatcherTimer _closeTimer;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static Color ThemeColor(string key) =>
        ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    private ToastWindow(string message, Color dotColor)
    {
        InitializeComponent();

        MessageText.Text = message;
        Dot.Fill = new SolidColorBrush(dotColor);

        var workArea = SystemParameters.WorkArea;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
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
                if (_current == this) { _current = null; _currentMessage = null; }
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
            // Skip if same message is already showing (prevents flicker on key repeat)
            if (_current is not null && _currentMessage == message)
                return;

            _current?.Close();
            _currentMessage = message;
            _current = new ToastWindow(message, dotColor);
            _current.Show();
            if (autoClose)
                _current._closeTimer.Start();
        });
    }

}
