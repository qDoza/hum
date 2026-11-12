using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hum.App.Models;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Hum.App.Services;

/// <summary>What is playing right now.</summary>
public enum PlayContext { None, MyWave, Mix, Likes, Search, Vibe, Artist }

/// <summary>
/// Playback through the system media player (Media Foundation): plays Yandex mp3 and SoundCloud
/// HLS alike, and shows up in the Windows media overlay, so the keyboard media keys just work.
/// </summary>
public sealed class PlayerService
{
    private readonly MediaPlayer _mp = new() { AudioCategory = MediaPlayerAudioCategory.Media };
    private readonly Library _lib;
    private readonly Settings _settings;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _tick;

    private List<Track> _queue = new();
    private int _index = -1;
    private int _gen;               // bumps on every track change; late async results with an old gen are dropped
    private int _startedGen = -1;
    private int _errors;
    private bool _fetching;

    // wave state
    private Source _waveSource;
    private string[] _seeds = Array.Empty<string>();
    private string? _session, _batch;

    public Track? Current => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
    public PlayContext Context { get; private set; }
    /// <summary>Mix id while <see cref="Context"/> is Mix ("custom", "active", ...).</summary>
    public string? MixId { get; private set; }
    public Source ContextSource { get; private set; }
    public string ContextTitle { get; private set; } = "";
    public bool IsPlaying => _mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    public bool IsBusy { get; private set; }
    public double PositionMs => _mp.PlaybackSession.Position.TotalMilliseconds;
    public double DurationMs
    {
        get
        {
            var d = _mp.PlaybackSession.NaturalDuration.TotalMilliseconds;
            return d > 0 ? d : Current?.DurationMs ?? 0;
        }
    }
    public double Volume
    {
        get => _mp.Volume;
        set { _mp.Volume = Math.Clamp(value, 0, 1); _settings.Volume = _mp.Volume; VolumeChanged?.Invoke(); }
    }

    /// <summary>Volume changed from any of the sliders, so the others can follow.</summary>
    public event Action? VolumeChanged;

    /// <summary>Track, play state, context or busy flag changed.</summary>
    public event Action? Changed;
    /// <summary>Fires ~4 times a second while playing.</summary>
    public event Action? Progress;
    public event Action<string>? Toast;

