using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hum.App.Motion;
using Hum.App.UI;

namespace Hum.App;

/// <summary>The tray flyout: opens out of the tray icon exactly like SonyControl's.</summary>
public partial class FlyoutWindow : Window
{
    private bool _closing, _pinned;
    private int _tab;
    private DateTime _lastAutoClose = DateTime.MinValue;
    private TweenHandle? _clipTween, _toastTween;
    private Point _origin;
    private double _maxRadius;
    private readonly Button[] _tabs;

    public event Action? OpenWindowRequested;
    public bool IsClosing => _closing;

    public FlyoutWindow()
    {
        InitializeComponent();
        _tabs = new[] { TabWave, TabLikes, TabSearch };
        Wave.Top = new NowPlaying { Variant = NowPlayingVariant.Card };
        Mini.Variant = NowPlayingVariant.Mini;

        TabWave.Click += (_, _) => ShowTab(0, true);
        TabLikes.Click += (_, _) => ShowTab(1, true);
        TabSearch.Click += (_, _) => ShowTab(2, true);
        Wave.AllLikesRequested += () => ShowTab(1, true);
        OpenButton.Click += (_, _) => { CloseFlyout(); OpenWindowRequested?.Invoke(); };
        PinButton.Click += (_, _) =>
        {
            _pinned = !_pinned;
            PinButton.Content = _pinned ? "" : "";
            PinButton.ToolTip = _pinned ? "Закрывать при клике мимо" : "Не закрывать окно";
        };

        Deactivated += (_, _) => { if (!_pinned && IsVisible && !_closing && !OwnedWindows.Cast<Window>().Any()) { _lastAutoClose = DateTime.Now; CloseFlyout(); } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseFlyout(); };
        App.Hub.Toast += ShowToast;
        Loaded += (_, _) => ShowTab(_tab, false);
    }

    // ================================================================== open / close

    public void Toggle(System.Drawing.Point cursorPx)
    {
        // the click that deactivated the window also lands here: don't reopen it immediately
        if ((DateTime.Now - _lastAutoClose).TotalMilliseconds < 350) return;
        if (IsVisible && !_closing) CloseFlyout();
        else OpenFlyout(cursorPx);
    }

    public void OpenFlyout(System.Drawing.Point cursorPx)
    {
        _closing = false;
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width + 2;
        Top = wa.Bottom - Height + 2;
        Opacity = 1;
        Show();
        Activate();
        UpdateLayout();

        // a circle grows out of the tray icon (or the window corner if the cursor is elsewhere)
        _origin = Root.PointFromScreen(new Point(cursorPx.X, cursorPx.Y));
        var nearTray = _origin.Y > Root.ActualHeight * 0.5 && _origin.Y < Root.ActualHeight + 120
                       && _origin.X > -120 && _origin.X < Root.ActualWidth + 120;
        if (!nearTray) _origin = new Point(Root.ActualWidth * 0.85, Root.ActualHeight + 20);
        var corners = new[] { new Point(0, 0), new Point(Root.ActualWidth, 0), new Point(0, Root.ActualHeight), new Point(Root.ActualWidth, Root.ActualHeight) };
        _maxRadius = corners.Max(c => (c - _origin).Length) + 30;

        var clip = new EllipseGeometry(_origin, 14, 14);
        Root.Clip = clip;
        _clipTween?.Cancel();
        _clipTween = Animator.Tween(0.95, t =>
        {
            clip.RadiusX = clip.RadiusY = Ease.Lerp(14, _maxRadius, Ease.OutQuart(t));
            var y = t < 0.55 ? Ease.Lerp(22, -3, Ease.OutCubic(t / 0.55)) : Ease.Lerp(-3, 0, Ease.InOutCubic((t - 0.55) / 0.45));
            var s = t < 0.55 ? Ease.Lerp(0.94, 1.006, Ease.OutCubic(t / 0.55)) : Ease.Lerp(1.006, 1, Ease.InOutCubic((t - 0.55) / 0.45));
            RootShift.Y = y;
            RootScale.ScaleX = RootScale.ScaleY = s;
            Root.Opacity = Ease.Lerp(0.5, 1, Ease.OutCubic(Math.Min(1, t * 1.8)));
        }, () => Root.Clip = null);

        // the current page pours in
        var page = CurrentPage();
        var tr = new TranslateTransform(0, 10);
        page.RenderTransform = tr;
        page.Opacity = 0;
        Animator.Tween(1.0, t => { var e = Ease.OutExpo(t); page.Opacity = e; tr.Y = 10 * (1 - e); }, delay: 0.16);

        ShowTab(_tab, animate: false);
        if (_tab == 2) Search.FocusQuery();
    }

    public void CloseFlyout()
    {
        if (_closing || !IsVisible) return;
        _closing = true;
        var clip = new EllipseGeometry(_origin, _maxRadius, _maxRadius);
        Root.Clip = clip;
        _clipTween?.Cancel();
        _clipTween = Animator.Tween(0.52, t =>
        {
            var e = Ease.InOutCubic(t);
            clip.RadiusX = clip.RadiusY = Ease.Lerp(_maxRadius, 14, e);
            RootShift.Y = 8 * e;
            RootScale.ScaleX = RootScale.ScaleY = 1 - 0.02 * e;
            Root.Opacity = 1 - 0.6 * e;
        }, () =>
        {
            Hide();
            _closing = false;
            Root.Clip = null;
        });
    }

    // ================================================================== tabs

    private FrameworkElement CurrentPage() => _tab switch { 1 => Likes, 2 => Search, _ => WaveHost };

    private void ShowTab(int tab, bool animate)
    {
        var prev = CurrentPage();
        var dir = tab > _tab ? 1 : -1;
        _tab = tab;
        var show = CurrentPage();
        Tabs.Paint(_tabs, _tabs[tab]);
        Tabs.Place(TabHost, TabBlob, _tabs[tab], animate);
        Mini.Visibility = tab == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (tab == 2 && animate) Search.FocusQuery();

        foreach (var p in new FrameworkElement[] { WaveHost, Likes, Search })
            if (p != show && p != prev) p.Visibility = Visibility.Collapsed;
        if (!animate || prev == show)
        {
            prev.Visibility = prev == show ? Visibility.Visible : Visibility.Collapsed;
            show.Visibility = Visibility.Visible;
            show.Opacity = 1;
            return;
        }
        show.Visibility = Visibility.Visible;
        var showShift = new TranslateTransform(); show.RenderTransform = showShift;
        var hideShift = new TranslateTransform(); prev.RenderTransform = hideShift;
        Animator.Tween(0.55, t =>
        {
            var e = Ease.OutExpo(t);
            show.Opacity = e;
            showShift.X = 24 * dir * (1 - e);
            prev.Opacity = 1 - Math.Min(1, t * 2.5);
            hideShift.X = -16 * dir * Ease.OutCubic(Math.Min(1, t * 2));
        }, () => { if (CurrentPage() != prev) prev.Visibility = Visibility.Collapsed; });
    }

    // ================================================================== toast

    private void ShowToast(string text)
    {
        if (!IsVisible) return;
        ToastText.Text = text;
        _toastTween?.Cancel();
        _toastTween = Animator.Tween(3.2, t => Toast.Opacity = t < 0.1 ? t / 0.1 : t > 0.85 ? (1 - t) / 0.15 : 1);
    }
}
