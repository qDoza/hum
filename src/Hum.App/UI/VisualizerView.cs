using System;
using System.Windows;
using System.Windows.Media;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>
/// The music-channel visualizer around the cover: mirrored spectrum bars (bass on top), flying dust and
/// riding waves, or plain rings / one thin ring. It follows what the speakers really play (see
/// <see cref="AudioMeter"/>); without a meter it falls back to a steady pulse.
/// </summary>
public sealed class VisualizerView : FrameworkElement
{
    private const int Bars = 64;
    private readonly float[] _raw = new float[AudioMeter.Bands];
    private readonly double[] _bars = new double[Bars];
    private readonly (double A, double R, double V, double S)[] _dust = new (double, double, double, double)[70];
    private readonly Random _rnd = new();
    private TimeSpan _last;
    private bool _hooked;

    /// <summary>Radius of the cover in the middle, in DIPs.</summary>
    public double CoverRadius { get; set; } = 120;
    public bool Square { get; set; }

    // smoothed levels, read by the player view to move the cover and the light
    public double Level { get; private set; }
    public double Low { get; private set; }
    public double Mid { get; private set; }
    public double High { get; private set; }
    public double Hit { get; private set; }
    public double Kick { get; set; }

    public AudioMeter? Meter { get; set; }

    public VisualizerView()
    {
        IsHitTestVisible = false;
        for (var i = 0; i < _dust.Length; i++) _dust[i] = (_rnd.NextDouble() * Math.PI * 2, _rnd.NextDouble(), 0.2 + _rnd.NextDouble() * 0.8, 0.6 + _rnd.NextDouble() * 1.6);
        IsVisibleChanged += (_, _) => Hook(IsVisible);
        Loaded += (_, _) => Hook(IsVisible);
        Unloaded += (_, _) => Hook(false);
    }

    /// <summary>Fires every frame after the levels are updated.</summary>
    public event Action? Frame;

    private void Hook(bool on)
    {
        if (on == _hooked) return;
        _hooked = on;
        if (on) CompositionTarget.Rendering += OnFrame; else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        var dt = Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;
        var look = App.Hub.Settings.Look;
        var p = App.Hub.Player;
        Level += ((p.IsPlaying ? 1 : 0) - Level) * 0.06;
        double lo, mi, hi, hit;
        var t = now.TotalSeconds;
        if (Meter is { Running: true } m && m.Read(_raw, out var l, out var md, out var h, out var ht))
        { lo = l; mi = md; hi = h; hit = ht; }
        else if (Meter is { Running: true })
        { lo = mi = hi = hit = 0; Array.Clear(_raw); }
        else
        {
            // no meter: a steady pulse that follows the playback position
            var sec = p.PositionMs / 1000;
            var bp = 60 / 112.0;
            var ph = sec % bp / bp;
            var beat = (int)(sec / bp) % 4;
            lo = Math.Exp(-ph * 7) * (beat == 0 ? 1 : beat == 2 ? 0.85 : 0.6);
            mi = 0.35 + 0.2 * Math.Sin(sec * 1.7);
            hi = 0.25 + Math.Exp(-(ph * 2 % 1) * 10) * 0.5;
            hit = ph < 0.08 ? lo : 0;
            for (var i = 0; i < _raw.Length; i++)
                _raw[i] = (float)(i < 16 ? lo * (1 - i / 20.0) : i < 44 ? mi * (0.7 + 0.3 * Math.Sin(i * 0.9 + sec * 2)) : hi * (0.6 + 0.4 * Math.Sin(i * 1.7 - sec * 3)));
        }
        var k = Level * look.VizAmount;
        void Ease(ref double v, double target, double up, double down) => v += (target - v) * (target > v ? up : down);
        double L0 = Low, M0 = Mid, H0 = High, T0 = Hit;
        Ease(ref L0, lo * k, 0.6, 0.15); Ease(ref M0, mi * k, 0.5, 0.15); Ease(ref H0, hi * k, 0.6, 0.2); Ease(ref T0, Math.Min(1, hit * k + Kick), 0.8, 0.12);
        Low = L0; Mid = M0; High = H0; Hit = T0;
        Kick *= 0.92;
        for (var i = 0; i < Bars; i++)
        {
            var target = _raw[i] * k * (0.85 + 0.15 * Math.Sin(t * 3 + i));
            _bars[i] += (target - _bars[i]) * (target > _bars[i] ? 0.55 : 0.14);
        }
        if (!look.ReducedMotion)
            for (var i = 0; i < _dust.Length; i++)
            {
                var d = _dust[i];
                d.R += dt * d.V * (0.04 + Level * 0.1 + Low * 0.5 + Hit * 0.8);
                if (d.R > 1) { d.R = 0; d.A = _rnd.NextDouble() * Math.PI * 2; }
                _dust[i] = d;
            }
        _time = t;
        Frame?.Invoke();
        InvalidateVisual();
    }

