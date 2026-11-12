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
            DriftAurora();
        };
        Look.Changed += Paint;
        Hero.SizeChanged += (_, _) => DriftAurora();
    }

    public object? Top { get => TopSlot.Content; set => TopSlot.Content = value; }

    // ------------------------------------------------------------------ colour

    private string _mood = "mywave";

    /// <summary>Resting: the colours of the chosen mood. Playing here: the cover's. A fixed accent wins over both.</summary>
    private Color[] CurrentPalette()
    {
        var L = H.Settings.Look;
        var p = H.Player;
        if (L.Accent is not ("mono" or "cover")) return Palette.Triad(Look.Accent);
        var playingHere = p.IsWave && p.ContextSource == CurrentSource && p.IsPlaying;
        if (playingHere || L.Accent == "cover") return Palette.Triad(Look.CoverColor);
        if (p.Context == PlayContext.Mix && p.MixId is { } id && Palette.Moods.ContainsKey(id)) return Palette.Moods[id];
        return Palette.Moods.GetValueOrDefault(_mood, Palette.Moods["mywave"]);
    }

    private Color[] _pal = Palette.Moods["mywave"];

    private void Paint()
    {
        _pal = CurrentPalette();
        Rings.Colors = _pal;
        Brush Cloud(Color c) { var b = new RadialGradientBrush(Color.FromArgb(0xA0, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)); b.Freeze(); return b; }
        Blob1.Fill = Cloud(_pal[0]);
        Blob2.Fill = Cloud(_pal[_pal.Length / 2]);
        Blob3.Fill = Cloud(_pal[^1]);
        HeroPlay.Resources["Accent"] = Palette.Gradient(_pal);
        HeroPlay.Resources["OnAccent"] = Brushes.Black;
    }

    private void DriftAurora()
    {
        double w = Hero.ActualWidth, h = Hero.ActualHeight, t = Environment.TickCount64 / 1000.0;
        if (w == 0) return;
        void Place(FrameworkElement e, double bx, double by, double sp, double ph)
        {
            Canvas.SetLeft(e, w * bx - e.Width / 2 + Math.Sin(t * sp + ph) * w * 0.08);
            Canvas.SetTop(e, h * by - e.Height / 2 + Math.Cos(t * sp * 0.8 + ph) * h * 0.08);
        }
        Place(Blob1, 0.32, 0.38, 0.35, 0);
        Place(Blob2, 0.68, 0.45, 0.28, 2);
        Place(Blob3, 0.5, 0.72, 0.22, 4);
    }

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

        void AddChip(string title, Action click, Func<bool> on, string? mood = null)
        {
            object content = title;
            if (mood is not null && Palette.Moods.TryGetValue(mood, out var mp))
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Fill = Palette.Gradient(mp) });
                sp.Children.Add(new TextBlock { Text = title });
                content = sp;
            }
            var b = new RadioButton { Content = content, Style = (Style)FindResource("ChipToggle"), GroupName = "wchips" + GetHashCode() };
            if (mood is not null && Palette.Moods.TryGetValue(mood, out var mp2)) { b.Resources["Accent"] = Palette.Gradient(mp2); b.Resources["OnAccent"] = Brushes.Black; b.Click += (_, _) => _mood = mood; }
            b.Click += (_, _) => click();
            Press.SetEnabled(b, true);
            ChipRow.Children.Add(b);
            _marks.Add((b, on));
            Rise(b, i++ * 0.03);
        }
        void AddMix(string title, Action click, Func<bool> on, string? mixId = null)
        {
            var idx = MixRow.Children.Count;
            var disc = new Border { Width = 70, Height = 70, CornerRadius = new CornerRadius(35), Background = Discs.For(mixId) ?? Discs.Get(idx), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
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
            AddChip("Любое", () => { Rings.Burst(); _ = p.PlayMyWaveAsync(Source.Yandex); }, () => p.Context == PlayContext.MyWave && p.ContextSource == Source.Yandex, "mywave");
            foreach (var m in PlayerService.Mixes.Where(m => m.Group == "Настроение" && m.Id != "custom"))
                AddChip(m.Title, () => { H.Settings.Mix = m.Id; H.Settings.Save(); _ = p.PlayMixAsync(m.Id); }, () => p.Context == PlayContext.Mix && p.MixId == m.Id, m.Id);
            foreach (var m in PlayerService.Mixes.Where(m => m.Group != "Настроение"))
                AddMix(m.Title, () => _ = p.PlayMixAsync(m.Id), () => p.Context == PlayContext.Mix && p.MixId == m.Id, m.Id);
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
        Paint();

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

/// <summary>Colours of «Мой эфир»: one palette per mood, and three related colours around any single colour.</summary>
public static class Palette
{
    private static Color C(string h) => (Color)ColorConverter.ConvertFromString(h);

    public static readonly Dictionary<string, Color[]> Moods = new()
    {
        ["mywave"] = new[] { C("#FF5A2A"), C("#FFD23F"), C("#45E0B0"), C("#58B2FF"), C("#B38CFF") },
        ["active"] = new[] { C("#FFD23F"), C("#FF8A2A"), C("#FF2E63") },
        ["fun"] = new[] { C("#FFE14D"), C("#FF7AB6"), C("#FF4D8D") },
        ["calm"] = new[] { C("#45E0B0"), C("#2BB3D4"), C("#3A6DF0") },
        ["sad"] = new[] { C("#B38CFF"), C("#7B6CF6"), C("#3A4A9F") },
    };

    public static Brush Gradient(Color[] cs)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < cs.Length; i++) b.GradientStops.Add(new GradientStop(cs[i], i / (double)Math.Max(1, cs.Length - 1)));
        b.Freeze();
        return b;
    }

    /// <summary>Lighter, itself, deeper and hue-shifted: a palette from one colour (the cover's or the accent).</summary>
    public static Color[] Triad(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn, l = (mx + mn) / 2;
        double h = 0;
        if (d > 0) h = mx == r ? (g - b) / d % 6 : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = (h * 60 + 360) % 360;
        var s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        s = Math.Clamp(s, 0.55, 1);
        Color Hsl(double H, double S, double L)
        {
            double cc = (1 - Math.Abs(2 * L - 1)) * S, x = cc * (1 - Math.Abs(H / 60 % 2 - 1)), m = L - cc / 2;
            var (a, bb, c2) = H < 60 ? (cc, x, 0.0) : H < 120 ? (x, cc, 0.0) : H < 180 ? (0.0, cc, x) : H < 240 ? (0.0, x, cc) : H < 300 ? (x, 0.0, cc) : (cc, 0.0, x);
            return Color.FromRgb((byte)((a + m) * 255), (byte)((bb + m) * 255), (byte)((c2 + m) * 255));
        }
        return new[] { Hsl((h + 25) % 360, s, 0.72), Hsl(h, s, 0.6), Hsl((h + 320) % 360, s, 0.45) };
    }
}

