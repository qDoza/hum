using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hum.App.Motion;
using Hum.App.Services;
using Colors_ = System.Windows.Media.Colors;

namespace Hum.App.UI;

/// <summary>
/// «Мой эфир» as a living nebula, like the iPhone version. Four colour schemes (deep space, aurora, supernova,
/// ice) picked by the mix; three layers of ragged gas drift and turn on their own, every star has its own
/// orbit, wobble and twinkle, a third drift with the gas, now and then one flares. While the wave plays a
/// ripple runs out on each beat and reaches every star with its own delay.
/// </summary>
public sealed class HeroNebula : FrameworkElement
{
    private sealed record SchemeDef(string[] Fog, string[] Stars, int Seed);
    private static readonly Dictionary<string, SchemeDef> Schemes = new()
    {
        ["space"] = new(new[] { "#1B1464", "#5B2A9C", "#B8327E", "#2A6FD6" }, new[] { "#9FD0FF", "#E7C6FF", "#FF8CC6", "#FFFFFF" }, 11),
        ["aurora"] = new(new[] { "#0B3D3A", "#11A37F", "#2BC4D4", "#6A3FC8" }, new[] { "#A8FFE6", "#7FE3FF", "#D6B8FF", "#FFFFFF" }, 42),
        ["nova"] = new(new[] { "#3A0F1F", "#C2412D", "#FF8A3D", "#D43B7A" }, new[] { "#FFE2A8", "#FFB38A", "#FF9AD0", "#FFFFFF" }, 73),
        ["ice"] = new(new[] { "#071A3A", "#1F4FB8", "#3AA0FF", "#7FD8FF" }, new[] { "#BFE3FF", "#7FB8FF", "#E6F4FF", "#FFFFFF" }, 104),
    };
    private static readonly Dictionary<string, BitmapSource[]> Gas = new();

    private sealed class Star
    {
        public double A, R, Spd, Wa, Wf, Wp, Tf, Tp, Tpow, S, Lag, Gain, Flash, Dx, Dy, X, Y;
        public int C;
        public bool Drift;
    }
    private readonly Star[] _stars;
    private readonly Random _rnd = new();
    private readonly List<(double T0, double K)> _waves = new();
    private readonly List<(Star S, double T0, double Life)> _flares = new();
    private bool _hooked, _armed = true;
    private double _t, _level, _burst = 1, _mix = 1, _beatPh, _rot, _spread = 0.45;
    private TimeSpan _last;
    private string _scheme = "space";
    private string? _prev;
    private readonly Color[] _starCol = new Color[4];

    public bool Active { get; set; }
    /// <summary>Fade the sides into the page (when it sits in a narrow column).</summary>
    public bool SoftSides { get; set; } = true;
    public double Energy { get; private set; }
    public Color[] Colors { get; set; } = Array.Empty<Color>();
    public event Action? Frame;

    /// <summary>space | aurora | nova | ice — cross-fades when it changes.</summary>
    public string Scheme
    {
        get => _scheme;
        set { if (value == _scheme || !Schemes.ContainsKey(value)) return; _prev = _scheme; _scheme = value; _mix = 0; }
    }

