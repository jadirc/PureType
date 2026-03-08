using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PureType;

public partial class StatusOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private bool _dragging;
    private System.Windows.Point _dragStart;
    private double _windowLeft, _windowTop;

    /// <summary>Fired when the user finishes dragging. Args: (left, top).</summary>
    public event Action<double, double>? PositionChanged;

    /// <summary>Fired when the user middle-clicks to hide the overlay.</summary>
    public event Action? HideRequested;

    /// <summary>Saved position to restore; set before Show().</summary>
    public double? RestoreLeft { get; set; }

    /// <summary>Saved position to restore; set before Show().</summary>
    public double? RestoreTop { get; set; }

    public StatusOverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

        Loaded += (_, _) =>
        {
            if (RestoreLeft.HasValue && RestoreTop.HasValue && IsOnScreen(RestoreLeft.Value, RestoreTop.Value))
            {
                Left = RestoreLeft.Value;
                Top = RestoreTop.Value;
            }
            else
            {
                PositionTopCenter();
            }
        };
    }

    public void UpdateState(bool recording, bool muted, string statusText, Color dotColor)
    {
        StatusDot.Fill = new SolidColorBrush(dotColor);
        StatusText.Text = statusText;
    }

    // ── Drag ────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = PointToScreen(e.GetPosition(this));
        _windowLeft = Left;
        _windowTop = Top;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        var current = PointToScreen(e.GetPosition(this));
        Left = _windowLeft + (current.X - _dragStart.X);
        Top = _windowTop + (current.Y - _dragStart.Y);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        PositionChanged?.Invoke(Left, Top);
        e.Handled = true;
    }

    // ── Double-click: reset to center ───────────────────────────────────

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _dragging = false;
            ReleaseMouseCapture();
            PositionTopCenter();
            PositionChanged?.Invoke(Left, Top);
            e.Handled = true;
        }
    }

    // ── Middle-click: hide overlay ──────────────────────────────────────

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            HideRequested?.Invoke();
            e.Handled = true;
            return;
        }
        base.OnMouseDown(e);
    }

    // ── Positioning ─────────────────────────────────────────────────────

    private void PositionTopCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + 16;
    }

    /// <summary>
    /// Returns true if the given position is at least partially visible on the virtual screen.
    /// </summary>
    internal static bool IsOnScreen(double left, double top)
    {
        return left >= SystemParameters.VirtualScreenLeft - 100
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top >= SystemParameters.VirtualScreenTop - 50
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }
}