/// <summary>The round mix covers: a few drawn patterns, like the iPhone version.</summary>
public static class Discs
{
    private static readonly Brush[] Set = Build();

    public static Brush Get(int i) => Set[i % Set.Length];

    private static readonly Dictionary<string, Brush> Art = BuildArt();

    /// <summary>A cover that looks like the mix's name, when there is one.</summary>
    public static Brush? For(string? mixId) => mixId is not null && Art.TryGetValue(mixId, out var b) ? b : null;

    private static Dictionary<string, Brush> BuildArt()
    {
        Color C(string h) => (Color)ColorConverter.ConvertFromString(h);
        Brush Lin(Point a, Point b, params (string c, double o)[] st) { var br = new LinearGradientBrush { StartPoint = a, EndPoint = b }; foreach (var (c, o) in st) br.GradientStops.Add(new GradientStop(C(c), o)); br.Freeze(); return br; }
        Brush Rad(params (string c, double o)[] st) { var br = new RadialGradientBrush(); foreach (var (c, o) in st) br.GradientStops.Add(new GradientStop(C(c), o)); br.Freeze(); return br; }
        Brush Draw(string bg, Action<DrawingGroup> paint) { var g = new DrawingGroup(); g.Children.Add(new GeometryDrawing(new SolidColorBrush(C(bg)), null, new RectangleGeometry(new Rect(0, 0, 100, 100)))); paint(g); var br = new DrawingBrush(g); br.Freeze(); return br; }
        Point P(double x, double y) => new(x, y);
        return new Dictionary<string, Brush>
        {
            ["rain"] = Draw("#0B1A33", g =>
            {
                g.Children.Add(new GeometryDrawing(Lin(P(0, 0), P(0.3, 1), ("#1D4E89", 0), ("#0B1A33", 1)), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
                foreach (var (x, y) in new[] { (30.0, 25.0), (65.0, 50.0), (45.0, 78.0), (75.0, 22.0), (20.0, 60.0) })
                    g.Children.Add(new GeometryDrawing(new SolidColorBrush(C("#9FD3FF")), null, new EllipseGeometry(P(x, y), 3, 4.5)));
            }),
            ["party"] = Lin(P(0, 0), P(1, 1), ("#FF3CAC", 0), ("#784BA0", 0.5), ("#2B86C5", 1)),
            ["focus"] = Draw("#08302C", g => { for (var k = 1; k <= 7; k++) g.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(C("#2DE0C8")), 1.6), new EllipseGeometry(P(50, 50), k * 7, k * 7))); }),
            ["drive"] = Lin(P(0, 0), P(0, 1), ("#FFB347", 0), ("#FF6A3D", 0.45), ("#3A1D4F", 0.46), ("#140B1C", 1)),
            ["evening"] = Rad(("#FFCF7A", 0), ("#B8661B", 0.55), ("#2A1406", 1)),
            ["dig"] = Lin(P(0, 0), P(1, 1), ("#C8F53C", 0), ("#1F6B3A", 0.6), ("#0B1F12", 1)),
            ["ru"] = Lin(P(0, 0), P(0, 1), ("#F2F2F2", 0), ("#F2F2F2", 0.33), ("#3A6DF0", 0.33), ("#3A6DF0", 0.66), ("#E5383B", 0.66), ("#E5383B", 1)),
            ["popular"] = Lin(P(0, 1), P(1, 0), ("#FF5CA8", 0), ("#FFB43D", 1)),
            ["favorite"] = Draw("#12050A", g => g.Children.Add(new GeometryDrawing(new SolidColorBrush(C("#FF4D6D")), null, Geometry.Parse("M50,74 C28,60 22,48 22,39 C22,30 29,24 37,24 C43,24 47,27 50,32 C53,27 57,24 63,24 C71,24 78,30 78,39 C78,48 72,60 50,74 Z")))),
            ["discover"] = Lin(P(0, 0), P(1, 1), ("#58B2FF", 0), ("#45E0B0", 0.5), ("#C8F53C", 1)),
            ["foreign"] = Rad(("#7FD1FF", 0), ("#1F5FA8", 0.55), ("#0A1F3A", 1)),
            ["instrumental"] = Draw("#2B2116", g => { for (var x = 6; x < 100; x += 12) g.Children.Add(new GeometryDrawing(new SolidColorBrush(C("#F2E6C9")), null, new RectangleGeometry(new Rect(x, 0, 5, 100)))); }),
            ["rusnew"] = Lin(P(0, 0), P(1, 1), ("#3A6DF0", 0), ("#E5383B", 1)),
        };
    }

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
    public Color[] Colors { get; set; } = Palette.Moods["mywave"];
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
        var c = Colors[0];
        var e = Energy;
        var r0 = Math.Min(w, h) * 0.36 * (1 + e * 0.04);
        for (var k = 0; k < 5; k++)
        {
            var alpha = 0.9 - k * 0.15;
            // a gradient across the palette whose direction turns slowly, ring by ring
            var ang = _t * 0.35 + k * 0.9;
            var br = new LinearGradientBrush { StartPoint = new Point(0.5 - Math.Cos(ang) / 2, 0.5 - Math.Sin(ang) / 2), EndPoint = new Point(0.5 + Math.Cos(ang) / 2, 0.5 + Math.Sin(ang) / 2), Opacity = alpha };
            for (var i = 0; i < Colors.Length; i++) br.GradientStops.Add(new GradientStop(Colors[i], i / (double)Math.Max(1, Colors.Length - 1)));
            var pen = new Pen(br, k == 0 ? 2.2 : 1.2);
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
