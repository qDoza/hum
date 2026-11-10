using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hum.App.Motion;

namespace Hum.App.UI;

/// <summary>Cover URL → downscaled, cached bitmap (loaded asynchronously by WPF).</summary>
public sealed class CoverConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || url.Length == 0) return null;
        if (Cache.TryGetValue(url, out var img)) return img;
        try
        {
            img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(url);
            img.DecodePixelWidth = parameter is string p && int.TryParse(p, out var w) ? w : 96;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            if (Cache.Count > 3000) Cache.Clear();
            Cache[url] = img;
            return img;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Thin progress / volume line: click or drag to set; thickens under the mouse.</summary>
public sealed class ProgressLine : FrameworkElement
{
    private double _value, _drag = -1;
    private readonly Spring _thick = new(0) { Omega = 18, Zeta = 1 };

    public event Action<double>? Committed;
    public event Action<double>? Dragging;

    public ProgressLine()
    {
        Height = 14;
        Cursor = Cursors.Hand;
        MouseEnter += (_, _) => Thicken(1);
        MouseLeave += (_, _) => Thicken(0);
    }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1); if (_drag < 0) InvalidateVisual(); }
    }

    public bool IsDragging => _drag >= 0;

    private void Thicken(double t)
    {
        _thick.Target = t;
        Animator.Run(dt => { var m = _thick.Step(dt); InvalidateVisual(); return m; });
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        _drag = Frac(e);
        Dragging?.Invoke(_drag);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag < 0) return;
        _drag = Frac(e);
        Dragging?.Invoke(_drag);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag < 0) return;
        ReleaseMouseCapture();
        _value = _drag;
        _drag = -1;
        Committed?.Invoke(_value);
        InvalidateVisual();
        e.Handled = true;
    }

    private double Frac(MouseEventArgs e) => Math.Clamp(e.GetPosition(this).X / Math.Max(1, ActualWidth), 0, 1);

    private static readonly Brush Track = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = 3 + 2 * _thick.Value;
        var y = (ActualHeight - h) / 2;
        var v = _drag >= 0 ? _drag : _value;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));
        dc.DrawRoundedRectangle(Track, null, new Rect(0, y, w, h), h / 2, h / 2);
        if (v > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, y, Math.Max(h, w * v), h), h / 2, h / 2);
        if (_thick.Value > 0.05 || _drag >= 0)
            dc.DrawEllipse(Fill, null, new Point(w * v, ActualHeight / 2), 5 * Math.Max(_thick.Value, _drag >= 0 ? 1 : 0), 5 * Math.Max(_thick.Value, _drag >= 0 ? 1 : 0));
    }
}

/// <summary>
/// The «Свой» mix pad, a cousin of SonyControl's EQ graph: drag the point —
/// left/right is sad → happy, down/up is quiet → energetic.
/// </summary>
public sealed class MoodPad : FrameworkElement
{
    private const double Left = 34, Bottom = 18;
    private double _x = 0.7, _y = 0.75;
    private bool _dragging;
    private readonly Spring _glow = new(0) { Omega = 14, Zeta = 1 };

    /// <summary>Drag finished; the new X/Y are in [0..1].</summary>
    public event Action<double, double>? Committed;

    public MoodPad()
    {
        Height = 136;
        Cursor = Cursors.Hand;
        ClipToBounds = false;
    }

    public bool Active
    {
        get => _glow.Target > 0.5;
        set { _glow.Target = value ? 1 : 0; Animator.Run(dt => { var m = _glow.Step(dt); InvalidateVisual(); return m; }); }
    }

    public void Set(double x, double y) { _x = Math.Clamp(x, 0, 1); _y = Math.Clamp(y, 0, 1); InvalidateVisual(); }

    private Rect Area => new(Left, 6, Math.Max(10, ActualWidth - Left - 2), Math.Max(10, ActualHeight - 6 - Bottom - 4));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { CaptureMouse(); _dragging = true; Move(e); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) Move(e); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        Committed?.Invoke(_x, _y);
    }

    private void Move(MouseEventArgs e)
    {
        var a = Area;
        var p = e.GetPosition(this);
        _x = Math.Clamp((p.X - a.Left) / a.Width, 0, 1);
        _y = Math.Clamp(1 - (p.Y - a.Top) / a.Height, 0, 1);
        InvalidateVisual();
    }

    private static readonly Pen Grid = MakeGrid();
    private static Pen MakeGrid() { var p = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) }; p.Freeze(); return p; }

    protected override void OnRender(DrawingContext dc)
    {
        var a = Area;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        for (var i = 0; i <= 4; i++) { var x = a.Left + a.Width * i / 4; dc.DrawLine(Grid, new Point(x, a.Top), new Point(x, a.Bottom)); }
        for (var i = 0; i <= 2; i++) { var y = a.Top + a.Height * i / 2; dc.DrawLine(Grid, new Point(a.Left, y), new Point(a.Right, y)); }

        var g = _glow.Value;
        var lineColor = Lerp(Color.FromRgb(0x5C, 0x5C, 0x5C), Color.FromRgb(0xF2, 0xF2, 0xF2), g);
        var pt = new Point(a.Left + a.Width * _x, a.Top + a.Height * (1 - _y));
        var start = new Point(a.Left, a.Bottom - a.Height * 0.22);
        var end = new Point(a.Right, a.Top + a.Height * (1 - (0.35 + _y * 0.3)));

        var curve = new StreamGeometry();
        using (var c = curve.Open())
        {
            c.BeginFigure(start, false, false);
            c.BezierTo(new Point(start.X + (pt.X - start.X) * 0.55, start.Y), new Point(pt.X - (pt.X - start.X) * 0.45, pt.Y), pt, true, true);
            c.BezierTo(new Point(pt.X + (end.X - pt.X) * 0.45, pt.Y), new Point(end.X - (end.X - pt.X) * 0.55, end.Y), end, true, true);
        }
        var fill = curve.Clone();
        var area = new PathGeometry();
        area.AddGeometry(fill);
        var poly = PathGeometry.CreateFromGeometry(curve);
        var fig = poly.Figures[0].Clone();
        fig.IsClosed = true;
        fig.IsFilled = true;
        fig.Segments.Add(new LineSegment(new Point(a.Right, a.Bottom), false));
        fig.Segments.Add(new LineSegment(new Point(a.Left, a.Bottom), false));
        var under = new PathGeometry(new[] { fig });
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(10 + 14 * g), 0xF2, 0xF2, 0xF2)), null, under);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(lineColor), 2), curve);

        if (g > 0.05) dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(60 * g), 0xF2, 0xF2, 0xF2)), 1), pt, 14, 14);
        dc.DrawEllipse(new SolidColorBrush(lineColor), null, pt, 7.5, 7.5);

        var dim = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
        Text(dc, "бодро", new Point(0, a.Top - 2), dim);
        Text(dc, "тихо", new Point(0, a.Bottom - 12), dim);
        Text(dc, "грустно", new Point(a.Left, a.Bottom + 4), dim);
        Text(dc, "спокойно", new Point(a.Left + a.Width / 2, a.Bottom + 4), dim, center: true);
        Text(dc, "весело", new Point(a.Right, a.Bottom + 4), dim, right: true);
    }

    private void Text(DrawingContext dc, string s, Point p, Brush b, bool center = false, bool right = false)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10.5, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (center) p.X -= ft.Width / 2;
        if (right) p.X -= ft.Width;
        dc.DrawText(ft, p);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
