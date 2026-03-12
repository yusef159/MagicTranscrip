using System.Drawing;
using System.Windows.Forms;

namespace VoiceTyper.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enableItem;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action<bool>? DictationToggled;

    public TrayIconService()
    {
        _enableItem = new ToolStripMenuItem("Dictation Enabled")
        {
            Checked = true,
            CheckOnClick = true
        };
        _enableItem.Click += (_, _) => DictationToggled?.Invoke(_enableItem.Checked);

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enableItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "VoiceTyper - Ready",
            Icon = CreateIcon(Color.FromArgb(60, 140, 230)),
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    public void SetStatus(string text)
    {
        if (text.Length > 63)
            text = text[..63];
        _notifyIcon.Text = text;
    }

    public void SetRecording(bool recording)
    {
        _notifyIcon.Icon = recording
            ? CreateIcon(Color.FromArgb(220, 50, 50))
            : CreateIcon(Color.FromArgb(60, 140, 230));

        SetStatus(recording ? "VoiceTyper - Recording..." : "VoiceTyper - Ready");
    }

    public void SetDictationEnabled(bool enabled)
    {
        _enableItem.Checked = enabled;
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    private static Icon CreateIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Microphone body
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 10, 2, 12, 18);

        // Stand arc
        using var pen = new Pen(color, 2.5f);
        g.DrawArc(pen, 7, 10, 18, 16, 0, 180);

        // Stand line
        g.DrawLine(pen, 16, 26, 16, 30);
        g.DrawLine(pen, 10, 30, 22, 30);

        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
