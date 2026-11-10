using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hum.App;

/// <summary>Taskbar tray icon with the HUM mark; the wave flattens when paused. Left click opens the flyout.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _playItem;
    private Icon? _current;
    private bool? _lastPlaying;

    /// <summary>Left click; the argument is the cursor position in screen pixels.</summary>
    public event Action<System.Drawing.Point>? Clicked;
    public event Action? OpenWindowRequested;
    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Открыть окно", null, (_, _) => OpenWindowRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        _playItem = new ToolStripMenuItem("Играть", null, (_, _) => PlayPauseRequested?.Invoke());
        menu.Items.Add(_playItem);
        menu.Items.Add("Следующий трек", null, (_, _) => NextRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "HUM" };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Clicked?.Invoke(Cursor.Position);
            else if (e.Button == MouseButtons.Middle) PlayPauseRequested?.Invoke();
        };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenWindowRequested?.Invoke(); };
        Update("HUM", false);
    }

    public void Update(string tooltip, bool playing)
    {
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _playItem.Text = playing ? "Пауза" : "Играть";
        if (_lastPlaying == playing) return;
        _lastPlaying = playing;
        var old = _current;
        _current = Draw(playing);
        _icon.Icon = _current;
        if (old is not null) { DestroyIcon(old.Handle); old.Dispose(); }
    }

    private static Icon Draw(bool playing)
    {
        const int s = 32;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.White, 2.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawEllipse(pen, 3.5f, 3.5f, 25, 25);
            // the hum: a small sine through the ring, flat-ish when paused
            var amp = playing ? 3.6f : 1.4f;
            var pts = new PointF[25];
            for (var i = 0; i < pts.Length; i++)
            {
                var x = 8f + i * 16f / (pts.Length - 1);
                pts[i] = new PointF(x, 16f - amp * (float)Math.Sin(i / (double)(pts.Length - 1) * Math.PI * 4));
            }
            g.DrawLines(pen, pts);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_current is not null) DestroyIcon(_current.Handle);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
