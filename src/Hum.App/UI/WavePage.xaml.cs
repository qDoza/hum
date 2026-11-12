using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hum.App.Models;
using Hum.App.Motion;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>
/// «Эфир» in the iPhone design: a living ring around «Мой эфир» with a big play button, mood chips,
/// round mixes and recent likes. The pad, character and SoundCloud filters sit behind the tune button.
/// </summary>
public partial class WavePage : UserControl
{
    private static Hub H => App.Hub;
    private bool _sync, _tuneOpen;
    private readonly List<(FrameworkElement El, Func<bool> On)> _marks = new();
    private readonly List<RadioButton> _scBase = new(), _scGenre = new();
    private int _builtFor = -1;

    /// <summary>«Все» next to recent likes.</summary>
    public event Action? AllLikesRequested;

    public WavePage()
    {
        InitializeComponent();
        Icons.Set_(TuneBtn, "tune", 17);

        // SoundCloud filters inside the tune box
        var baseRow = AddGroup("Основа", ScGroups);
        foreach (var b in PlayerService.ScBases)
        {
            var chip = new RadioButton { Content = b.Title, Tag = b.Id, Style = (Style)FindResource("ChipToggle"), GroupName = "scbase" };
            chip.Checked += (_, _) => { if (_sync || H.Settings.ScBase == b.Id) return; H.Settings.ScBase = b.Id; H.Player.ScFiltersChanged(); };
            _scBase.Add(chip);
            baseRow.Children.Add(chip);
        }
        void Flag(System.Windows.Controls.Primitives.ToggleButton tb, Action<bool> set) =>
            tb.Click += (_, _) => { set(tb.IsChecked == true); H.Player.ScFiltersChanged(); Refresh(); };
        Flag(ScNoSets, v => H.Settings.ScNoSets = v);
        Flag(ScOnlyNew, v => H.Settings.ScOnlyNew = v);
        Flag(ScFresh, v => H.Settings.ScFresh = v);

        SrcYa.Checked += (_, _) => SetSource("ya");
        SrcSc.Checked += (_, _) => SetSource("sc");
        DivFav.Checked += (_, _) => SetDiversity("favorite");
        DivAny.Checked += (_, _) => SetDiversity("default");
        DivNew.Checked += (_, _) => SetDiversity("discover");

        HeroPlay.Click += (_, _) =>
        {
            var p = H.Player;
            if (p.IsWave && p.ContextSource == CurrentSource && p.Current is not null) p.Toggle();
            else { Rings.Burst(); _ = p.PlayMyWaveAsync(CurrentSource); }
        };
        TuneBtn.Click += (_, _) => ToggleTune();
        ConnectButton.Click += (_, _) => H.Connect(CurrentSource);
        AllLikes.Click += (_, _) => AllLikesRequested?.Invoke();
        Rows.Attach(Recent, () => H.Library.Liked.Take(6).ToList(), PlayContext.Likes, "Любимое");

        Pad.Set(H.Settings.PadX, H.Settings.PadY);
        Pad.Committed += (x, y) =>
        {
            H.Settings.PadX = x; H.Settings.PadY = y; H.Settings.Mix = "custom"; H.Settings.Save();
            _ = H.Player.PlayMixAsync("custom");
        };

        H.Player.Changed += Refresh;
        H.Library.Changed += () => { _builtFor = -1; Refresh(); };
        H.Library.Liked.CollectionChanged += (_, _) => FillRecent();
        H.SourceChanged += () => { _builtFor = -1; Refresh(); };
        Loaded += (_, _) => { _builtFor = -1; Refresh(); };
        // the name breathes while the wave of this source plays
        Rings.Frame += () =>
        {
            var s = 1 + Rings.Energy * 0.03;
            NameScale.ScaleX = NameScale.ScaleY = s;
        };
    }

    public object? Top { get => TopSlot.Content; set => TopSlot.Content = value; }

    private Source CurrentSource => H.Settings.Source == "sc" ? Source.SoundCloud : Source.Yandex;

    // ------------------------------------------------------------------ building

