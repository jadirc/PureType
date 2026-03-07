using System.Drawing.Drawing2D;

namespace VoiceDictation.Helpers;

internal class TrayIconManager : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly System.Drawing.Icon _baseIcon;
    private bool _connected;
    private bool _recording;
    private bool _muted;

    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? MuteToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ExportRequested;
    public event Action? HistoryRequested;
    public event Action? AboutRequested;
    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public TrayIconManager(System.Drawing.Icon baseIcon)
    {
        _baseIcon = baseIcon;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _baseIcon,
            Text = "Voice Dictation",
            Visible = true
        };

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                ShowRequested?.Invoke();
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
                ShowTrayMenu();
        };
    }

    private void ShowTrayMenu()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var menu = new TrayMenuWindow();
            menu.ConnectRequested += () => ConnectRequested?.Invoke();
            menu.DisconnectRequested += () => DisconnectRequested?.Invoke();
            menu.MuteToggleRequested += () => MuteToggleRequested?.Invoke();
            menu.SettingsRequested += () => SettingsRequested?.Invoke();
            menu.ExportRequested += () => ExportRequested?.Invoke();
            menu.HistoryRequested += () => HistoryRequested?.Invoke();
            menu.AboutRequested += () => AboutRequested?.Invoke();
            menu.ShowRequested += () => ShowRequested?.Invoke();
            menu.ExitRequested += () => ExitRequested?.Invoke();
            menu.UpdateState(_connected, _recording, _muted);

            // Position near cursor (above the click point)
            var pos = System.Windows.Forms.Cursor.Position;
            // Use screen-to-WPF coordinate conversion
            var source = System.Windows.PresentationSource.FromVisual(System.Windows.Application.Current.MainWindow);
            double dpiX = 1.0, dpiY = 1.0;
            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            menu.Left = pos.X / dpiX - 100;
            menu.Top = pos.Y / dpiY;

            menu.Loaded += (_, _) =>
            {
                // Adjust so menu appears above the click point
                menu.Top = pos.Y / dpiY - menu.ActualHeight;
                if (menu.Left < 0) menu.Left = 0;
                if (menu.Top < 0) menu.Top = 0;
            };

            menu.Show();
        });
    }

    public void Update(bool connected, bool recording, bool muted)
    {
        _connected = connected;
        _recording = recording;
        _muted = muted;

        var dotColor = connected
            ? System.Drawing.Color.FromArgb(0x40, 0xA0, 0x2B)
            : System.Drawing.Color.FromArgb(0xE6, 0x40, 0x53);
        _trayIcon.Icon = CreateStatusIcon(_baseIcon, dotColor, !connected);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static System.Drawing.Icon CreateStatusIcon(System.Drawing.Icon baseIcon, System.Drawing.Color color, bool showCross)
    {
        using var bmp = baseIcon.ToBitmap();
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int dotSize = 10;
        int x = bmp.Width - dotSize;
        int y = bmp.Height - dotSize;

        using var outlineBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        g.FillEllipse(outlineBrush, x - 1, y - 1, dotSize + 2, dotSize + 2);

        using var brush = new System.Drawing.SolidBrush(color);
        g.FillEllipse(brush, x, y, dotSize, dotSize);

        if (showCross)
        {
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2f);
            g.DrawLine(pen, x + 1, y + dotSize - 1, x + dotSize - 1, y + 1);
        }

        var handle = bmp.GetHicon();
        var icon = System.Drawing.Icon.FromHandle(handle);
        var result = (System.Drawing.Icon)icon.Clone();
        DestroyIcon(handle);
        return result;
    }

    public void Dispose()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
