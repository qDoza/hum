using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hum.App.Models;
using Hum.App.Motion;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>
/// The full player: smoked glass lit by the cover, the cover inside a live visualizer, track info with
/// artist links and tags, controls, «Эфир по вайбу», and the lyrics on the right (synced ones follow the music).
/// </summary>
public partial class CoverView : UserControl
{
    private static Hub H => App.Hub;
    private static readonly CoverConverter Covers = new();
    private static readonly Regex LrcLine = new(@"^\s*\[(\d+):(\d+(?:[.,]\d+)?)\]\s?(.*)$");

    private readonly List<(double Ms, TextBlock Block)> _timed = new();
    private readonly AudioMeter _meter = new();
    private string? _trackKey;
    private int _req, _active = -1;
    private bool _synced, _lyricsOpen;
    private int _rtab;                       // 0 Дальше, 1 Похожее, 2 Недавнее
    private string? _similarKey, _artistKey;
    private List<Track> _similar = new();
    private double _spin;

    public event Action? CloseRequested;
    /// <summary>A Yandex artist name was clicked: (artist id, name).</summary>
    public event Action<string, string>? ArtistRequested;

    public CoverView()
    {
        InitializeComponent();
        Viz.Meter = _meter;
        CloseBtn.Click += (_, _) => CloseRequested?.Invoke();
        Icons.Set_(CloseBtn, "down", 18);
        Icons.Set_(PrevBtn, "prev", 22);
        Icons.Set_(NextBtn, "next", 22);
        Icons.Set_(RepeatBtn, "repeat", 18);
        LyricsIcon.Content = Icons.Make("lyrics", 15);
        BuildTabs();
        QueueList.MouseLeftButtonUp += OnQueueClick;
        ArtistCard.Click += (_, _) => { if (_artistRef is { } a) ArtistRequested?.Invoke(a.Id, a.Name); };
        BackToQueue.Click += (_, _) => SetLyrics(false);
        RightPane.SizeChanged += (_, _) => LayoutPages();
        // scroll down past the list → lyrics; scroll up at the top of the lyrics → back
        QueuePage.PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta < 0 && H.Player.Current is { HasLyrics: true } && AtEnd(QueueList)) { SetLyrics(true); e.Handled = true; }
        };
        LyricsPage.PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta > 0 && (LyricsScroll.Visibility != Visibility.Visible || LyricsScroll.VerticalOffset <= 1)) { SetLyrics(false); e.Handled = true; }
        };

        var p = H.Player;
        PlayBtn.Click += (_, _) => p.Toggle();
        PrevBtn.Click += (_, _) => p.Prev();
        NextBtn.Click += (_, _) => p.Next();
        LeftBtn.Click += (_, _) => { if (p.IsWave) _ = p.DislikeAsync(); else p.ShuffleRest(); };
        RepeatBtn.Click += (_, _) => { p.Repeat = !p.Repeat; H.RaiseToast(p.Repeat ? "Повтор трека включён" : "Повтор выключен"); };
        LikeBtn.Click += (_, _) => { if (p.Current is { } t) { _ = H.Library.ToggleLikeAsync(t); Viz.Kick = 1; } };
        VibeBtn.Click += (_, _) => _ = p.StartVibeAsync();
        LyricsBtn.Click += (_, _) => SetLyrics(!_lyricsOpen);
        Seek.Committed += f => p.Seek(f);
        Vol.Committed += v => p.Volume = v;
        Vol.Dragging += v => p.Volume = v;
        MuteBtn.Click += (_, _) => { if (p.Volume > 0.001) { _unmute = p.Volume; p.Volume = 0; } else p.Volume = _unmute > 0 ? _unmute : 0.6; };
        p.VolumeChanged += RenderVolume;
        // double click on the cover likes the track
        CoverBox.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && p.Current is { } t && !H.Library.IsLiked(t)) { _ = H.Library.ToggleLikeAsync(t); Viz.Kick = 1; } };

        p.Changed += OnTrack;
        p.Changed += () => { if (Visibility == Visibility.Visible) RefreshQueue(); };
        p.Progress += () => { Follow(); RenderProgress(); };
        H.Library.Changed += RenderButtons;
        LyricsScroll.SizeChanged += (_, _) => { Pad(); Follow(force: true); };
        Stage.SizeChanged += (_, _) => LayoutStage();
        Root.SizeChanged += (_, _) => { LayoutLight(); Shade.Clip = new RectangleGeometry(new Rect(Root.RenderSize), 14, 14); };
        Viz.Frame += Animate;
        Look.Changed += () => { ApplyLook(); RenderButtons(); };
        ApplyLook();
    }

    private double _unmute = 0.6;

    // ------------------------------------------------------------------ open / close

    public void Open()
    {
        Visibility = Visibility.Visible;
        _trackKey = null;
        OnTrack();
        RenderVolume();
        RenderProgress();
        _meter.Start();
        Opacity = 0;
        Animator.Tween(0.45, t =>
        {
            var e = Ease.OutExpo(t);
            Opacity = e;
            BodyScale.ScaleX = BodyScale.ScaleY = Ease.Lerp(0.97, 1, e);
        });
    }

    public void Close(Action? done = null)
    {
        Animator.Tween(0.25, t => Opacity = 1 - Ease.OutCubic(t), () => { Visibility = Visibility.Collapsed; _meter.Stop(); done?.Invoke(); });
    }

    // ------------------------------------------------------------------ look

    private void ApplyLook()
    {
        var L = H.Settings.Look;
        var square = L.Cover == "square";
        Viz.Square = square;
        LayoutStage();
        Glow1.Visibility = Glow2.Visibility = L.PlayerBg == "black" ? Visibility.Collapsed : Visibility.Visible;
        Smoke.Fill = new SolidColorBrush(L.PlayerBg switch
        {
            "black" => Color.FromRgb(5, 5, 5),
            "vivid" => Color.FromArgb(0x4D, 10, 10, 12),
            _ => L.Contrast == "high" ? Color.FromArgb(0xBD, 4, 4, 6) : Color.FromArgb(0x94, 10, 10, 12),
        });
        var ac = Look.Accent;
        VibeIcon.Background = new SolidColorBrush(Color.FromArgb(0x38, ac.R, ac.G, ac.B));
        VibeRing.Stroke = VibeWave.Stroke = new SolidColorBrush(ac);
    }

    private void LayoutStage()
    {
        var size = Math.Clamp(Math.Min(Stage.ActualWidth, Stage.ActualHeight) * 0.56, 120, 300);
        CoverBox.Width = CoverBox.Height = size;
        var r = H.Settings.Look.Cover == "square" ? 16 : size / 2;
        CoverShadow.CornerRadius = CoverImg.CornerRadius = new CornerRadius(r);
        Viz.CoverRadius = size / 2;
    }

    private void LayoutLight()
    {
        double w = Root.ActualWidth, h = Root.ActualHeight;
        Canvas.SetLeft(Glow1, w * 0.3 - Glow1.Width / 2); Canvas.SetTop(Glow1, h * 0.35 - Glow1.Height / 2);
        Canvas.SetLeft(Glow2, w * 0.7 - Glow2.Width / 2); Canvas.SetTop(Glow2, h * 0.75 - Glow2.Height / 2);
    }

    /// <summary>Every frame: the cover bounces and spins, the light breathes with the bass.</summary>
    private void Animate()
    {
        var L = H.Settings.Look;
        var lo = Viz.Low; var hit = Viz.Hit;
        var s = 1 + lo * 0.07 + hit * 0.05;
        CoverScale.ScaleX = CoverScale.ScaleY = s;
        if (L.Spin && L.Cover != "square") _spin = (_spin + 0.15 * Viz.Level) % 360; else _spin *= 0.9;
        CoverRot.Angle = _spin + Math.Sin(Environment.TickCount64 / 600.0) * Viz.Mid * 2;
        var sh = !L.ReducedMotion && hit > 0.55 ? (hit - 0.55) * 9 : 0;
        CoverShake.X = sh > 0 ? (Random.Shared.NextDouble() - 0.5) * sh : 0;
        CoverShake.Y = sh > 0 ? (Random.Shared.NextDouble() - 0.5) * sh : 0;
        var gk = L.PlayerBg == "vivid" ? 1.5 : 1;
        Glow1.Opacity = Math.Min(1, (0.35 + lo * 0.4 + hit * 0.15) * gk);
        Glow1Scale.ScaleX = Glow1Scale.ScaleY = 4.2 * (1 + lo * 0.08);
        Glow2.Opacity = Math.Min(1, (0.25 + Viz.High * 0.35) * gk);
        if (!L.ReducedMotion)
        {
            Glow1Rot.Angle = (Environment.TickCount64 / 1000.0 * 14) % 360;
            Glow2Rot.Angle = -(Environment.TickCount64 / 1000.0 * 10) % 360;
        }
    }

    // ------------------------------------------------------------------ track

    private void OnTrack()
    {
        if (Visibility != Visibility.Visible) return;
        var p = H.Player;
        var t = p.Current;
        Ctx.Text = p.ContextTitle.ToUpper(CultureInfo.CurrentCulture);
        RenderButtons();
        if (t?.Key == _trackKey) return;
        _trackKey = t?.Key;

        Title.Text = t?.Title ?? "Ничего не играет";
        BuildArtists(t);
        BuildTags(t);
        var big = t?.BigCoverUrl is { } u ? Covers.Convert(u, typeof(ImageSource), "680", null!) as ImageSource : null;
        Cover.ImageSource = big;
        var small = t?.CoverUrl is { } su ? Tiny(su) : null;
        Glow1Img.ImageSource = Glow2Img.ImageSource = small;
        TintFrom(t);
        Animator.Tween(0.5, x => { var e = Ease.OutExpo(x); Title.Opacity = e; Artists.Opacity = e; });
        LoadLyrics(t);
        if (_lyricsOpen && t?.HasLyrics != true) SetLyrics(false);
        RefreshQueue();
    }