    private Panel AddGroup(string caption, Panel into)
    {
        into.Children.Add(new TextBlock { Text = caption.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 12, 0, 8) });
        var row = new WrapPanel();
        into.Children.Add(row);
        return row;
    }

    /// <summary>Chips and mixes depend on the source and on the service's «занятия»: rebuilt when either changes.</summary>
    private void Build()
    {
        var src = CurrentSource;
        var key = (src == Source.Yandex ? 1000 : 2000) + H.Library.Activities.Count;
        if (key == _builtFor) return;
        _builtFor = key;
        _marks.Clear();
        ChipRow.Children.Clear();
        MixRow.Children.Clear();
        var p = H.Player;
        var i = 0;

        void AddChip(string title, Action click, Func<bool> on)
        {
            var b = new RadioButton { Content = title, Style = (Style)FindResource("ChipToggle"), GroupName = "wchips" + GetHashCode() };
            b.Click += (_, _) => click();
            Press.SetEnabled(b, true);
            ChipRow.Children.Add(b);
            _marks.Add((b, on));
            Rise(b, i++ * 0.03);
        }
        void AddMix(string title, Action click, Func<bool> on)
        {
            var idx = MixRow.Children.Count;
            var disc = new Border { Width = 70, Height = 70, CornerRadius = new CornerRadius(35), Background = Discs.Get(idx), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
            var label = new TextBlock { Text = title, FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0), Foreground = (Brush)FindResource("TextMain"), MaxWidth = 84 };
            var stack = new StackPanel { Width = 84 };
            stack.Children.Add(disc);
            stack.Children.Add(label);
            var b = new Button { Style = (Style)FindResource("Bare"), Content = stack, Margin = new Thickness(0, 0, 10, 0), Tag = disc };
            b.Click += (_, _) => click();
            Press.SetEnabled(b, true);
            MixRow.Children.Add(b);
            _marks.Add((b, on));
            Rise(b, 0.05 + idx * 0.04);
        }

        if (src == Source.Yandex)
        {
            AddChip("Любое", () => { Rings.Burst(); _ = p.PlayMyWaveAsync(Source.Yandex); }, () => p.Context == PlayContext.MyWave && p.ContextSource == Source.Yandex);
            foreach (var m in PlayerService.Mixes.Where(m => m.Group == "Настроение" && m.Id != "custom"))
                AddChip(m.Title, () => { H.Settings.Mix = m.Id; H.Settings.Save(); _ = p.PlayMixAsync(m.Id); }, () => p.Context == PlayContext.Mix && p.MixId == m.Id);
            foreach (var m in PlayerService.Mixes.Where(m => m.Group != "Настроение"))
                AddMix(m.Title, () => _ = p.PlayMixAsync(m.Id), () => p.Context == PlayContext.Mix && p.MixId == m.Id);
            foreach (var (name, seed) in H.Library.Activities)
                AddMix(name, () => _ = p.PlayActivityAsync(name, seed), () => p.Context == PlayContext.Mix && p.MixId == "act:" + seed);
            MixTitle.Text = "Миксы";
        }
        else
        {
            foreach (var g in PlayerService.ScGenres)
                AddChip(g.Title, () => { H.Settings.ScGenre = g.Query; H.Settings.Save(); _ = p.PlayMyWaveAsync(Source.SoundCloud); }, () => H.Settings.ScGenre == g.Query);
            foreach (var b in PlayerService.ScBases)
                AddMix(b.Title, () => { H.Settings.ScBase = b.Id; H.Settings.ScGenre = null; H.Settings.Save(); _ = p.PlayMyWaveAsync(Source.SoundCloud); }, () => H.Settings.ScBase == b.Id && H.Settings.ScGenre is null);
            MixTitle.Text = "Основа эфира";
        }
        MixCount.Text = MixRow.Children.Count.ToString();
        FillRecent();
    }

    private void FillRecent() => Recent.ItemsSource = H.Library.Liked.Take(6).ToList();

    /// <summary>An element rises into place a moment after the page appears.</summary>
    private static void Rise(FrameworkElement el, double delay)
    {
        var tr = new TranslateTransform(0, 12);
        el.RenderTransform = tr;
        el.Opacity = 0;
        var wait = delay;
        Animator.Tween(0.5 + delay, t =>
        {
            var k = Math.Clamp((t * (0.5 + delay) - wait) / 0.5, 0, 1);
            var e = Ease.OutExpo(k);
            el.Opacity = e;
            tr.Y = 12 * (1 - e);
        });
    }

    // ------------------------------------------------------------------ actions

    private void ToggleTune()
    {
        _tuneOpen = !_tuneOpen;
        TuneBtn.Foreground = _tuneOpen ? (Brush)FindResource("Accent") : (Brush)FindResource("TextMain");
        if (_tuneOpen)
        {
            TuneBox.Visibility = Visibility.Visible;
            var tr = new TranslateTransform(0, -8);
            TuneBox.RenderTransform = tr;
            Animator.Tween(0.4, t => { var e = Ease.OutExpo(t); TuneBox.Opacity = e; tr.Y = -8 * (1 - e); });
        }
        else Animator.Tween(0.2, t => TuneBox.Opacity = 1 - t, () => { if (!_tuneOpen) TuneBox.Visibility = Visibility.Collapsed; });
    }

    private void SetSource(string s)
    {
        if (_sync || H.Settings.Source == s) return;
        H.Settings.Source = s;
        H.Settings.Save();
        H.RaiseSourceChanged();
    }

    private void SetDiversity(string d)
    {
        if (_sync || H.Settings.Diversity == d) return;
        H.Settings.Diversity = d;
        H.Settings.Save();
        if (H.Player.Context == PlayContext.Mix && H.Player.MixId == "custom") _ = H.Player.PlayMixAsync("custom");
    }

    private static readonly Dictionary<string, string> DivLabel = new() { ["favorite"] = "Знакомое", ["default"] = "Любое", ["discover"] = "Новое" };

    private void Refresh()
    {
        _sync = true;
        var src = CurrentSource;
        var p = H.Player;
        var st = H.Settings;
        SrcYa.IsChecked = src == Source.Yandex;
        SrcSc.IsChecked = src == Source.SoundCloud;
        DivFav.IsChecked = st.Diversity == "favorite";
        DivAny.IsChecked = st.Diversity == "default";
        DivNew.IsChecked = st.Diversity == "discover";

        var connected = H.Library.Has(src);
        ConnectBox.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        WaveBox.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ConnectTitle.Text = src == Source.Yandex ? "Яндекс не подключён" : "SoundCloud не подключён";
        ConnectText.Text = src == Source.Yandex
            ? "Войдите через Яндекс ID — появятся «Мой эфир», миксы и ваши лайки."
            : "Войдите в SoundCloud — лайки попадут в общее «Любимое», а эфир соберётся из похожих треков.";
        if (connected) Build();

        var isYa = src == Source.Yandex;
        YaTune.Visibility = isYa ? Visibility.Visible : Visibility.Collapsed;
        ScBox.Visibility = isYa ? Visibility.Collapsed : Visibility.Visible;
        foreach (var c in _scBase) c.IsChecked = (string)c.Tag == st.ScBase;
        ScNoSets.IsChecked = st.ScNoSets;
        ScOnlyNew.IsChecked = st.ScOnlyNew;
        ScFresh.IsChecked = st.ScFresh;
        ScHint.Text = p.Context == PlayContext.MyWave && p.ContextSource == Source.SoundCloud ? "применяются сразу" : "для «Мой эфир»";
        Pad.Active = p.Context == PlayContext.Mix && p.MixId == "custom";

        // hero: what plays, and the big button
        var mine = p.IsWave && p.ContextSource == src && p.Current is not null;
        HeroSub.Text = p.IsBusy && p.IsWave && p.ContextSource == src ? "Подбираю…"
            : mine ? p.ContextTitle
            : isYa ? $"Яндекс · {DivLabel.GetValueOrDefault(st.Diversity, "Любое")}"
            : $"SoundCloud · {(st.ScGenre is { } g ? PlayerService.ScGenres.FirstOrDefault(x => x.Query == g)?.Title : PlayerService.ScBases.FirstOrDefault(x => x.Id == st.ScBase)?.Title)}";
        Icons.Set_(HeroPlay, mine && p.IsPlaying ? "pause" : "play", 26);
        Rings.Active = mine && p.IsPlaying;

        // chips and discs follow what plays
        foreach (var (el, on) in _marks)
        {
            var isOn = on();
            if (el is RadioButton rb) rb.IsChecked = isOn;
            else if (el is Button { Tag: Border disc })
            {
                disc.BorderBrush = isOn ? (Brush)FindResource("Accent") : Brushes.Transparent;
                Discs.Spin(disc, isOn && p.IsPlaying);
            }
        }
        _sync = false;
    }
}