    public PlayerService(Library lib, Settings settings, Dispatcher ui)
    {
        _lib = lib;
        _settings = settings;
        _ui = ui;
        _mp.Volume = settings.Volume;

        _mp.MediaEnded += (_, _) => _ui.BeginInvoke(OnEnded);
        _mp.MediaFailed += (_, e) => _ui.BeginInvoke(() => OnFailed(e.ErrorMessage));
        _mp.PlaybackSession.PlaybackStateChanged += (_, _) => _ui.BeginInvoke(OnStateChanged);

        // media keys / system overlay: next and previous are ours to handle
        var cm = _mp.CommandManager;
        cm.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.NextReceived += (_, e) => { e.Handled = true; _ui.BeginInvoke(() => Next()); };
        cm.PreviousReceived += (_, e) => { e.Handled = true; _ui.BeginInvoke(Prev); };

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Progress?.Invoke(), ui);
    }

    // ------------------------------------------------------------------ starting things

    public sealed record Mix(string Id, string Title, string Group, params string[] Seeds);

    /// <summary>Wave presets: plain settings and a few handy combinations. «Свой» comes from the mood pad.</summary>
    public static readonly Mix[] Mixes =
    {
        new("custom", "Свой", "Настроение"),
        new("active", "Бодрый", "Настроение", "settingMoodEnergy:active"),
        new("fun", "Весёлый", "Настроение", "settingMoodEnergy:fun"),
        new("calm", "Спокойный", "Настроение", "settingMoodEnergy:calm"),
        new("sad", "Грустный", "Настроение", "settingMoodEnergy:sad"),

        new("favorite", "Любимое", "Характер и язык", "settingDiversity:favorite"),
        new("discover", "Новое", "Характер и язык", "settingDiversity:discover"),
        new("popular", "Хиты", "Характер и язык", "settingDiversity:popular"),
        new("ru", "Русское", "Характер и язык", "settingLanguage:russian"),
        new("foreign", "Иностранное", "Характер и язык", "settingLanguage:not-russian"),
        new("instrumental", "Без слов", "Характер и язык", "settingLanguage:without-words"),

        new("focus", "Фокус", "Подборки", "settingMoodEnergy:calm", "settingLanguage:without-words"),
        new("party", "Вечеринка", "Подборки", "settingMoodEnergy:fun", "settingDiversity:popular"),
        new("drive", "В дорогу", "Подборки", "settingMoodEnergy:active", "settingDiversity:favorite"),
        new("dig", "Раскопки", "Подборки", "settingDiversity:discover", "settingLanguage:not-russian"),
        new("rusnew", "Новое русское", "Подборки", "settingDiversity:discover", "settingLanguage:russian"),
        new("evening", "Тихий вечер", "Подборки", "settingMoodEnergy:calm", "settingDiversity:favorite"),
        new("rain", "Под дождь", "Подборки", "settingMoodEnergy:sad", "settingDiversity:discover"),
    };

    /// <summary>A «занятие» from the service (workout, focus…): its own station seed, not a setting.</summary>
    /// <summary>«Эфир по артисту»: a wave seeded by one Yandex artist.</summary>
    public Task PlayArtistWaveAsync(string artistId, string name) =>
        StartYandexWave(new[] { "artist:" + artistId }, PlayContext.Mix, "artist:" + artistId, "Эфир · " + name);

    /// <summary>Key of the track «Эфир по вайбу» was started from (null when no vibe wave plays).</summary>
    public string? VibeKey { get; private set; }
    private Track? _vibeSeed;

    /// <summary>
    /// «Эфир по вайбу»: the current track keeps playing and becomes the seed of a new wave of similar music
    /// (Yandex: a station seeded by the track; SoundCloud: «похожие» on it and on what follows).
    /// </summary>
    public async Task StartVibeAsync()
    {
        if (Current is not { } t) return;
        if (Context == PlayContext.Vibe && VibeKey == t.Key) { Toast?.Invoke("Уже играет эфир по этому треку"); return; }
        SetBusy(true);
        try
        {
            List<Track> list;
            if (t.Source == Source.Yandex)
            {
                if (_lib.Ya is not { } ya) throw new InvalidOperationException("подключите Яндекс");
                var seeds = new[] { "track:" + t.Id.Split(':')[0] };
                var b = await ya.WaveStartAsync(seeds);
                _waveSource = Source.Yandex; _seeds = seeds; _session = b.SessionId; _batch = b.BatchId;
                list = b.Tracks;
            }
            else
            {
                if (_lib.Sc is not { } sc) throw new InvalidOperationException("подключите SoundCloud");
                list = ScFilter((await sc.RelatedAsync(t)).OrderBy(_ => Random.Shared.Next()), new HashSet<string> { t.Key });
                _waveSource = Source.SoundCloud; _session = null; _vibeSeed = t;
            }
            list = list.Where(x => x.Key != t.Key).ToList();
            if (list.Count == 0) throw new InvalidOperationException("похожего не нашлось");
            _lib.Mark(list);
            VibeKey = t.Key;
            _queue = new List<Track> { t };
            _queue.AddRange(list);
            _index = 0;
            SetContext(PlayContext.Vibe, null, t.Source, "Эфир по вайбу · " + t.Title);
            SetBusy(false);
            if (!IsPlaying) _mp.Play();
            Toast?.Invoke($"Дальше — похожее на «{t.Title}»");
        }
        catch (Exception e)
        {
            Log.Write("vibe: " + e.Message);
            SetBusy(false);
            Toast?.Invoke("Не получилось: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ queue view (the player's right side)

    /// <summary>What plays now and what comes next (the wave keeps growing as you go).</summary>
    public IReadOnlyList<Track> Upcoming(int count) => _index < 0 ? Array.Empty<Track>() : _queue.Skip(_index).Take(count + 1).ToList();

    /// <summary>Tracks heard in this session, newest first.</summary>
    public IReadOnlyList<Track> Recent => _recent;
    private readonly List<Track> _recent = new();

    /// <summary>Jumps to a track that is already in the queue (keeps the wave going).</summary>
    public void JumpTo(Track t)
    {
        var i = _queue.IndexOf(t);
        if (i < 0 || i == _index) return;
        SkipFeedback();
        _ = GoTo(i);
    }

    /// <summary>Repeat the current track.</summary>
    public bool Repeat
    {
        get => _mp.IsLoopingEnabled;
        set { _mp.IsLoopingEnabled = value; Changed?.Invoke(); }
    }

    /// <summary>Shuffles what comes after the current track.</summary>
    public void ShuffleRest()
    {
        if (_queue.Count - _index < 3) return;
        var rest = _queue.Skip(_index + 1).OrderBy(_ => Random.Shared.Next()).ToList();
        _queue = _queue.Take(_index + 1).Concat(rest).ToList();
        Toast?.Invoke("Дальше — вперемешку");
    }

    public Task PlayActivityAsync(string name, string seed) =>
        StartYandexWave(new[] { seed }, PlayContext.Mix, "act:" + seed, "Занятие · " + name);

    /// <summary>The plain personal wave: no settings at all.</summary>
    public Task PlayMyWaveAsync(Source s) => s == Source.Yandex
        ? StartYandexWave(new[] { YandexClient.WaveSeed }, PlayContext.MyWave, null, "Мой эфир")
        : StartScWave(PlayContext.MyWave, "Мой эфир");

    public Task PlayMixAsync(string mixId)
    {
        var mix = Mixes.First(m => m.Id == mixId);
        var seeds = new List<string> { YandexClient.WaveSeed };
        if (mixId == "custom")
        {
            seeds.Add("settingMoodEnergy:" + MoodFromPad(_settings.PadX, _settings.PadY));
            if (_settings.Diversity != "default") seeds.Add("settingDiversity:" + _settings.Diversity);
        }
        else seeds.AddRange(mix.Seeds);
        return StartYandexWave(seeds.ToArray(), PlayContext.Mix, mixId, "Микс · " + mix.Title);
    }

    /// <summary>The pad: x = sad → happy, y = quiet → energetic.</summary>
    public static string MoodFromPad(double x, double y) =>
        y >= 0.5 ? (x >= 0.5 ? "fun" : "active") : (x >= 0.5 ? "calm" : "sad");

    public void PlayList(IList<Track> tracks, int start, PlayContext ctx, string title)
    {
        if (tracks.Count == 0) return;
        SkipFeedback();
        _session = null;
        SetContext(ctx, null, tracks[Math.Clamp(start, 0, tracks.Count - 1)].Source, title);
        _queue = tracks.ToList();
        _index = -1;
        _ = GoTo(start);
    }

    private async Task StartYandexWave(string[] seeds, PlayContext ctx, string? mix, string title)
    {
        if (_lib.Ya is not { } ya) { Toast?.Invoke("Подключите Яндекс"); return; }
        SkipFeedback();
        var gen = ++_gen;
        SetBusy(true);
        try
        {
            var batch = await ya.WaveStartAsync(seeds);
            if (gen != _gen) return;
            _waveSource = Source.Yandex;
            _seeds = seeds;
            _session = batch.SessionId;
            _batch = batch.BatchId;
            _lib.Mark(batch.Tracks);
            SetContext(ctx, mix, Source.Yandex, title);
            _queue = batch.Tracks;
            _index = -1;
            await GoTo(0);
        }
        catch (Exception e)
        {
            Log.Write("wave start: " + e);
            Toast?.Invoke("Эфир не запустился: " + e.Message);
            SetBusy(false);
        }
    }

    /// <summary>SoundCloud has no personal wave, so it is built from tracks related to your likes.</summary>
    private async Task StartScWave(PlayContext ctx, string title)
    {
        if (_lib.Sc is not { } sc) { Toast?.Invoke("Подключите SoundCloud"); return; }
        var genre = _settings.ScGenre;
        if (genre is null && ScSeeds().Count == 0) { Toast?.Invoke("Лайкните пару треков в SoundCloud — эфир соберётся из них"); return; }
        SkipFeedback();
        var gen = ++_gen;
        SetBusy(true);
        try
        {
            var start = new List<Track>();
            if (genre is not null)
            {
                // a vibe: start from search results for it, shuffled, then grow by «похожие»
                var found = await sc.SearchAsync(genre, Random.Shared.Next(0, 3) * 40);
                start.AddRange(found.OrderBy(_ => Random.Shared.Next()).Take(12));
                _scGenreOffset = 40;
            }
            else
            {
                var seed = PickScSeed();
                if (!_settings.ScOnlyNew) start.Add(seed);
                start.AddRange(await sc.RelatedAsync(seed));
            }
            if (gen != _gen) return;
            var queue = ScFilter(start, new HashSet<string>());
            if (queue.Count == 0) { Toast?.Invoke("Под эти фильтры ничего не нашлось — ослабьте их"); SetBusy(false); return; }
            _waveSource = Source.SoundCloud;
            _session = null;
            _lib.Mark(queue);
            SetContext(ctx, null, Source.SoundCloud, genre is null ? title : $"{title} · {ScGenreTitle(genre)}");
            _queue = queue;
            _index = -1;
            await GoTo(0);
        }
        catch (Exception e)
        {
            Log.Write("sc wave: " + e);
            Toast?.Invoke("Эфир SoundCloud не запустился");
            SetBusy(false);
        }
    }

    /// <summary>Snapshot mode: pretend a track is loaded (no audio).</summary>
    public void DemoLoad(Track t)
    {
        _queue = new List<Track> { t };
        _index = 0;
        t.IsCurrent = true;
        SetContext(PlayContext.Mix, "party", t.Source, "Микс · Вечеринка");
    }

    // ------------------------------------------------------------------ SoundCloud wave filters

    private int _scGenreOffset;

    public sealed record ScOption(string Id, string Title, string? Query = null);

    /// <summary>What the SoundCloud wave grows from.</summary>
    public static readonly ScOption[] ScBases =
    {
        new("likes", "Все лайки"), new("recent", "Свежие лайки"), new("old", "Давние лайки"), new("current", "От текущего трека"),
    };

    /// <summary>Vibes: a SoundCloud search phrase seeds the wave instead of your likes.</summary>
    public static readonly ScOption[] ScGenres =
    {
        new("any", "Любой"), new("lofi", "Lo-fi", "lofi"), new("phonk", "Phonk", "phonk"), new("hiphop", "Hip-hop", "hip hop"),
        new("trap", "Trap", "trap"), new("electronic", "Electronic", "electronic"), new("house", "House", "house"),
        new("techno", "Techno", "techno"), new("dnb", "Drum & Bass", "drum and bass"), new("ambient", "Ambient", "ambient"),
        new("indie", "Indie", "indie"), new("rock", "Rock", "rock"), new("pop", "Pop", "pop"), new("rnb", "R&B", "r&b"),
        new("jazz", "Jazz", "jazz"), new("hyperpop", "Hyperpop", "hyperpop"), new("ru", "Русский рэп", "русский рэп"),
    };

    private static string ScGenreTitle(string query) => ScGenres.FirstOrDefault(g => g.Query == query)?.Title ?? query;

    private List<Track> ScSeeds()
    {
        var likes = _lib.Liked.Where(t => t.Source == Source.SoundCloud).ToList();   // newest first
        return _settings.ScBase switch
        {
            "recent" => likes.Take(15).ToList(),
            "old" => likes.Skip(Math.Max(0, likes.Count - 40)).ToList(),
            _ => likes,
        };
    }

    private Track PickScSeed()
    {
        if (_settings.ScBase == "current" && Current is { Source: Source.SoundCloud } cur) return cur;
        var seeds = ScSeeds();
        if (seeds.Count == 0 && Current is { Source: Source.SoundCloud } c) return c;
        return seeds[Random.Shared.Next(seeds.Count)];
    }

    /// <summary>Applies «без сетов», «только незнакомое», «свежее» and drops repeats and unplayable tracks.</summary>
    private List<Track> ScFilter(IEnumerable<Track> tracks, HashSet<string> known)
    {
        var s = _settings;
        var yearAgo = DateTime.UtcNow.AddYears(-1);
        var seen = new HashSet<string>(known);
        return tracks.Where(t =>
                t.Source == Source.SoundCloud
                && t.Transcodings.Any(x => !x.Protocol.Contains("encrypted"))
                && !(s.ScNoSets && t.DurationMs > 8 * 60_000)
                && !(s.ScOnlyNew && _lib.IsLiked(t))
                && !(s.ScFresh && (t.Released ?? DateTime.MinValue) < yearAgo)
                && seen.Add(t.Key))
            .ToList();
    }

    /// <summary>A filter changed: if the SoundCloud wave plays, rebuild it right away.</summary>
    public void ScFiltersChanged()
    {
        _settings.Save();
        if (Context == PlayContext.MyWave && ContextSource == Source.SoundCloud) _ = StartScWave(PlayContext.MyWave, "Мой эфир");
    }

    // ------------------------------------------------------------------ controls

    /// <summary>Play/pause. With nothing loaded it starts «Мой эфир» of the chosen source.</summary>
    public void Toggle()
    {
        if (Current is null)
        {
            var s = _settings.Source == "sc" ? Source.SoundCloud : Source.Yandex;
            _ = PlayMyWaveAsync(_lib.Has(s) ? s : (_lib.Has(Source.Yandex) ? Source.Yandex : Source.SoundCloud));
            return;
        }
        if (IsPlaying) _mp.Pause(); else _mp.Play();
    }

    public void Next(bool user = true)
    {
        if (Current is null) return;
        if (user) SkipFeedback();
        _ = GoTo(_index + 1);
    }

    public void Prev()
    {
        if (Current is null) return;
        if (PositionMs > 3000 || _index <= 0) { _mp.PlaybackSession.Position = TimeSpan.Zero; return; }
        _ = GoTo(_index - 1);
    }

    public void Seek(double fraction)
    {
        if (Current is null) return;
        _mp.PlaybackSession.Position = TimeSpan.FromMilliseconds(DurationMs * Math.Clamp(fraction, 0, 1));
        Progress?.Invoke();
        Seeked?.Invoke();
    }

    /// <summary>The position jumped (seek), so anything showing elapsed time must resync.</summary>
    public event Action? Seeked;

    public async Task DislikeAsync()
    {
        if (Current is not { } t) return;
        Next();
        await _lib.DislikeAsync(t);
        Toast?.Invoke("Больше не будет попадаться");
    }

    // ------------------------------------------------------------------ queue

    public bool IsWave => Context is PlayContext.MyWave or PlayContext.Mix or PlayContext.Vibe;

    private async Task GoTo(int idx)
    {
        if (!IsWave && _queue.Count > 0) idx = (idx % _queue.Count + _queue.Count) % _queue.Count;
        if (idx >= _queue.Count)
        {
            if (IsWave) await FetchMore(playNext: true);
            return;
        }
        if (idx < 0) return;

        if (Current is { } old) old.IsCurrent = false;
        _index = idx;
        var track = _queue[idx];
        _recent.Remove(track);
        _recent.Insert(0, track);
        if (_recent.Count > 30) _recent.RemoveAt(_recent.Count - 1);
        track.IsCurrent = true;
        var gen = ++_gen;
        _mp.Pause();
        SetBusy(true);
        Changed?.Invoke();

        if (IsWave && idx >= _queue.Count - 2) _ = FetchMore(playNext: false);

        try
        {
            var url = await _lib.StreamUrlAsync(track);
            if (gen != _gen) return;
            var item = new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(url)));
            var props = item.GetDisplayProperties();
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = track.Title;
            props.MusicProperties.Artist = track.Artist;
            if (track.CoverUrl is { } cover) props.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(cover));
            item.ApplyDisplayProperties(props);
            _mp.Source = item;
            _mp.Play();
        }
        catch (Exception e)
        {
            if (gen != _gen) return;
            Log.Write($"stream {track.Key}: {e.Message}");
            OnFailed(e.Message);
        }
    }

    private async Task FetchMore(bool playNext)
    {
        if (_fetching) return;
        _fetching = true;
        try
        {
            List<Track> more;
            if (_waveSource == Source.Yandex && _lib.Ya is { } ya && _session is not null)
            {
                try
                {
                    var b = await ya.WaveMoreAsync(_session, _queue.TakeLast(10).Select(t => t.Id));
                    _batch = b.BatchId;
                    more = b.Tracks;
                }
                catch (Exception e)
                {
                    // sessions expire: start a fresh one with the same seeds
                    Log.Write("wave more: " + e.Message);
                    var b = await ya.WaveStartAsync(_seeds);
                    _session = b.SessionId;
                    _batch = b.BatchId;
                    more = b.Tracks;
                }
            }
            else if (_waveSource == Source.SoundCloud && _lib.Sc is { } sc)
            {
                // alternate: «похожие» on what plays now, and a fresh seed (or the next page of the vibe)
                var known0 = _queue.Select(t => t.Key).ToHashSet();
                more = new List<Track>();
                for (var attempt = 0; attempt < 3 && ScFilter(more, known0).Count < 4; attempt++)
                {
                    if (Context != PlayContext.Vibe && _settings.ScGenre is { } g && attempt % 2 == 1)
                    {
                        more.AddRange(await sc.SearchAsync(g, _scGenreOffset));
                        _scGenreOffset += 40;
                    }
                    else
                    {
                        var seed = Context == PlayContext.Vibe && _vibeSeed is { } vs ? (attempt == 0 ? vs : new[] { vs }.Concat(_queue.TakeLast(4)).OrderBy(_ => Random.Shared.Next()).First())
                                 : attempt == 0 && Current is { Source: Source.SoundCloud } cur ? cur
                                 : _settings.ScGenre is null ? PickScSeed() : _queue[Random.Shared.Next(_queue.Count)];
                        more.AddRange(await sc.RelatedAsync(seed));
                    }
                }
                more = ScFilter(more.OrderBy(_ => Random.Shared.Next()).ToList(), known0);
            }
            else return;

            var known = _queue.Select(t => t.Key).ToHashSet();
            var fresh = more.Where(t => known.Add(t.Key)).ToList();
            _lib.Mark(fresh);
            _queue.AddRange(fresh);
        }
        catch (Exception e) { Log.Write("fetch more: " + e.Message); }
        finally { _fetching = false; }
        if (playNext) await GoTo(_index + 1);
    }

    // ------------------------------------------------------------------ player events

    private void OnStateChanged()
    {
        if (IsPlaying)
        {
            SetBusy(false);
            _errors = 0;
            _tick.Start();
            if (_startedGen != _gen && Current is { } t && _session is not null && _lib.Ya is { } ya)
            {
                _startedGen = _gen;
                _ = ya.FeedbackAsync(_session, _batch ?? "", "trackStarted", t);
            }
        }
        else _tick.Stop();
        Changed?.Invoke();
        Progress?.Invoke();
    }

    private void OnEnded()
    {
        if (Current is { } t && _session is not null && _lib.Ya is { } ya)
            _ = ya.FeedbackAsync(_session, _batch ?? "", "trackFinished", t, t.DurationMs / 1000.0);
        Next(user: false);
    }

    private void OnFailed(string why)
    {
        Log.Write("playback failed: " + why);
        SetBusy(false);
        if (++_errors < 5) Next(user: false);
        else { _errors = 0; Toast?.Invoke("Не получается воспроизвести: " + why); }
    }

    private void SkipFeedback()
    {
        if (Current is { } t && _session is not null && _lib.Ya is { } ya && IsWave)
            _ = ya.FeedbackAsync(_session, _batch ?? "", "skip", t, PositionMs / 1000);
    }

    private void SetContext(PlayContext c, string? mix, Source s, string title)
    {
        Context = c;
        MixId = mix;
        ContextSource = s;
        ContextTitle = title;
        Changed?.Invoke();
    }

    private void SetBusy(bool v)
    {
        if (IsBusy == v) return;
        IsBusy = v;
        Changed?.Invoke();
    }
}