    private double _time;

    protected override void OnRender(DrawingContext dc)
    {
        var look = App.Hub.Settings.Look;
        if (look.VizStyle == "off") return;
        double w = ActualWidth, h = ActualHeight, cx = w / 2, cy = h / 2;
        var c = Look.VizColor;
        var cr = CoverRadius * (1 + Low * 0.11 + Hit * 0.06) * (Square ? 1.13 : 1);
        var room = Math.Min(w, h) / 2 - cr;
        var t = _time;

        if (look.VizStyle == "minimal")
        {
            Ring(dc, cx, cy, cr + 14 + Low * 16, t, 1, 1 + Mid * 5, Pen(c, 0.55 + Hit * 0.4, 1.5 + Low * 2.5));
            return;
        }
        if (look.VizStyle == "waves")
        {
            for (var k = 0; k < 6; k++)
            {
                var f = k / 6.0;
                var col = Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
                Ring(dc, cx, cy, cr + 12 + k * 12 + Low * (8 + k * 6), t * (1 + Mid), k, (3 + Low * 14 + Mid * 10 + High * 4) * (1 + k * 0.35), Pen(col, 0.75 - k * 0.1, k == 0 ? 2.4 : 1.3));
            }
            return;
        }

        // dust
        if (!look.ReducedMotion)
        {
            var dustBrush = new SolidColorBrush(c);
            foreach (var d in _dust)
            {
                var rr = cr + d.R * (room + 40) * 1.1;
                var sz = d.S * (1 + Low);
                dc.PushOpacity(Math.Min(1, (1 - d.R) * 1.4) * 0.75);
                dc.DrawEllipse(dustBrush, null, new Point(cx + Math.Cos(d.A) * rr, cy + Math.Sin(d.A) * rr), sz, sz);
                dc.Pop();
            }
        }

        // mirrored bars + a faint wide copy for the glow
        var r0 = cr + 7;
        var maxLen = Math.Max(18, room - 14);
        StreamGeometry BarsGeo(double scale)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
                for (var side = -1; side <= 1; side += 2)
                    for (var i = 0; i < Bars; i++)
                    {
                        var a = -Math.PI / 2 + side * (i + 0.5) / Bars * Math.PI;
                        var len = 2.5 + _bars[i] * maxLen * scale;
                        ctx.BeginFigure(new Point(cx + Math.Cos(a) * r0, cy + Math.Sin(a) * r0), false, false);
                        ctx.LineTo(new Point(cx + Math.Cos(a) * (r0 + len), cy + Math.Sin(a) * (r0 + len)), true, false);
                    }
            g.Freeze();
            return g;
        }
        var big = BarsGeo(1);
        dc.DrawGeometry(null, Pen(c, 0.16, 3.2 + 5 + Low * 7), big);
        dc.DrawGeometry(null, Pen(c, 0.9, 3.2), big);
        dc.DrawGeometry(null, Pen(c, 0.95, 1.4), BarsGeo(0.55));

        for (var k = 0; k < 2; k++)
            Ring(dc, cx, cy, r0 + maxLen * (0.55 + k * 0.25) + Low * 10, t * (1 + Mid * 1.5), k + 3, (2 + Mid * 9 + High * 5) * (1 + k),
                Pen(Colors.White, 0.35 - k * 0.15 + Hit * 0.3, 1.2 - k * 0.4));
    }

    private static Pen Pen(Color c, double alpha, double width)
    {
        var p = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B)), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    private static void Ring(DrawingContext dc, double cx, double cy, double r, double t, int k, double amp, Pen pen)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (var i = 0; i <= 180; i++)
            {
                var a = i / 180.0 * Math.PI * 2;
                var n = Math.Sin(a * 3 + t * 0.8 + k * 1.3) * 0.5 + Math.Sin(a * 5 - t * 1.2 + k * 2.1) * 0.32 + Math.Sin(a * 2 + t * 0.45 + k) * 0.42;
                var rr = r + n * amp;
                var pt = new Point(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr);
                if (i == 0) ctx.BeginFigure(pt, false, true); else ctx.LineTo(pt, true, true);
            }
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