/// <summary>The round mix covers: a few drawn patterns, like the iPhone version.</summary>
public static class Discs
{
    private static readonly Brush[] Set = Build();

    public static Brush Get(int i) => Set[i % Set.Length];

    private static Brush[] Build()
    {
        Color C(string h) => (Color)ColorConverter.ConvertFromString(h);
        Brush Radial(params (string c, double o)[] stops)
        {
            var b = new RadialGradientBrush();
            foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(C(c), o));
            b.Freeze(); return b;
        }
        Brush Linear(double angle, params (string c, double o)[] stops)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(Math.Cos(angle * Math.PI / 180) * 0.5 + 0.5, Math.Sin(angle * Math.PI / 180) * 0.5 + 0.5) };
            foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(C(c), o));
            b.Freeze(); return b;
        }
        Brush Rings(string a, string b2, int n)
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(C(b2)), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
            for (var k = n; k > 0; k--)
                g.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(C(a)), 2.2), new EllipseGeometry(new Point(50, 50), k * 48.0 / n, k * 48.0 / n)));
            var br = new DrawingBrush(g); br.Freeze(); return br;
        }
        Brush Stripes(string a, string b2)
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(C(b2)), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
            for (var x = 4; x < 100; x += 11) g.Children.Add(new GeometryDrawing(new SolidColorBrush(C(a)), null, new RectangleGeometry(new Rect(x, 0, 4, 100))));
            var br = new DrawingBrush(g); br.Freeze(); return br;
        }
        return new[]
        {
            Rings("#D9D9D9", "#141414", 5),
            Radial(("#3B3A36", 0), ("#121212", 1)),
            Linear(30, ("#5A6B7A", 0), ("#1A1F26", 0.45), ("#8796A3", 0.7), ("#1A1F26", 1)),
            Stripes("#E9E9E9", "#151515"),
            Radial(("#C7A35A", 0), ("#7A3B2E", 0.55), ("#1C1410", 1)),
            Linear(160, ("#2B2B2B", 0), ("#2B2B2B", 0.4), ("#EDEDED", 0.42), ("#0E0E0E", 0.44), ("#0E0E0E", 1)),
            Radial(("#4F6B4A", 0), ("#4F6B4A", 0.25), ("#203020", 0.27), ("#0D140D", 1)),
            Linear(45, ("#6D5A8A", 0), ("#1D1726", 0.7)),
            Rings("#BFBFBF", "#161616", 9),
            Linear(90, ("#E6E6E6", 0), ("#151515", 0.5), ("#8A8A8A", 1)),
        };
    }

    private static readonly Dictionary<Border, TweenHandle?> Spinning = new();

    /// <summary>The disc of the mix that plays turns slowly, like a record.</summary>
    public static void Spin(Border disc, bool on)
    {
        if (disc.RenderTransform is not RotateTransform rot) return;
        if (on == Spinning.ContainsKey(disc)) return;
        if (on)
        {
            Spinning[disc] = null;
            Animator.Run(dt => { rot.Angle = (rot.Angle + dt * 50) % 360; return Spinning.ContainsKey(disc); });
        }
        else Spinning.Remove(disc);
    }
}

