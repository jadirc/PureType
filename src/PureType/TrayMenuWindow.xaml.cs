using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace PureType;

public partial class TrayMenuWindow : Window
{
    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? MuteToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ExportRequested;
    public event Action? HistoryRequested;
    public event Action? AboutRequested;
    public event Action? StatsRequested;
    public event Action? ShowRequested;
    public event Action? ExitRequested;

    private Border? _connectItem;
    private TextBlock? _connectText;
    private Border? _muteItem;
    private TextBlock? _muteCheckmark;

    private bool _connected;
    private bool _muted;
    private bool _isClosing;

    public TrayMenuWindow()
    {
        InitializeComponent();
        BuildMenu();

        Deactivated += (_, _) => SafeClose();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                SafeClose();
        };
    }

    private void SafeClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }

    private void BuildMenu()
    {
        // Connect / Disconnect
        _connectText = new TextBlock
        {
            Text = "Connect",
            FontSize = 13,
            Foreground = FindBrush("TextBrush")
        };
        _connectItem = AddClickItem(_connectText, () =>
        {
            if (_connected)
                DisconnectRequested?.Invoke();
            else
                ConnectRequested?.Invoke();
        });

        // Mute (with checkmark when muted)
        var mutePanel = new StackPanel { Orientation = Orientation.Horizontal };
        _muteCheckmark = new TextBlock
        {
            Text = "\uE73E",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = FindBrush("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Visibility = Visibility.Collapsed
        };
        mutePanel.Children.Add(_muteCheckmark);
        mutePanel.Children.Add(new TextBlock
        {
            Text = "Mute",
            FontSize = 13,
            Foreground = FindBrush("TextBrush")
        });
        _muteItem = AddClickItem(mutePanel, () => MuteToggleRequested?.Invoke());

        AddSeparator();
        AddClickItem("Settings", () => SettingsRequested?.Invoke());
        AddClickItem("Export Transcript", () => ExportRequested?.Invoke());
        AddClickItem("Transcript History", () => HistoryRequested?.Invoke());
        AddClickItem("Statistics", () => StatsRequested?.Invoke());
        AddClickItem("About", () => AboutRequested?.Invoke());
        AddClickItem("Open", () => ShowRequested?.Invoke());
        AddSeparator();
        AddClickItem("Exit", () => ExitRequested?.Invoke());
    }

    internal record StateInfo(string StatusText, string StatusBrush, string ConnectText, bool MuteVisible);

    internal static StateInfo ComputeState(bool connected, bool recording, bool muted)
    {
        var (statusText, statusBrush) = muted
            ? ("Muted", "YellowBrush")
            : !connected
                ? ("Not connected", "RedBrush")
                : recording
                    ? ("Recording", "RedBrush")
                    : ("Connected", "GreenBrush");

        return new StateInfo(statusText, statusBrush, connected ? "Disconnect" : "Connect", muted);
    }

    public void UpdateState(bool connected, bool recording, bool muted)
    {
        _connected = connected;
        _muted = muted;

        var state = ComputeState(connected, recording, muted);

        StatusLabel.Text = state.StatusText;
        StatusLabel.Foreground = FindBrush(state.StatusBrush);

        if (_connectText != null)
            _connectText.Text = state.ConnectText;

        if (_muteCheckmark != null)
            _muteCheckmark.Visibility = state.MuteVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border AddClickItem(string text, Action onClick)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = FindBrush("TextBrush")
        };
        return AddClickItem(textBlock, onClick);
    }

    private Border AddClickItem(UIElement content, Action onClick)
    {
        var border = new Border
        {
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = content
        };

        border.MouseEnter += (_, _) => border.Background = FindBrush("BorderBrush");
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) =>
        {
            onClick();
            SafeClose();
        };

        MenuPanel.Children.Add(border);
        return border;
    }

    private void AddSeparator()
    {
        MenuPanel.Children.Add(new Separator
        {
            Background = FindBrush("BorderBrush"),
            Margin = new Thickness(8, 4, 8, 4)
        });
    }

    private static Brush FindBrush(string key) =>
        (Brush)Application.Current.FindResource(key);
}
