using System.Drawing.Drawing2D;

namespace VoiceDictation.Helpers;

internal class TrayIconManager : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.ToolStripLabel _statusLabel;
    private readonly System.Windows.Forms.ToolStripMenuItem _connectItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _muteItem;
    private readonly System.Drawing.Icon _baseIcon;
    private bool _connected;

    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? MuteToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ExportRequested;
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
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();

        _statusLabel = new System.Windows.Forms.ToolStripLabel("Not connected")
        {
            ForeColor = System.Drawing.Color.FromArgb(0xF3, 0x8B, 0xA8)
        };
        menu.Items.Add(_statusLabel);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _connectItem = new System.Windows.Forms.ToolStripMenuItem("Connect", null, (_, _) =>
        {
            if (_connected)
                DisconnectRequested?.Invoke();
            else
                ConnectRequested?.Invoke();
        });
        menu.Items.Add(_connectItem);

        _muteItem = new System.Windows.Forms.ToolStripMenuItem("Mute", null, (_, _) =>
        {
            MuteToggleRequested?.Invoke();
        })
        {
            CheckOnClick = false
        };
        menu.Items.Add(_muteItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("Export Transcript", null, (_, _) => ExportRequested?.Invoke());
        menu.Items.Add("Open", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _trayIcon.ContextMenuStrip = menu;
    }

    public void Update(bool connected, bool recording, bool muted)
    {
        _connected = connected;
        _muteItem.Checked = muted;

        if (muted)
        {
            _statusLabel.Text = "Muted";
            _statusLabel.ForeColor = System.Drawing.Color.FromArgb(0xF9, 0xE2, 0xAF);
        }
        else if (recording)
        {
            _statusLabel.Text = "Recording";
            _statusLabel.ForeColor = System.Drawing.Color.FromArgb(0xF3, 0x8B, 0xA8);
        }
        else if (connected)
        {
            _statusLabel.Text = "Connected";
            _statusLabel.ForeColor = System.Drawing.Color.FromArgb(0x40, 0xA0, 0x2B);
        }
        else
        {
            _statusLabel.Text = "Not connected";
            _statusLabel.ForeColor = System.Drawing.Color.FromArgb(0xF3, 0x8B, 0xA8);
        }

        _connectItem.Text = connected ? "Disconnect" : "Connect";

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