/// <summary>The living ring behind «Мой эфир»: soft rings that drift, pulse and swell while the wave plays.</summary>
public sealed class HeroRings : FrameworkElement
{
    private bool _hooked;
    private double _level, _burst = 1, _t;
    private TimeSpan _last;

    public bool Active { get; set; }
    public double Energy { get; private set; }
    public event Action? Frame;

    public HeroRings()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Hook(IsVisible);
        Loaded += (_, _) => Hook(IsVisible);
        Unloaded += (_, _) => Hook(false);
        Look.Changed += InvalidateVisual;
    }

    /// <summary>A ripple out of the ring (the wave starts).</summary>
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
        _level += ((Active ? 1 : 0) - _level) * 0.05;
        _t += dt;
        // a soft beat while the wave plays (the real spectrum lives in the full player)
        var ph = _t * 112 / 60 % 1;
        Energy = _level * (Math.Exp(-ph * 6) * 0.8 + 0.2);
        if (_burst < 1) _burst = Math.Min(1, _burst + dt / 0.9);
        Frame?.Invoke();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, cx = w / 2, cy = h / 2;
        var c = Look.Accent;
        var e = Energy;
        var r0 = Math.Min(w, h) * 0.36 * (1 + e * 0.04);
        for (var k = 0; k < 5; k++)
        {
            var alpha = 0.6 - k * 0.11;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B)), k == 0 ? 1.7 : 1);
            pen.Freeze();
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var r = r0 * (1 + k * 0.07) + e * k * 5;
                var amp = (3 + e * 12) * (1 + k * 0.3);
                for (var i = 0; i <= 180; i++)
                {
                    var a = i / 180.0 * Math.PI * 2;
                    var n = Math.Sin(a * 3 + _t * 0.8 + k * 1.3) * 0.5 + Math.Sin(a * 5 - _t * 1.2 + k * 2.1) * 0.32 + Math.Sin(a * 2 + _t * 0.45 + k) * 0.42;
                    var pt = new Point(cx + Math.Cos(a) * (r + n * amp), cy + Math.Sin(a) * (r + n * amp));
                    if (i == 0) ctx.BeginFigure(pt, false, true); else ctx.LineTo(pt, true, true);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        if (_burst < 1)
        {
            var k = Ease.OutExpo(_burst);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)((1 - _burst) * 200), c.R, c.G, c.B)), 2);
            dc.DrawEllipse(null, pen, new Point(cx, cy), 60 + k * 220, 60 + k * 220);
        }
    }
}
