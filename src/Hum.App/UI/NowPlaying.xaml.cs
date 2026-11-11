using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hum.App.Models;
using Hum.App.Services;

namespace Hum.App.UI;

public enum NowPlayingVariant { Card, Mini, Bar }

/// <summary>What's playing + controls, in three sizes: flyout card, flyout mini strip, window bar.</summary>
public partial class NowPlaying : UserControl
{
    private static Hub H => App.Hub;
    private static readonly CoverConverter Covers = new();

    public NowPlaying()
    {
        InitializeComponent();
        var p = H.Player;
        foreach (var b in new[] { CardPlay, MiniPlay, BarPlay }) b.Click += (_, _) => p.Toggle();
        foreach (var b in new[] { CardNext, MiniNext, BarNext }) b.Click += (_, _) => p.Next();
        foreach (var b in new[] { CardPrev, BarPrev }) b.Click += (_, _) => p.Prev();
        foreach (var b in new[] { CardDislike, BarDislike }) b.Click += (_, _) => _ = p.DislikeAsync();
        foreach (var b in new[] { CardLike, BarLike }) b.Click += (_, _) => { if (p.Current is { } t) _ = H.Library.ToggleLikeAsync(t); };
        foreach (var pl in new[] { CardProgress, BarProgress }) pl.Committed += f => p.Seek(f);
        BarProgress.Dragging += f => BarPos.Text = Track.FormatTime(p.DurationMs * f);
        foreach (var vol in new[] { Volume, CardVolume })
        {
            vol.Dragging += v => p.Volume = v;
            vol.Committed += v => { p.Volume = v; H.Settings.Save(); };
        }
        CardMute.Click += (_, _) =>
        {
            if (p.Volume > 0.001) { _beforeMute = p.Volume; p.Volume = 0; }
            else p.Volume = _beforeMute > 0.001 ? _beforeMute : 0.5;
            H.Settings.Save();
        };
        p.VolumeChanged += RenderVolume;

        // one vector icon set with soft corners instead of mismatched font glyphs
        foreach (var b in new[] { CardNext, BarNext }) Icons.Set_(b, "next", 17);
        foreach (var b in new[] { CardPrev, BarPrev }) Icons.Set_(b, "prev", 17);
        foreach (var b in new[] { CardDislike, BarDislike }) Icons.Set_(b, "dislike", 17);
        Icons.Set_(MiniNext, "next", 15);
        RenderVolume();

        p.Changed += Render;
        p.Progress += RenderProgress;
        H.Library.Changed += Render;
        Loaded += (_, _) => { Render(); RenderProgress(); };
    }

    private double _beforeMute = 0.6;

    private void RenderVolume()
    {
        var v = H.Player.Volume;
        if (!Volume.IsDragging) Volume.Value = v;
        if (!CardVolume.IsDragging) CardVolume.Value = v;
        CardVolText.Text = $"{Math.Round(v * 100)}";
        Icons.Set_(CardMute, v < 0.001 ? "vol0" : v < 0.5 ? "vol1" : "vol2", 16);
    }

    private void OnCoverClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        H.OpenCover();
    }

    private NowPlayingVariant _variant;
    public NowPlayingVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            Card.Visibility = value == NowPlayingVariant.Card ? Visibility.Visible : Visibility.Collapsed;
            Mini.Visibility = value == NowPlayingVariant.Mini ? Visibility.Visible : Visibility.Collapsed;
            Bar.Visibility = value == NowPlayingVariant.Bar ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Render()
    {
        var p = H.Player;
        var t = p.Current;
        var title = t?.Title ?? "Ничего не играет";
        var sub = t is null ? "Нажмите «Мой эфир»" : p.IsBusy && !p.IsPlaying ? "Загружаю…" : t.Artist;
        var img = t?.CoverUrl is { } u ? Covers.Convert(u, typeof(ImageSource), "160", null!) as ImageSource : null;

        CardTitle.Text = MiniTitle.Text = BarTitle.Text = title;
        CardSub.Text = t is null ? sub : $"{sub} · {p.ContextTitle}";
        MiniSub.Text = BarSub.Text = t is null ? "" : sub;
        CardCover.ImageSource = MiniCover.ImageSource = BarCover.ImageSource = img;
        BarTag.Visibility = t is null ? Visibility.Collapsed : Visibility.Visible;
        BarTagText.Text = t?.SourceTag ?? "";

        var play = p.IsPlaying ? "pause" : "play";
        Icons.Set_(CardPlay, play, 18);
        Icons.Set_(BarPlay, play, 17);
        Icons.Set_(MiniPlay, play, 15);

        var liked = t is not null && H.Library.IsLiked(t);
        foreach (var b in new[] { CardLike, BarLike }) Icons.Set_(b, liked ? "heart-fill" : "heart", 18);
        // dislike only makes sense inside a wave
        var wave = p.IsWave;
        CardDislike.Opacity = BarDislike.Opacity = wave ? 1 : 0.35;
        CardDislike.IsEnabled = BarDislike.IsEnabled = wave;
    }

    private void RenderProgress()
    {
        var p = H.Player;
        var dur = p.DurationMs;
        var f = dur > 0 ? p.PositionMs / dur : 0;
        if (!CardProgress.IsDragging) CardProgress.Value = f;
        CardTime.Text = $"{Track.FormatTime(p.PositionMs)} / {Track.FormatTime(dur)}";
        if (!BarProgress.IsDragging)
        {
            BarProgress.Value = f;
            BarPos.Text = Track.FormatTime(p.PositionMs);
        }
        BarDur.Text = Track.FormatTime(dur);
    }
}

