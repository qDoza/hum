using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Hum.App.Models;
using Hum.App.Services;

namespace Hum.App;

public partial class App : Application
{
    public static Hub Hub { get; private set; } = null!;

    private Mutex? _single;
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private MainWindow? _main;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { Log.Write("UI error: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("fatal: " + ex.ExceptionObject);
        Log.Write("start");

        // single instance: launching the exe again just opens the window of the running copy
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Hum.Show");
        _single = new Mutex(true, @"Local\Hum.SingleInstance", out var first);
        if (!first)
        {
            AllowSetForegroundWindow(-1);
            showSignal.Set();
            Shutdown();
            return;
        }
        new Thread(() =>
        {
            while (showSignal.WaitOne())
                Dispatcher.BeginInvoke(() => _main?.BringUp());
        }) { IsBackground = true, Name = "show-signal" }.Start();

        Hub = new Hub(Dispatcher);
        _main = new MainWindow();
        _flyout = new FlyoutWindow();
        _tray = new TrayIcon();

        _tray.Clicked += p => _flyout.Toggle(p);
        _tray.OpenWindowRequested += () => { _flyout.CloseFlyout(); _main.BringUp(); };
        _tray.PlayPauseRequested += () => Hub.Player.Toggle();
        _tray.NextRequested += () => Hub.Player.Next();
        _tray.ExitRequested += Quit;
        _flyout.OpenWindowRequested += () => _main.BringUp();
        Hub.CoverRequested += () => { _flyout.CloseFlyout(); _main.ShowCover(); };
        Hub.Player.Changed += UpdateTray;

        // first launch (nothing connected) or --show: open the window so there is something to click
        var nothingYet = string.IsNullOrEmpty(Hub.Settings.YandexToken) && string.IsNullOrEmpty(Hub.Settings.ScToken);
        if (nothingYet || e.Args.Contains("--window")) _main.BringUp();
        if (e.Args.Contains("--show"))
            _ = Dispatcher.BeginInvoke(() => _flyout.OpenFlyout(new System.Drawing.Point(int.MaxValue / 2, int.MaxValue / 2)),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var snap = Array.IndexOf(e.Args, "--snap");
        if (snap >= 0 && snap + 1 < e.Args.Length) { Snapshot(e.Args[snap + 1]); return; }

        await Hub.Library.StartAsync();
        if (e.Args.Contains("--probe")) await Probe();
    }

    /// <summary>Debug aid: silently checks SoundCloud playback and Yandex lyrics on the real accounts, logs the outcome, quits.</summary>
    private async System.Threading.Tasks.Task Probe()
    {
        var lib = Hub.Library;
        Log.Write($"probe: likes={lib.Liked.Count} ya={lib.Count(Source.Yandex)} sc={lib.Count(Source.SoundCloud)} activities={lib.Activities.Count}");
        await System.Threading.Tasks.Task.Delay(1500);
        Log.Write($"probe: activities={lib.Activities.Count} [{string.Join(", ", lib.Activities.Take(6).Select(a => a.Name))}]");

        var ya = lib.Liked.FirstOrDefault(t => t.Source == Source.Yandex && t.HasLyrics);
        if (ya is not null)
        {
            try { var l = await lib.LyricsAsync(ya); Log.Write($"probe lyrics: synced={l?.Synced} lines={l?.Text.Split('\n').Length}"); }
            catch (Exception ex) { Log.Write("probe lyrics FAILED: " + ex.Message); }
        }
        else Log.Write("probe lyrics: no liked Yandex track with lyrics");

        var sc = lib.Liked.Where(t => t.Source == Source.SoundCloud).Take(3).ToList();
        foreach (var t in sc)
        {
            try
            {
                var url = await lib.StreamUrlAsync(t);
                var mp = new global::Windows.Media.Playback.MediaPlayer { Volume = 0, IsMuted = true };
                var done = new System.Threading.Tasks.TaskCompletionSource<string>();
                mp.PlaybackSession.PlaybackStateChanged += (s, _) => { if (s.PlaybackState == global::Windows.Media.Playback.MediaPlaybackState.Playing) done.TrySetResult("PLAYING"); };
                mp.MediaFailed += (_, a) => done.TrySetResult("FAILED " + a.ErrorMessage);
                mp.Source = global::Windows.Media.Core.MediaSource.CreateFromUri(new Uri(url));
                mp.Play();
                var r = await System.Threading.Tasks.Task.WhenAny(done.Task, System.Threading.Tasks.Task.Delay(12000));
                Log.Write($"probe sc {t.Key}: {(r == done.Task ? done.Task.Result : "TIMEOUT")}");
                mp.Dispose();
            }
            catch (Exception ex) { Log.Write($"probe sc {t.Key}: {ex.Message}"); }
        }
        Quit();
    }

    /// <summary>Debug aid: renders the window and the flyout (every tab) to PNGs, then quits.</summary>
    private async void Snapshot(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        Hub.Library.UseDemo();
        _flyout!.Topmost = false;
        ((Button)_flyout.FindName("PinButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // stay open while the window takes focus
        void Save(System.Windows.FrameworkElement el, string name)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(el);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(el.ActualWidth * dpi.DpiScaleX), (int)(el.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(el);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = System.IO.File.Create(System.IO.Path.Combine(dir, name + ".png"));
            enc.Save(f);
        }
        _main!.BringUp();
        _flyout!.OpenFlyout(new System.Drawing.Point(int.MaxValue / 2, int.MaxValue / 2));
        await System.Threading.Tasks.Task.Delay(1800);
        Save((System.Windows.FrameworkElement)_main.Content, "window_wave");
        Save((System.Windows.FrameworkElement)_flyout.Content, "flyout_wave");
        foreach (var (tab, name) in new[] { (_flyout.FindName("TabLikes"), "likes"), (_flyout.FindName("TabSearch"), "search") })
        {
            ((Button)tab!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)_main.FindName(tab == _flyout.FindName("TabLikes") ? "TabLikes" : "TabSearch")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await System.Threading.Tasks.Task.Delay(900);
            Save((System.Windows.FrameworkElement)_flyout.Content, "flyout_" + name);
            Save((System.Windows.FrameworkElement)_main.Content, "window_" + name);
        }
        Hub.Player.DemoLoad(Hub.Library.Liked[2]);
        ((Button)_flyout.FindName("TabWave")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)_main.FindName("TabWave")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await System.Threading.Tasks.Task.Delay(900);
        Save((System.Windows.FrameworkElement)_flyout.Content, "flyout_wave_mix");
        ((System.Windows.Controls.ScrollViewer)_main.FindName("WaveHost")!).ScrollToVerticalOffset(400);
        await System.Threading.Tasks.Task.Delay(400);
        Save((System.Windows.FrameworkElement)_main.Content, "window_wave_presets");
        _main.ShowCover();
        await System.Threading.Tasks.Task.Delay(1500);
        Save((System.Windows.FrameworkElement)_main.Content, "window_cover");
        var src = Hub.Settings.Source;
        Hub.Settings.Source = "sc";
        Hub.RaiseSourceChanged();
        await System.Threading.Tasks.Task.Delay(600);
        Save((System.Windows.FrameworkElement)_flyout.Content, "flyout_sc");
        ((System.Windows.Controls.ScrollViewer)_flyout.FindName("WaveHost")!).ScrollToVerticalOffset(300);
        await System.Threading.Tasks.Task.Delay(400);
        Save((System.Windows.FrameworkElement)_flyout.Content, "flyout_sc_scrolled");
        Hub.Settings.Source = src;   // snapshots never change the user's settings
        Quit();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private void UpdateTray()
    {
        var p = Hub.Player;
        var text = p.Current is { } t ? $"{t.Title} — {t.Artist}" : "HUM";
        _tray?.Update(text, p.IsPlaying);
    }

    /// <summary>Heart button inside a track row (the row template lives in App.xaml).</summary>
    private void OnRowHeart(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Track t }) _ = Hub.Library.ToggleLikeAsync(t);
        e.Handled = true;
    }

    private void Quit()
    {
        Hub.Settings.Save();
        if (_main is not null) _main.Quitting = true;
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}