/// <summary>The cover at 8 px: scaled up smoothly it becomes the soft light behind the glass (the shared cache keeps bigger copies).</summary>
    private static ImageSource? Tiny(string url)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit(); img.UriSource = new Uri(url); img.DecodePixelWidth = 8; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
            return img;
        }
        catch { return null; }
    }

    private void RenderButtons()
    {
        var p = H.Player;
        var t = p.Current;
        Icons.Set_(PlayBtn, p.IsPlaying ? "pause" : "play", 26);
        Icons.Set_(LikeBtn, t is not null && H.Library.IsLiked(t) ? "heart-fill" : "heart", 22);
        Icons.Set_(LeftBtn, p.IsWave ? "dislike" : "shuffle", 18);
        LeftBtn.ToolTip = p.IsWave ? "Не нравится" : "Перемешать";
        RepeatBtn.Foreground = p.Repeat ? new SolidColorBrush(Look.Accent) : Brushes.White;
        LikeBtn.Foreground = t is not null && H.Library.IsLiked(t) ? new SolidColorBrush(Look.Accent) : Brushes.White;
        var vibeOn = t is not null && p.Context == PlayContext.Vibe && p.VibeKey == t.Key;
        VibeSub.Text = t is null ? "Включите трек" : vibeOn ? "Играет: похожее на этот трек" : $"Похожее по звучанию на «{t.Title}»";
        VibeGoText.Text = vibeOn ? "Играет" : "Запустить";

    }

    private void RenderProgress()
    {
        if (Visibility != Visibility.Visible) return;
        var p = H.Player;
        var d = p.DurationMs;
        if (!Seek.IsDragging) Seek.Value = d > 0 ? p.PositionMs / d : 0;
        Pos.Text = Track.FormatTime(p.PositionMs);
        Dur.Text = Track.FormatTime(d);
    }

    private void RenderVolume()
    {
        var v = H.Player.Volume;
        if (!Vol.IsDragging) Vol.Value = v;
        Icons.Set_(MuteBtn, v < 0.001 ? "vol0" : v < 0.5 ? "vol1" : "vol2", 16);
    }

    /// <summary>Yandex artists become links to their pages.</summary>
    private void BuildArtists(Track? t)
    {
        Artists.Children.Clear();
        if (t is null) return;
        var muted = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255));
        if (t.Source == Source.Yandex && t.Artists.Count > 0)
        {
            for (var i = 0; i < t.Artists.Count; i++)
            {
                var a = t.Artists[i];
                var tb = new TextBlock { Text = a.Name, FontSize = 15, Foreground = muted, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline };
                tb.MouseEnter += (_, _) => tb.Foreground = Brushes.White;
                tb.MouseLeave += (_, _) => tb.Foreground = muted;
                tb.MouseLeftButtonUp += (_, _) => ArtistRequested?.Invoke(a.Id, a.Name);
                Artists.Children.Add(tb);
                if (i < t.Artists.Count - 1) Artists.Children.Add(new TextBlock { Text = ", ", FontSize = 15, Foreground = muted });
            }
        }
        else Artists.Children.Add(new TextBlock { Text = t.Artist, FontSize = 15, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis });
        if (t.Album is { Length: > 0 } al) Artists.Children.Add(new TextBlock { Text = " · " + al, FontSize = 15, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 });
    }

    private void BuildTags(Track? t)
    {
        Tags.Children.Clear();
        if (t is null) return;
        Border Pill(string text) => new()
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 6, 6),
            Child = new TextBlock { Text = text, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)) },
        };
        Tags.Children.Add(Pill(t.Source == Source.Yandex ? "Яндекс" : "SoundCloud"));
        if (!string.IsNullOrWhiteSpace(t.Genre)) Tags.Children.Add(Pill(t.Genre!.ToLower(CultureInfo.CurrentCulture)));
        if (t.YearShown is { } y) Tags.Children.Add(Pill(y.ToString(CultureInfo.InvariantCulture)));
        if (t.HasLyrics)
        {
            var on = _lyricsOpen;
            var b = new Button { Style = (Style)FindResource("Bare"), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand };
            var inner = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4),
                Background = on ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
                Child = new TextBlock { Text = "Текст", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = on ? (Brush)FindResource("OnAccent") : Brushes.White },
            };
            b.Content = inner;
            b.Click += (_, _) => SetLyrics(!_lyricsOpen);
            Motion.Press.SetEnabled(b, true);
            Tags.Children.Add(b);
        }
    }

    /// <summary>The cover's dominant vivid colour, for the visualizer and «По обложке».</summary>
    private async void TintFrom(Track? t)
    {
        if (t?.CoverUrl is not { } url) { Look.SetCoverColor(Color.FromRgb(200, 200, 200)); return; }
        try
        {
            var key = t.Key;
            using var http = new System.Net.Http.HttpClient();
            var bytes = await http.GetByteArrayAsync(url);
            if (H.Player.Current?.Key != key) return;
            var img = new BitmapImage();
            img.BeginInit(); img.StreamSource = new System.IO.MemoryStream(bytes); img.DecodePixelWidth = 16; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
            var conv = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, b = 0, wt = 0;
            for (var i = 0; i < px.Length; i += 4)
            {
                double B = px[i], G = px[i + 1], R = px[i + 2];
                var mx = Math.Max(R, Math.Max(G, B)); var mn = Math.Min(R, Math.Min(G, B));
                var sat = mx > 0 ? (mx - mn) / mx : 0;
                var k = 0.05 + sat * sat * (mx / 255);
                r += R * k; g += G * k; b += B * k; wt += k;
            }
            r /= wt; g /= wt; b /= wt;
            // keep the hue, make it bright enough to glow on black
            var m = Math.Max(1, Math.Max(r, Math.Max(g, b))); var f = 235 / m;
            double[] o = { r * f, g * f, b * f };
            var avg = o.Average();
            byte C(double v) => (byte)Math.Clamp(avg + (v - avg) * 1.25, 0, 255);
            Look.SetCoverColor(Color.FromRgb(C(o[0]), C(o[1]), C(o[2])));
        }
        catch (Exception e) { Log.Write("tint: " + e.Message); }
    }

    // ------------------------------------------------------------------ lyrics

    private void SetLyrics(bool open)
    {
        if (_lyricsOpen == open) return;
        _lyricsOpen = open;
        var from = SlideY.Y;
        var to = open ? -RightPane.ActualHeight : 0;
        Animator.Tween(0.55, t => SlideY.Y = Ease.Lerp(from, to, Ease.OutExpo(t)));
        BuildTags(H.Player.Current);
        RenderButtons();
        if (open) Dispatcher.BeginInvoke(() => Follow(force: true), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void LayoutPages()
    {
        var h = Math.Max(100, RightPane.ActualHeight);
        Page1Row.Height = Page2Row.Height = new GridLength(h);
        Slider.Height = h * 2;
        SlideY.Y = _lyricsOpen ? -h : 0;
    }

    // ------------------------------------------------------------------ right side: Дальше / Похожее / Недавнее

    private readonly List<Button> _rtabs = new();
    private ArtistRef? _artistRef;

    private void BuildTabs()
    {
        var names = new[] { "Дальше", "Похожее", "Недавнее" };
        for (var i = 0; i < names.Length; i++)
        {
            var idx = i;
            var b = new Button { Style = (Style)FindResource("Bare"), Margin = new Thickness(0, 0, 6, 0), Content = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 6), Child = new TextBlock { Text = names[i], FontSize = 12 } } };
            Motion.Press.SetEnabled(b, true);
            b.Click += (_, _) => { _rtab = idx; PaintTabs(); RefreshQueue(); };
            _rtabs.Add(b);
            RTabs.Children.Add(b);
        }
        PaintTabs();
    }

    private void PaintTabs()
    {
        for (var i = 0; i < _rtabs.Count; i++)
        {
            var on = i == _rtab;
            var bd = (Border)_rtabs[i].Content;
            bd.Background = on ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
            ((TextBlock)bd.Child).Foreground = on ? (Brush)FindResource("OnAccent") : new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255));
            ((TextBlock)bd.Child).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private async void RefreshQueue()
    {
        var p = H.Player;
        var t = p.Current;
        List<Track> list;
        if (_rtab == 0) list = p.Upcoming(9).ToList();
        else if (_rtab == 2) list = p.Recent.Skip(t is null ? 0 : 1).ToList();
        else
        {
            if (t is not null && _similarKey != t.Key)
            {
                _similarKey = t.Key;
                _similar = new();
                QueueEmpty.Text = "Подбираю похожее…";
                try
                {
                    var key = t.Key;
                    var found = t.Source == Source.Yandex && H.Library.Ya is { } ya ? await ya.SimilarAsync(t)
                              : t.Source == Source.SoundCloud && H.Library.Sc is { } sc ? await sc.RelatedAsync(t) : new List<Track>();
                    if (key != H.Player.Current?.Key) return;
                    H.Library.Mark(found);
                    _similar = found.Take(20).ToList();
                }
                catch (Exception e) { Log.Write("similar: " + e.Message); }
                if (_rtab != 1) return;
            }
            list = _similar;
        }
        QueueList.ItemsSource = list;
        QueueEmpty.Text = list.Count > 0 ? "" : _rtab == 0 ? "Очередь пуста" : _rtab == 1 ? "Похожего не нашлось" : "Здесь появятся треки, которые вы слушали";
        QueueEmpty.Visibility = list.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ScrollHint.Visibility = t?.HasLyrics == true ? Visibility.Visible : Visibility.Collapsed;
        LyricsBtn.Visibility = t?.HasLyrics == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateArtist(t);
    }

    private void OnQueueClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(QueueList, (DependencyObject)e.OriginalSource) is not ListBoxItem item || item.DataContext is not Track t) return;
        var p = H.Player;
        if (p.Current?.Key == t.Key) { p.Toggle(); return; }
        if (_rtab == 0) { p.JumpTo(t); return; }
        var list = ((IEnumerable<Track>)QueueList.ItemsSource).ToList();
        p.PlayList(list, list.IndexOf(t), PlayContext.Search, _rtab == 1 ? "Похожее на «" + (p.Current?.Title ?? "") + "»" : "Недавнее");
    }

    private async void UpdateArtist(Track? t)
    {
        var a = t?.Artists.FirstOrDefault();
        var key = a?.Id ?? t?.Artist;
        if (key == _artistKey) return;
        _artistKey = key;
        _artistRef = a;
        ArtistCard.Visibility = t is null ? Visibility.Collapsed : Visibility.Visible;
        ArtistName.Text = a?.Name ?? t?.Artist ?? "";
        ArtistSub.Text = t?.Source == Source.SoundCloud ? "SoundCloud" : "";
        ArtistPhoto.ImageSource = null;
        ArtistGo.Visibility = a is null ? Visibility.Collapsed : Visibility.Visible;
        if (a is null || H.Library.Ya is not { } ya) return;
        try
        {
            var b = await ya.ArtistBriefAsync(a.Id);
            if (_artistKey != a.Id) return;
            ArtistSub.Text = b.Sub;
            if (b.PhotoUrl is { } u) ArtistPhoto.ImageSource = Covers.Convert(u, typeof(ImageSource), "120", null!) as ImageSource;
        }
        catch (Exception e) { Log.Write("artist card: " + e.Message); }
    }

    /// <summary>True when a list's scroll is at its end (or it does not scroll at all).</summary>
    private static bool AtEnd(DependencyObject root)
    {
        var sv = FindScroll(root);
        return sv is null || sv.VerticalOffset >= sv.ScrollableHeight - 1;
    }

    private static ScrollViewer? FindScroll(DependencyObject d)
    {
        if (d is ScrollViewer s) return s;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindScroll(VisualTreeHelper.GetChild(d, i)) is { } f) return f;
        return null;
    }

    private async void LoadLyrics(Track? t)
    {
        Lines.Children.Clear();
        _timed.Clear();
        _active = -1;
        LyricsScroll.ScrollToVerticalOffset(0);
        if (t is null) { ShowEmpty("Включите что-нибудь"); return; }
        if (t.Source == Source.SoundCloud) { ShowEmpty("SoundCloud не отдаёт тексты песен"); return; }
        if (!t.HasLyrics && !H.Library.Demo) { ShowEmpty("Для этого трека текста нет"); return; }

        ShowEmpty("Загружаю текст…");
        var req = ++_req;
        try
        {
            var lyr = await H.Library.LyricsAsync(t);
            if (req != _req) return;
            if (lyr is not { } l || string.IsNullOrWhiteSpace(l.Text)) { ShowEmpty("Для этого трека текста нет"); return; }
            Build(l.Synced, l.Text);
        }
        catch (Exception e)
        {
            if (req != _req) return;
            Log.Write("lyrics: " + e.Message);
            ShowEmpty("Не удалось загрузить текст");
        }
    }

    /// <summary>Synced lyrics get half a viewport of space above and below, so even the first and last line can stand in the middle.</summary>
    private void Pad()
    {
        var h = LyricsScroll.ActualHeight;
        var half = Math.Max(0, h / 2 - 18);
        Lines.Margin = _synced ? new Thickness(0, half, 0, half) : new Thickness(0, 24, 0, 48);
        var fade = Math.Min(110, h * 0.2);
        LyricsScroll.OpacityMask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, Math.Max(1, h)),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Black, fade / Math.Max(1, h)),
                new GradientStop(Colors.Black, 1 - fade / Math.Max(1, h)),
                new GradientStop(Colors.Transparent, 1),
            },
        };
    }

    private void ShowEmpty(string text)
    {
        NoLyricsText.Text = text;
        NoLyrics.Visibility = Visibility.Visible;
        LyricsScroll.Visibility = Visibility.Collapsed;
    }

    private static readonly Brush LineDim = Frozen(Color.FromArgb(0x52, 255, 255, 255));
    private static readonly Brush LinePast = Frozen(Color.FromArgb(0x80, 255, 255, 255));
    private static readonly Brush LinePlain = Frozen(Color.FromArgb(0xD8, 255, 255, 255));
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void Build(bool synced, string text)
    {
        NoLyrics.Visibility = Visibility.Collapsed;
        LyricsScroll.Visibility = Visibility.Visible;
        _synced = synced;
        Pad();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            double? ms = null;
            var line = raw;
            if (synced)
            {
                var m = LrcLine.Match(raw);
                if (!m.Success) continue;      // tags like [ar:], [ti:]
                ms = int.Parse(m.Groups[1].Value) * 60000 + double.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * 1000;
                line = m.Groups[3].Value;
            }
            var tb = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(line) ? "•" : line,
                FontSize = synced ? 22 : 18,
                FontWeight = synced ? FontWeights.Bold : FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, synced ? 14 : 8),
                Foreground = synced ? LineDim : LinePlain,
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            if (ms is { } at)
            {
                tb.Cursor = Cursors.Hand;
                tb.MouseLeftButtonUp += (_, _) => { var d = H.Player.DurationMs; if (d > 0) H.Player.Seek(at / d); };
                _timed.Add((at, tb));
            }
            Lines.Children.Add(tb);
        }
        Dispatcher.BeginInvoke(() => Follow(force: true), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Highlights the current line of synced lyrics and keeps it in the vertical middle.</summary>
    private void Follow(bool force = false)
    {
        if (_timed.Count == 0 || Visibility != Visibility.Visible || !_lyricsOpen) return;
        var pos = H.Player.PositionMs + 250;
        var idx = _timed.FindLastIndex(x => x.Ms <= pos);
        if (idx == _active && !force) return;
        var changed = idx != _active;
        _active = idx;
        var accent = new SolidColorBrush(Colors.White);
        for (var i = 0; i < _timed.Count; i++)
            _timed[i].Block.Foreground = i == idx ? accent : i < idx ? LinePast : LineDim;

        var cur = _timed[Math.Max(0, idx)].Block;
        if (changed && idx >= 0)
        {
            var st = (ScaleTransform)cur.RenderTransform;
            Animator.Tween(0.35, t => { var s = Ease.Lerp(0.96, 1, Ease.OutBack(t, 1.2)); st.ScaleX = st.ScaleY = s; });
        }
        if (!cur.IsLoaded || LyricsScroll.ActualHeight == 0) return;
        var mid = cur.TransformToAncestor(Lines).Transform(new Point(0, cur.ActualHeight / 2)).Y + Lines.Margin.Top;
        var target = Math.Max(0, mid - LyricsScroll.ActualHeight / 2);
        if (force && !changed) LyricsScroll.ScrollToVerticalOffset(target);
        else SmoothScroll.AnimateTo(LyricsScroll, target);
    }
}
