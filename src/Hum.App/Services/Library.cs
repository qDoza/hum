using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hum.App.Models;

namespace Hum.App.Services;

/// <summary>Both accounts and the merged «Любимое» list (newest like first, each track tagged with its source).</summary>
public sealed class Library
{
    private readonly Settings _settings;
    private readonly Dispatcher _ui;
    private readonly HashSet<string> _likedKeys = new();

    public YandexClient? Ya { get; private set; }
    public SoundCloudClient? Sc { get; private set; }
    public ObservableCollection<Track> Liked { get; } = new();
    public bool LikesLoading { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Accounts, likes or the loading state changed.</summary>
    public event Action? Changed;
    /// <summary>A short message for the user (errors, confirmations).</summary>
    public event Action<string>? Toast;

    public Library(Settings settings, Dispatcher ui)
    {
        _settings = settings;
        _ui = ui;
    }

    public bool Has(Source s) => Demo || (s == Source.Yandex ? Ya is not null : Sc is not null);

    /// <summary>Snapshot/debug mode: pretend both accounts exist and fill «Любимое» with sample rows.</summary>
    public bool Demo { get; private set; }

    public void UseDemo()
    {
        Demo = true;
        var rows = new (Source, string, string)[]
        {
            (Source.Yandex, "Кассета 1998", "Дом Культуры"), (Source.SoundCloud, "night drive (demo)", "lowtide"),
            (Source.Yandex, "Стеклянный город", "Полярный Лис"), (Source.SoundCloud, "рассвет ремикс", "kitsunebi"),
            (Source.Yandex, "Шум прибоя", "Мира Тан"), (Source.SoundCloud, "bedroom tape #4", "pale.wav"),
            (Source.Yandex, "Маяк на краю", "Полярный Лис"), (Source.Yandex, "Лунный сад", "Кит и Компас"),
        };
        var i = 0;
        foreach (var (s, title, artist) in rows)
        {
            var t = new Track { Source = s, Id = (1000 + i++).ToString(), Title = title, Artist = artist, DurationMs = 150_000 + i * 17_000 };
            t.IsLiked = true;
            _likedKeys.Add(t.Key);
            Liked.Add(t);
        }
        Liked[0].IsCurrent = true;
        Changed?.Invoke();
    }
    public int Count(Source s) => Liked.Count(t => t.Source == s);

    // ------------------------------------------------------------------ accounts

    public async Task StartAsync()
    {
        var tasks = new List<Task>();
        if (!string.IsNullOrEmpty(_settings.YandexToken)) tasks.Add(ConnectYandexAsync(_settings.YandexToken, quiet: true));
        if (!string.IsNullOrEmpty(_settings.ScToken)) tasks.Add(ConnectScAsync(_settings.ScToken, _settings.ScClientId, quiet: true));
        await Task.WhenAll(tasks);
        await ReloadLikesAsync();
    }

    public async Task<bool> ConnectYandexAsync(string token, bool quiet = false)
    {
        try
        {
            var c = new YandexClient(token);
            await c.InitAsync();
            Ya = c;
            _settings.YandexToken = token;
            _settings.Save();
            Log.Write("yandex: " + c.Name);
            Changed?.Invoke();
            _ = LoadActivitiesAsync(c);
            if (!quiet) await ReloadLikesAsync();
            return true;
        }
        catch (Exception e)
        {
            Log.Write("yandex connect: " + e.Message);
            Toast?.Invoke("Яндекс: " + e.Message);
            return false;
        }
    }

    public async Task<bool> ConnectScAsync(string token, string? clientId, bool quiet = false)
    {
        try
        {
            var c = new SoundCloudClient(token, clientId);
            await c.InitAsync();
            Sc = c;
            _settings.ScToken = token;
            _settings.ScClientId = c.ClientId;
            _settings.Save();
            Log.Write("soundcloud: " + c.Name);
            Changed?.Invoke();
            if (!quiet) await ReloadLikesAsync();
            return true;
        }
        catch (Exception e)
        {
            Log.Write("soundcloud connect: " + e.Message);
            Toast?.Invoke("SoundCloud: " + e.Message);
            return false;
        }
    }

    /// <summary>The wave's «занятия» from Yandex (name, seed); empty until loaded.</summary>
    public List<(string Name, string Seed)> Activities { get; private set; } = new();

    private async Task LoadActivitiesAsync(YandexClient c)
    {
        try
        {
            Activities = await c.WaveActivitiesAsync();
            Log.Write($"wave activities: {Activities.Count}");
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Write("wave activities: " + e.Message); }
    }

    public void Disconnect(Source s)
    {
        if (s == Source.Yandex) { Ya = null; _settings.YandexToken = null; }
        else { Sc = null; _settings.ScToken = null; }
        _settings.Save();
        foreach (var t in Liked.Where(t => t.Source == s).ToList()) { Liked.Remove(t); _likedKeys.Remove(t.Key); }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ likes

    public async Task ReloadLikesAsync()
    {
        LikesLoading = true;
        Changed?.Invoke();
        var all = new List<Track>();
        async Task Load(Func<Task<List<Track>>> f, string name)
        {
            try { var l = await f(); lock (all) all.AddRange(l); }
            catch (Exception e) { LastError = e.Message; Log.Write($"{name} likes: {e}"); Toast?.Invoke($"{name}: не удалось загрузить лайки"); }
        }
        var jobs = new List<Task>();
        if (Ya is { } ya) jobs.Add(Load(ya.LikedAsync, "Яндекс"));
        if (Sc is { } sc) jobs.Add(Load(() => sc.LikedAsync(), "SoundCloud"));
        await Task.WhenAll(jobs);

        await _ui.InvokeAsync(() =>
        {
            Liked.Clear();
            _likedKeys.Clear();
            foreach (var t in all.OrderByDescending(t => t.LikedAt))
            {
                if (!_likedKeys.Add(t.Key)) continue;
                t.IsLiked = true;
                Liked.Add(t);
            }
            LikesLoading = false;
            Changed?.Invoke();
        });
    }

    public bool IsLiked(Track t) => _likedKeys.Contains(t.Key);

    /// <summary>Marks tracks from the API (search, wave) with the current like state.</summary>
    public void Mark(IEnumerable<Track> tracks)
    {
        foreach (var t in tracks) t.IsLiked = _likedKeys.Contains(t.Key);
    }

    public async Task ToggleLikeAsync(Track t)
    {
        var liked = !IsLiked(t);
        Apply(t, liked);
        try
        {
            if (t.Source == Source.Yandex && Ya is { } ya) await ya.SetLikeAsync(t, liked);
            else if (t.Source == Source.SoundCloud && Sc is { } sc) await sc.SetLikeAsync(t, liked);
        }
        catch (Exception e)
        {
            Log.Write("like: " + e.Message);
            Apply(t, !liked);
            Toast?.Invoke("Не удалось изменить лайк");
        }
    }

    private void Apply(Track t, bool liked)
    {
        t.IsLiked = liked;
        if (liked)
        {
            if (_likedKeys.Add(t.Key)) { t.LikedAt = DateTime.UtcNow; Liked.Insert(0, t); }
        }
        else if (_likedKeys.Remove(t.Key))
        {
            var row = Liked.FirstOrDefault(x => x.Key == t.Key);
            if (row is not null) { row.IsLiked = false; Liked.Remove(row); }
        }
        Changed?.Invoke();
    }

    public async Task DislikeAsync(Track t)
    {
        if (IsLiked(t)) await ToggleLikeAsync(t);
        if (t.Source == Source.Yandex && Ya is { } ya)
        {
            try { await ya.DislikeAsync(t); } catch (Exception e) { Log.Write("dislike: " + e.Message); }
        }
    }

    // ------------------------------------------------------------------ search + streams

    public async Task<List<Track>> SearchAsync(Source s, string q)
    {
        var list = s == Source.Yandex
            ? Ya is { } ya ? await ya.SearchAsync(q) : new List<Track>()
            : Sc is { } sc ? await sc.SearchAsync(q) : new List<Track>();
        Mark(list);
        return list;
    }

    public async Task<(bool Synced, string Text)?> LyricsAsync(Track t)
    {
        if (Demo) return (true, string.Join("\n", Enumerable.Range(0, 24).Select(i => $"[{i * 8 / 60:00}:{i * 8 % 60:00}.00] Строка текста {i + 1}")));
        return t.Source == Source.Yandex && Ya is { } ya ? await ya.LyricsAsync(t) : null;
    }

    public Task<string> StreamUrlAsync(Track t) => t.Source == Source.Yandex
        ? (Ya ?? throw new InvalidOperationException("Яндекс не подключён")).StreamUrlAsync(t)
        : (Sc ?? throw new InvalidOperationException("SoundCloud не подключён")).StreamUrlAsync(t);
}
