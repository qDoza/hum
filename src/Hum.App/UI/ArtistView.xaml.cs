using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hum.App.Models;
using Hum.App.Motion;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>A Yandex artist's page: big photo with parallax, monthly listeners, popular tracks, all tracks.</summary>
public partial class ArtistView : UserControl
{
    private static Hub H => App.Hub;
    private static readonly CoverConverter Covers = new();
    private string _id = "", _name = "";
    private int _req;
    private List<Track> _popular = new(), _all = new();

    public ArtistView()
    {
        InitializeComponent();
        BackBtn.Foreground = Brushes.White;
        BackBtn.Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)), CornerRadius = new CornerRadius(18), Child = Icons.Make("back", 18) };
        BackBtn.Click += (_, _) => Close();
        PlayBtn.Click += (_, _) => { var l = Tracks(); if (l.Count > 0) H.Player.PlayList(l, 0, PlayContext.Artist, _name); };
        WaveBtn.Click += (_, _) => _ = H.Player.PlayArtistWaveAsync(_id, _name);
        Rows.Attach(Popular, Tracks, PlayContext.Artist, () => _name);
        Rows.Attach(All, Tracks, PlayContext.Artist, () => _name);
        // the lists sit inside the page's scroll: show them whole and let the wheel scroll the page
        foreach (var l in new[] { Popular, All })
        {
            VirtualizingPanel.SetIsVirtualizing(l, false);
            l.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                Scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = Scroll });
            };
        }
        // the photo drifts slower than the list
        Scroll.ScrollChanged += (_, _) => PhotoShift.Y = Scroll.VerticalOffset * 0.4;
        SizeChanged += (_, _) => Shade.Clip = new RectangleGeometry(new Rect(RenderSize), 14, 14);
        Look.Changed += PaintFade;
        PaintFade();
    }

    private List<Track> Tracks() => _popular.Concat(_all).ToList();

    private void PaintFade()
    {
        var bg = ((SolidColorBrush)FindResource("Bg")).Color;
        Fade.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x59, 0, 0, 0), 0), new GradientStop(Colors.Transparent, 0.3),
                new GradientStop(Colors.Transparent, 0.45), new GradientStop(Color.FromArgb(0xC0, bg.R, bg.G, bg.B), 0.8), new GradientStop(bg, 1),
            },
        };
    }

    public async void Open(string id, string name)
    {
        _id = id; _name = name;
        var req = ++_req;
        Visibility = Visibility.Visible;
        NameText.Text = name;
        SubText.Text = "";
        PhotoImg.ImageSource = null;
        Popular.ItemsSource = null; All.ItemsSource = null;
        AllTitle.Visibility = Visibility.Collapsed;
        Status.Text = "Загружаю…";
        Scroll.ScrollToVerticalOffset(0);
        var tr = new TranslateTransform(40, 0);
        RenderTransform = tr;
        Animator.Tween(0.45, t => { var e = Ease.OutExpo(t); Opacity = e; tr.X = 40 * (1 - e); });

        try
        {
            if (H.Library.Ya is not { } ya) throw new InvalidOperationException("Яндекс не подключён");
            var info = await ya.ArtistAsync(id);
            if (req != _req) return;
            if (info.Name.Length > 0) { _name = info.Name; NameText.Text = info.Name; }
            SubText.Text = info.Sub;
            if (info.PhotoUrl is { } u)
            {
                PhotoImg.ImageSource = Covers.Convert(u, typeof(ImageSource), "900", null!) as ImageSource;
                Animator.Tween(0.9, t => { var e = Ease.OutExpo(t); Photo.Opacity = e; PhotoScale.ScaleX = PhotoScale.ScaleY = Ease.Lerp(1.08, 1, e); });
            }
            H.Library.Mark(info.Popular);
            H.Library.Mark(info.All);
            _popular = info.Popular; _all = info.All;
            Popular.ItemsSource = _popular;
            All.ItemsSource = _all;
            AllTitle.Visibility = _all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Status.Text = _popular.Count + _all.Count == 0 ? "Треков нет" : "";
        }
        catch (Exception e)
        {
            if (req != _req) return;
            Log.Write("artist: " + e.Message);
            Status.Text = "Не загрузилось: " + e.Message;
        }
    }

    public void Close()
    {
        var tr = RenderTransform as TranslateTransform ?? new TranslateTransform();
        RenderTransform = tr;
        Animator.Tween(0.25, t => { Opacity = 1 - Ease.OutCubic(t); tr.X = 40 * Ease.OutCubic(t); }, () => Visibility = Visibility.Collapsed);
    }
}
