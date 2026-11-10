using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hum.App.Models;
using Hum.App.Services;
using Hum.App.UI;

namespace Hum.App;

/// <summary>The app's shared state: settings, both accounts with the merged likes, and the player.</summary>
public sealed class Hub
{
    public Settings Settings { get; }
    public Library Library { get; }
    public PlayerService Player { get; }

    /// <summary>The Яндекс | SoundCloud switch changed (it is shared by «Эфир» and «Поиск»).</summary>
    public event Action? SourceChanged;
    public event Action<string>? Toast;

    public Hub(Dispatcher ui)
    {
        Settings = Settings.Load();
        Library = new Library(Settings, ui);
        Player = new PlayerService(Library, Settings, ui);
        Library.Toast += m => Toast?.Invoke(m);
        Player.Toast += m => Toast?.Invoke(m);

        Discord = new DiscordPresence();
        ApplyDiscord();
        Player.Changed += UpdateDiscord;
        Player.Seeked += UpdateDiscord;
    }

    public DiscordPresence Discord { get; }

    public void ApplyDiscord()
    {
        Discord.Configure(Settings.DiscordEnabled ? Settings.DiscordAppId : null);
        UpdateDiscord();
    }

    private void UpdateDiscord()
    {
        var p = Player;
        if (p.Current is not { } t) { Discord.Clear(); return; }
        if (p.IsBusy && !p.IsPlaying) return;   // between tracks: wait for the new one to start
        Discord.SetTrack(t.Title, t.Artist, t.Album, t.BigCoverUrl, t.Source == Source.Yandex ? "Яндекс Музыка" : "SoundCloud",
            p.IsPlaying, p.PositionMs, p.DurationMs);
    }

    public void RaiseSourceChanged() => SourceChanged?.Invoke();

    /// <summary>Someone clicked a cover: show it big with the lyrics.</summary>
    public event Action? CoverRequested;
    public void OpenCover() { if (Player.Current is not null) CoverRequested?.Invoke(); }

    /// <summary>Opens the sign-in window for a service.</summary>
    public async void Connect(Source s)
    {
        var win = new LoginWindow(s);
        if (win.ShowDialog() != true) return;
        var ok = s == Source.Yandex
            ? await Library.ConnectYandexAsync(win.Token!)
            : await Library.ConnectScAsync(win.Token!, win.ClientId);
        if (ok) Toast?.Invoke(s == Source.Yandex ? $"Яндекс подключён: {Library.Ya?.Name}" : $"SoundCloud подключён: {Library.Sc?.Name}");
    }
}

/// <summary>Click on a track row plays that list starting from the row.</summary>
public static class Rows
{
    public static void Attach(ListBox list, Func<IList<Track>> tracks, PlayContext ctx, string title)
    {
        list.MouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) is not ListBoxItem item) return;
            if (item.DataContext is not Track t) return;
            var all = tracks();
            var idx = all.IndexOf(t);
            if (idx < 0) return;
            if (App.Hub.Player.Current?.Key == t.Key) { App.Hub.Player.Toggle(); return; }
            App.Hub.Player.PlayList(all, idx, ctx, title);
        };
    }
}

/// <summary>Bottom tab strip with SonyControl's liquid blob.</summary>
public static class Tabs
{
    public static void Place(FrameworkElement host, FrameworkElement blob, Button tab, bool animate)
    {
        if (!tab.IsLoaded || tab.ActualWidth == 0) return;
        var x = tab.TransformToAncestor(host).Transform(new Point(0, 0)).X;
        Motion.Liquid.MoveTo(blob, x, tab.ActualWidth, animate, 0.8);
    }

    public static void Paint(IEnumerable<Button> tabs, Button active)
    {
        foreach (var t in tabs)
            t.Foreground = (Brush)Application.Current.FindResource(t == active ? "TextMain" : "Dim");
    }
}
