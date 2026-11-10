using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hum.App.Models;
using Hum.App.Motion;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>Big cover with the track's lyrics; synced lyrics follow the music and a line click seeks to it.</summary>
public partial class CoverView : UserControl
{
    private static Hub H => App.Hub;
    private static readonly CoverConverter Covers = new();
    private static readonly Regex LrcLine = new(@"^\s*\[(\d+):(\d+(?:[.,]\d+)?)\]\s?(.*)$");

    private readonly List<(double Ms, TextBlock Block)> _timed = new();
    private string? _trackKey;
    private int _req, _active = -1;

    public event Action? CloseRequested;

    public CoverView()
    {
        InitializeComponent();
        Player.Variant = NowPlayingVariant.Card;
        CloseBtn.Click += (_, _) => CloseRequested?.Invoke();
        H.Player.Changed += OnTrack;
        H.Player.Progress += () => Follow();
        LyricsScroll.SizeChanged += (_, _) => { Pad(); Follow(force: true); };
    }

    private bool _synced;

    /// <summary>Synced lyrics get half a viewport of space above and below, so even the first and last line can stand in the middle.</summary>
    private void Pad()
    {
        var h = LyricsScroll.ActualHeight;
        var half = Math.Max(0, h / 2 - 18);
        Lines.Margin = _synced ? new Thickness(0, half, 0, half) : new Thickness(0, 24, 0, 48);

        // fade top and bottom of the visible area. Absolute mapping: a relative brush would stretch over the
        // whole (much taller) lyrics and dim lines that are actually in the middle of the view
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

    public void Open()
    {
        Visibility = Visibility.Visible;
        _trackKey = null;
        OnTrack();
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
        Animator.Tween(0.25, t => Opacity = 1 - Ease.OutCubic(t), () => { Visibility = Visibility.Collapsed; done?.Invoke(); });
    }

    private async void OnTrack()
    {
        if (Visibility != Visibility.Visible) return;
        var t = H.Player.Current;
        Title.Text = t?.Title ?? "Ничего не играет";
        Artist.Text = t is null ? "" : t.Album is { Length: > 0 } a ? $"{t.Artist} · {a}" : t.Artist;
        Tag.Text = t?.SourceTag ?? "—";
        Cover.ImageSource = t?.BigCoverUrl is { } u ? Covers.Convert(u, typeof(ImageSource), "680", null!) as ImageSource : null;
        if (t?.Key == _trackKey) return;
        _trackKey = t?.Key;

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

    private void ShowEmpty(string text)
    {
        NoLyricsText.Text = text;
        NoLyrics.Visibility = Visibility.Visible;
        LyricsScroll.Visibility = Visibility.Collapsed;
    }

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
                Foreground = synced ? (Brush)FindResource("Dim") : new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
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
        // positions are known only after layout
        Dispatcher.BeginInvoke(() => Follow(force: true), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Highlights the current line of synced lyrics and keeps it in the vertical middle.</summary>
    private void Follow(bool force = false)
    {
        if (_timed.Count == 0 || Visibility != Visibility.Visible) return;
        var pos = H.Player.PositionMs + 250;
        var idx = _timed.FindLastIndex(x => x.Ms <= pos);
        if (idx == _active && !force) return;
        var changed = idx != _active;
        _active = idx;
        for (var i = 0; i < _timed.Count; i++)
            _timed[i].Block.Foreground = (Brush)FindResource(i == idx ? "TextMain" : i < idx ? "Muted" : "Dim");

        // before the first line: keep the first one in the middle, waiting
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