    public HeroNebula()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        double Rn(double a, double b) => a + _rnd.NextDouble() * (b - a);
        _stars = Enumerable.Range(0, 420).Select(_ =>
        {
            var arm = _rnd.Next(3); var r = Math.Pow(_rnd.NextDouble(), 0.75);
            return new Star
            {
                A = arm * 2.09 + r * 5 + Rn(-0.6, 0.6), R = r, Spd = Rn(0.4, 1.8) * (_rnd.NextDouble() < 0.15 ? -0.6 : 1),
                Wa = Rn(0.005, 0.035), Wf = Rn(0.2, 1.4), Wp = Rn(0, 6.28), Tf = Rn(0.4, 3.2), Tp = Rn(0, 6.28), Tpow = Rn(1, 6),
                S = Math.Pow(_rnd.NextDouble(), 2.2) * 2 + 0.5, C = _rnd.Next(4), Drift = _rnd.NextDouble() < 0.33,
                Dx = Rn(-1, 1), Dy = Rn(-1, 1), Lag = Rn(0, 0.18), Gain = Rn(0.3, 1.3),
            };
        }).ToArray();
        var s0 = Schemes["space"].Stars.Select(Hex).ToArray();
        Array.Copy(s0, _starCol, 4);
        IsVisibleChanged += (_, _) => Hook(IsVisible);
        Loaded += (_, _) => Hook(IsVisible);
        Unloaded += (_, _) => Hook(false);
    }

    public void Burst() => _burst = 0;

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
        _t += dt;
        // «is it playing» eases over seconds, so a skip or a pause never jerks: the swirl's speed, and the
        // spread — at rest the stars gather into a loose cluster, with music they open up
        _level += ((Active ? 1 : 0) - _level) * Math.Min(1, dt / 2.2);
        _spread += ((Active ? 1 : 0.42) - _spread) * Math.Min(1, dt / 3.2);
        _rot += dt * (0.55 + _level * 0.45);
        _mix = Math.Min(1, _mix + dt / 1.4);
        // a soft beat while the wave plays (the real spectrum lives in the full player)
        _beatPh = _t * 118 / 60 % 1;
        var kick = Math.Exp(-_beatPh * 7);
        Energy = Active ? kick * 0.8 : 0;
        if (Active && kick > 0.9 && _armed) { _armed = false; _waves.Add((_t, (int)(_t * 118 / 60) % 4 == 0 ? 1 : 0.55)); }
        if (kick < 0.5) _armed = true;
        if (Active && _beatPh * 2 % 1 < dt * 4) for (var i = 0; i < 18; i++) _stars[_rnd.Next(_stars.Length)].Flash = 0.6 + _rnd.NextDouble() * 0.4;
        _waves.RemoveAll(w => _t - w.T0 > 2.8);
        if (!App.Hub.Settings.Look.ReducedMotion && _rnd.NextDouble() < dt * (0.5 + _level * 1.5)) _flares.Add((_stars[_rnd.Next(_stars.Length)], _t, 0.6 + _rnd.NextDouble() * 0.8));
        _flares.RemoveAll(f => _t - f.T0 > f.Life);
        var target = Schemes[_scheme].Stars.Select(Hex).ToArray();
        for (var i = 0; i < 4; i++) _starCol[i] = Lerp(_starCol[i], target[i], 0.05);
        if (_burst < 1) _burst = Math.Min(1, _burst + dt / 0.9);
        Frame?.Invoke();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double cx = w * 0.5, cy = h * 0.42, R = Math.Sqrt(w * w + h * h) * 0.6, t = _t;
        // no background of its own: the page shows through and the edges melt into it

        // gas: three layers, each drifting, turning and breathing on its own; old scheme fades out
        var pulse = _waves.Sum(q => Math.Max(0, 1 - (t - q.T0) * 1.6) * q.K);
        (double rot, double sx, double ox, double oy, double a)[] specs =
        {
            (t * 0.012, 1.25, Math.Sin(t * 0.07) * 0.08, Math.Cos(t * 0.05) * 0.06, 1),
            (-t * 0.018 + 1.7, 1.05, Math.Cos(t * 0.09 + 1) * 0.12, Math.Sin(t * 0.06 + 2) * 0.1, 0.85),
            (t * 0.007 + 3.1, 1.55, Math.Sin(t * 0.04 + 4) * 0.1, Math.Sin(t * 0.08) * 0.08, 0.7),
        };
        void DrawGas(string key, double alpha)
        {
            var layers = Layers(key);
            for (var i = 0; i < layers.Length; i++)
            {
                var sp = specs[i];
                var size = R * 2 * sp.sx * (0.5 + _spread * 0.5) * (1 + Math.Sin(t * (0.11 + i * 0.05) + i) * 0.06 + pulse * 0.03);
                var tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(1, 0.78));
                tg.Children.Add(new RotateTransform(sp.rot * 180 / Math.PI));
                tg.Children.Add(new TranslateTransform(cx + sp.ox * w, cy + sp.oy * h));
                dc.PushTransform(tg);
                dc.PushOpacity(Math.Min(1, sp.a * alpha * (0.85 + pulse * 0.25)));
                dc.DrawImage(layers[i], new Rect(-size / 2, -size / 2, size, size));
                dc.Pop(); dc.Pop();
            }
        }
        if (_prev is not null && _mix < 1) DrawGas(_prev, 1 - _mix);
        DrawGas(_scheme, _mix);

        // stars, grouped by colour and brightness so the brushes can be shared
        var brushes = new SolidColorBrush[4, 6];
        for (var c = 0; c < 4; c++) for (var k = 0; k < 6; k++) { var b = new SolidColorBrush(Color.FromArgb((byte)(60 + k * 39), _starCol[c].R, _starCol[c].G, _starCol[c].B)); b.Freeze(); brushes[c, k] = b; }
        foreach (var p in _stars)
        {
            double px, py, rr;
            var own = Math.Pow(_spread, 0.6 + (p.Tpow - 1) / 5 * 0.9);      // each star gathers in by its own amount
            var churn = (1 - _spread) * R * 0.05;                            // and the cluster slowly churns
            if (p.Drift)
            {
                p.Dx += Math.Sin(p.Dy * 3 + t * 0.12) * 0.016 * 0.012; p.Dy += Math.Cos(p.Dx * 3 - t * 0.09) * 0.016 * 0.012;
                double fx = (p.A * 0.15 + t * 0.005 * p.Spd + p.Dx * 0.05) % 1, fy = (p.R + t * 0.0025 * p.Spd + p.Dy * 0.05) % 1;
                px = (fx + 1) % 1 * w; py = (fy + 1) % 1 * h;
                px = cx + (px - cx) * (0.25 + own * 0.75); py = cy + (py - cy) * (0.25 + own * 0.75);
            }
            else
            {
                var ang = p.A + _rot * 0.012 * p.Spd * (1.7 - p.R);
                rr = p.R * R * 0.9 * (0.3 + own * 0.7) * (1 + Math.Sin(t * p.Wf * 0.6 + p.Wp) * p.Wa * 3);
                px = cx + Math.Cos(ang) * rr + Math.Sin(t * p.Wf * 0.8 + p.Wp) * p.Wa * w * (0.4 + own * 0.6);
                py = cy + Math.Sin(ang) * rr * 0.75 + Math.Cos(t * p.Wf * 0.6 + p.Wp) * p.Wa * h * 0.5 * (0.4 + own * 0.6);
            }
            px += Math.Sin(t * 0.21 + p.Wp * 3) * churn; py += Math.Cos(t * 0.17 + p.Tp * 2) * churn * 0.8;
            rr = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            var bright = 0.25 + 0.75 * Math.Pow((Math.Sin(t * p.Tf + p.Tp) + 1) / 2, p.Tpow);
            foreach (var (t0, k) in _waves)
            {
                var age = t - t0 - p.Lag; if (age < 0) continue;
                var dd = rr - age * R * 0.55;
                var g = Math.Exp(-(dd * dd) / (2 * Math.Pow(R * 0.04, 2))) * k * p.Gain * Math.Max(0, 1 - age / 2.4);
                bright += g; px += (px - cx) / (rr + 1) * g * R * 0.02; py += (py - cy) / (rr + 1) * g * R * 0.02;
            }
            bright += p.Flash; p.Flash *= 0.88;
            var a = Math.Min(1, bright);
            p.X = px; p.Y = py;
            var rad = p.S * (0.6 + a * 0.7);
            dc.DrawEllipse(brushes[p.C, (int)Math.Clamp(a * 5.99, 0, 5)], null, new Point(px, py), rad, rad);
        }
        // flares: a bright star with four soft rays
        foreach (var (s, t0, life) in _flares)
        {
            var k = Math.Sin(Math.PI * (t - t0) / life);
            var c = _starCol[s.C];
            var len = 10 + 14 * k;
            var glow = new RadialGradientBrush(Color.FromArgb((byte)(230 * k), 255, 255, 255), Color.FromArgb(0, c.R, c.G, c.B));
            dc.DrawEllipse(glow, null, new Point(s.X, s.Y), len * 0.6, len * 0.6);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(150 * k), c.R, c.G, c.B)), 1);
            dc.DrawLine(pen, new Point(s.X - len, s.Y), new Point(s.X + len, s.Y));
            dc.DrawLine(pen, new Point(s.X, s.Y - len), new Point(s.X, s.Y + len));
        }
        // fade into the page at the bottom, and keep the title readable
        var bg = (TryFindResource("Bg") as SolidColorBrush)?.Color ?? Colors_.Black;
        var shade = new LinearGradientBrush(Color.FromArgb(0, bg.R, bg.G, bg.B), Color.FromArgb(0xE0, bg.R, bg.G, bg.B), 90) { StartPoint = new Point(0, 0.55), EndPoint = new Point(0, 1) };
        dc.DrawRectangle(shade, null, new Rect(0, 0, w, h));
        // and melt into the page at the sides
        var side = SoftSides ? Math.Min(90, w * 0.14) : 0;
        var l = new LinearGradientBrush(Color.FromArgb(0xFF, bg.R, bg.G, bg.B), Color.FromArgb(0, bg.R, bg.G, bg.B), 0);
        var rgt = new LinearGradientBrush(Color.FromArgb(0, bg.R, bg.G, bg.B), Color.FromArgb(0xFF, bg.R, bg.G, bg.B), 0);
        dc.DrawRectangle(l, null, new Rect(0, 0, side, h));
        dc.DrawRectangle(rgt, null, new Rect(w - side, 0, side, h));
        if (_burst < 1)
        {
            var k = Ease.OutExpo(_burst);
            var c0 = _starCol[0];
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)((1 - _burst) * 200), c0.R, c0.G, c0.B)), 2), new Point(cx, cy), 60 + k * 260, (60 + k * 260) * 0.75);
        }
    }

    // ---------- gas textures (built once per scheme)

    private static BitmapSource[] Layers(string key)
    {
        if (Gas.TryGetValue(key, out var l)) return l;
        var d = Schemes[key];
        l = new[] { Cloud(d.Seed, d.Fog), Cloud(d.Seed + 46, d.Fog.Reverse().ToArray()), Cloud(d.Seed + 80, new[] { d.Fog[0], d.Fog[3], d.Fog[2] }) };
        Gas[key] = l;
        return l;
    }

    private static BitmapSource Cloud(int seed, string[] cols, int size = 120)
    {
        var n = Noise(seed); var m = Noise(seed * 7 + 3);
        var C = cols.Select(Hex).ToArray();
        var px = new byte[size * size * 4];
        for (var j = 0; j < size; j++)
            for (var i = 0; i < size; i++)
            {
                double u = (double)i / size, v = (double)j / size, rr = Math.Sqrt((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5)) * 2;
                double wx = Fbm(n, u * 3 + 5, v * 3) * 2, wy = Fbm(n, u * 3, v * 3 + 9) * 2, dens = Fbm(n, u * 3 + wx, v * 3 + wy), edge = Math.Max(0, 1 - rr);
                var al = Math.Clamp(Math.Max(0, dens - 0.38) * 3.4 * edge * edge, 0, 1);
                var hue = Math.Clamp(Fbm(m, u * 2 + wx * 0.5, v * 2) * 1.6 - 0.3, 0, 0.999) * (C.Length - 1);
                var i0 = (int)hue; var fr = hue - i0; var a0 = C[i0]; var a1 = C[Math.Min(C.Length - 1, i0 + 1)];
                var k = (j * size + i) * 4;
                // premultiplied BGRA
                px[k] = (byte)((a0.B + (a1.B - a0.B) * fr) * al);
                px[k + 1] = (byte)((a0.G + (a1.G - a0.G) * fr) * al);
                px[k + 2] = (byte)((a0.R + (a1.R - a0.R) * fr) * al);
                px[k + 3] = (byte)(al * 255);
            }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, px, size * 4);
        bmp.Freeze();
        return bmp;
    }

    private static Func<double, double, double> Noise(int seed)
    {
        var r = new Random(seed);
        var p = Enumerable.Range(0, 256).OrderBy(_ => r.Next()).ToArray();
        var P = new int[512];
        for (var i = 0; i < 512; i++) P[i] = p[i & 255];
        var g = Enumerable.Range(0, 256).Select(_ => r.NextDouble()).ToArray();
        double F(double t) => t * t * (3 - 2 * t);
        double V(int x, int y) => g[P[(P[x & 255] + y) & 255]];
        return (x, y) =>
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double xf = F(x - xi), yf = F(y - yi), A = V(xi, yi), B = V(xi + 1, yi), Cc = V(xi, yi + 1), D = V(xi + 1, yi + 1);
            return A + (B - A) * xf + (Cc - A) * yf + (A - B - Cc + D) * xf * yf;
        };
    }

    private static double Fbm(Func<double, double, double> n, double x, double y)
    {
        double s = 0, a = 0.5, f = 1;
        for (var o = 0; o < 4; o++) { s += n(x * f, y * f) * a; f *= 2.03; a *= 0.5; }
        return s;
    }

    private static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
    private static Color Lerp(Color a, Color b, double k) => Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
}
