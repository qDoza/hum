using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using Hum.App.Models;

namespace Hum.App.Services;

/// <summary>
/// Minimal client for the Yandex Music mobile API (the same one the official apps use):
/// account, likes, search, the personal wave ("rotor sessions") and stream links.
/// </summary>
public sealed class YandexClient
{
    public const string OAuthUrl = "https://oauth.yandex.ru/authorize?response_type=token&client_id=23cabbbdc6cd418abb4b39c32c41195d";
    public const string WaveSeed = "user:onyourwave";
    private const string SignSalt = "XGRlBW9FXlekgbPrRHuSiA";
    private const string From = "desktop_win-home-playlist_of_the_day-playlist-default";

    private readonly HttpClient _http;
    public string Uid { get; private set; } = "";
    public string Name { get; private set; } = "";

    public YandexClient(string token)
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.music.yandex.net/"), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "OAuth " + token);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Yandex-Music-Client", "YandexMusicAndroid/24023621");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru");
    }

    // ------------------------------------------------------------------ plumbing

    private async Task<JsonNode?> GetAsync(string url)
    {
        using var r = await _http.GetAsync(url);
        return await Unwrap(r, url);
    }

    private async Task<JsonNode?> PostFormAsync(string url, Dictionary<string, string> form)
    {
        using var r = await _http.PostAsync(url, new FormUrlEncodedContent(form));
        return await Unwrap(r, url);
    }

    private async Task<JsonNode?> PostJsonAsync(string url, JsonObject body)
    {
        using var r = await _http.PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        return await Unwrap(r, url);
    }

    private static async Task<JsonNode?> Unwrap(HttpResponseMessage r, string url)
    {
        var text = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
            throw new HttpRequestException($"Яндекс {(int)r.StatusCode} на {url.Split('?')[0]}: {Trim(text)}");
        return JsonNode.Parse(text)?["result"];
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;

    // ------------------------------------------------------------------ account

    public async Task InitAsync()
    {
        var res = await GetAsync("account/status") ?? throw new InvalidOperationException("пустой ответ");
        var acc = res["account"];
        Uid = acc?["uid"]?.ToString() ?? throw new InvalidOperationException("токен недействителен");
        Name = acc?["displayName"]?.ToString() ?? acc?["login"]?.ToString() ?? "Яндекс";
    }

    // ------------------------------------------------------------------ tracks / likes

    public async Task<List<Track>> LikedAsync()
    {
        var res = await GetAsync($"users/{Uid}/likes/tracks");
        var shorts = res?["library"]?["tracks"]?.AsArray() ?? new JsonArray();
        var stamps = new Dictionary<string, DateTime>();
        var ids = new List<string>();
        foreach (var s in shorts)
        {
            var id = s?["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) continue;
            var album = s?["albumId"]?.ToString();
            ids.Add(string.IsNullOrEmpty(album) ? id : $"{id}:{album}");
            stamps[id] = DateTime.TryParse(s?["timestamp"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var t) ? t : DateTime.MinValue;
        }
        var tracks = await TracksAsync(ids);
        foreach (var t in tracks)
            if (stamps.TryGetValue(t.Id.Split(':')[0], out var ts)) t.LikedAt = ts;
        return tracks;
    }

    public async Task<List<Track>> TracksAsync(IReadOnlyList<string> ids)
    {
        var all = new List<Track>();
        for (var i = 0; i < ids.Count; i += 250)
        {
            var chunk = string.Join(",", ids.Skip(i).Take(250));
            var res = await PostFormAsync("tracks", new() { ["track-ids"] = chunk, ["with-positions"] = "false" });
            foreach (var n in res?.AsArray() ?? new JsonArray())
                if (Parse(n) is { } t) all.Add(t);
        }
        return all;
    }

    public Task SetLikeAsync(Track t, bool liked) =>
        PostFormAsync($"users/{Uid}/likes/tracks/{(liked ? "add-multiple" : "remove")}", new() { ["track-ids"] = t.Id });

    public Task DislikeAsync(Track t) =>
        PostFormAsync($"users/{Uid}/dislikes/tracks/add-multiple", new() { ["track-ids"] = t.Id });

    public async Task<List<Track>> SearchAsync(string text)
    {
        var res = await GetAsync($"search?text={Uri.EscapeDataString(text)}&type=track&page=0&nocorrect=false");
        var list = new List<Track>();
        foreach (var n in res?["tracks"]?["results"]?.AsArray() ?? new JsonArray())
            if (Parse(n) is { } t) list.Add(t);
        return list;
    }

    /// <summary>Direct mp3 link (valid for about a minute, so ask right before playing).</summary>
    public async Task<string> StreamUrlAsync(Track t)
    {
        var infos = (await GetAsync($"tracks/{t.Id}/download-info"))?.AsArray() ?? throw new InvalidOperationException("нет потока");
        var best = infos.Where(i => i?["codec"]?.ToString() == "mp3")
                        .OrderByDescending(i => i?["bitrateInKbps"]?.GetValue<int>() ?? 0)
                        .FirstOrDefault() ?? infos.FirstOrDefault()
                   ?? throw new InvalidOperationException("трек недоступен");
        var xmlUrl = best["downloadInfoUrl"]!.ToString();
        var xml = XDocument.Parse(await _http.GetStringAsync(xmlUrl));
        string V(string n) => xml.Descendants(n).First().Value;
        var path = V("path");
        var sign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(SignSalt + path[1..] + V("s")))).ToLowerInvariant();
        return $"https://{V("host")}/get-mp3/{sign}/{V("ts")}{path}";
    }

    // ------------------------------------------------------------------ lyrics

    private const string LyricsSignKey = "p93jhgh689SBReK6ghtw62";

    /// <summary>
    /// The track's lyrics from the service (licensed text, fetched on demand and never stored):
    /// LRC with timestamps when synced lyrics exist, plain text otherwise; null when there are none.
    /// </summary>
    public async Task<(bool Synced, string Text)?> LyricsAsync(Track t)
    {
        if (!t.HasLyrics) return null;
        var numeric = t.Id.Split(':')[0];
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LyricsSignKey));
        var sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(numeric + ts)));
        var format = t.HasSyncedLyrics ? "LRC" : "TEXT";
        var res = await GetAsync($"tracks/{numeric}/lyrics?format={format}&timeStamp={ts}&sign={Uri.EscapeDataString(sign)}");
        var url = res?["downloadUrl"]?.ToString();
        if (string.IsNullOrEmpty(url)) return null;
        var text = await _http.GetStringAsync(url);
        return (t.HasSyncedLyrics, text);
    }

    // ------------------------------------------------------------------ wave (rotor sessions)

    /// <summary>The wave's «занятия» (workout, focus, road…) as (name, seed) pairs.</summary>
    public async Task<List<(string Name, string Seed)>> WaveActivitiesAsync()
    {
        var res = await GetAsync($"rotor/wave/settings?seeds={Uri.EscapeDataString(WaveSeed)}");
        var list = new List<(string, string)>();
        foreach (var block in res?["blocks"]?.AsArray() ?? new JsonArray())
            foreach (var st in block?["items"]?.AsArray() ?? new JsonArray())
            {
                var type = st?["id"]?["type"]?.ToString();
                var tag = st?["id"]?["tag"]?.ToString();
                var name = st?["name"]?.ToString();
                if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(name))
                    list.Add((name, $"{type}:{tag}"));
            }
        return list;
    }

    public sealed record WaveBatch(string SessionId, string BatchId, List<Track> Tracks);

    public async Task<WaveBatch> WaveStartAsync(IEnumerable<string> seeds)
    {
        var body = new JsonObject
        {
            ["seeds"] = new JsonArray(seeds.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
            ["includeTracksInResponse"] = true,
        };
        var res = await PostJsonAsync("rotor/session/new", body) ?? throw new InvalidOperationException("волна не ответила");
        var batch = new WaveBatch(res["radioSessionId"]!.ToString(), res["batchId"]?.ToString() ?? "", ParseSequence(res));
        _ = FeedbackAsync(batch.SessionId, batch.BatchId, "radioStarted");
        return batch;
    }

    public async Task<WaveBatch> WaveMoreAsync(string sessionId, IEnumerable<string> recentIds)
    {
        var body = new JsonObject { ["queue"] = new JsonArray(recentIds.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) };
        var res = await PostJsonAsync($"rotor/session/{sessionId}/tracks", body);
        if (res is null || res["unknownSession"]?.GetValue<bool>() == true) throw new InvalidOperationException("сессия волны истекла");
        return new WaveBatch(sessionId, res["batchId"]?.ToString() ?? "", ParseSequence(res));
    }

    /// <summary>Tells the wave what happened, so it learns: radioStarted, trackStarted, trackFinished, skip.</summary>
    public async Task FeedbackAsync(string sessionId, string batchId, string type, Track? t = null, double? played = null)
    {
        try
        {
            var ev = new JsonObject { ["type"] = type, ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) };
            if (t is not null) ev["trackId"] = t.Id;
            if (played is not null) ev["totalPlayedSeconds"] = Math.Round(played.Value, 1);
            var body = new JsonObject { ["event"] = ev, ["batchId"] = batchId };
            if (type == "radioStarted") body["from"] = From;
            await PostJsonAsync($"rotor/session/{sessionId}/feedback", body);
        }
        catch (Exception e) { Log.Write("ya feedback: " + e.Message); }
    }

    private static List<Track> ParseSequence(JsonNode res)
    {
        var list = new List<Track>();
        foreach (var item in res["sequence"]?.AsArray() ?? new JsonArray())
            if (Parse(item?["track"]) is { } t) list.Add(t);
        return list;
    }

    // ------------------------------------------------------------------ artists

    public sealed record ArtistInfo(string Name, string? PhotoUrl, string Sub, List<Track> Popular, List<Track> All);

    /// <summary>An artist's page: photo, genres and listeners, popular tracks, then the rest of their tracks.</summary>
    public async Task<ArtistInfo> ArtistAsync(string id)
    {
        var r = await GetAsync($"artists/{id}/brief-info") ?? throw new InvalidOperationException("пустой ответ");
        var a = r["artist"];
        var img = a?["cover"]?["uri"]?.ToString() ?? a?["ogImage"]?.ToString();
        var bits = new List<string>();
        var genres = (a?["genres"]?.AsArray() ?? new JsonArray()).Select(g => g?.ToString()).Where(g => !string.IsNullOrEmpty(g)).Take(2)
                     .Select(g => Genres.TryGetValue(g!, out var x) ? x : g!).ToList();
        if (genres.Count > 0) bits.Add(string.Join(", ", genres));
        if (r["stats"]?["lastMonthListeners"]?.GetValue<long>() is long l and > 0)
            bits.Add((l >= 1_000_000 ? (l / 1e6).ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + " млн" : l >= 1000 ? $"{l / 1000} тыс" : l.ToString()) + " слушателей за месяц");
        var popular = (r["popularTracks"]?.AsArray() ?? new JsonArray()).Select(Parse).OfType<Track>().ToList();
        var all = new List<Track>();
        try
        {
            var t = await GetAsync($"artists/{id}/tracks?page=0&page-size=100");
            var seen = popular.Select(x => x.Key).ToHashSet();
            all = (t?["tracks"]?.AsArray() ?? new JsonArray()).Select(Parse).OfType<Track>().Where(x => seen.Add(x.Key)).ToList();
        }
        catch (Exception e) { Log.Write("artist tracks: " + e.Message); }
        return new ArtistInfo(a?["name"]?.ToString() ?? "", string.IsNullOrEmpty(img) ? null : "https://" + img.Replace("%%", "1000x1000"), string.Join(" · ", bits), popular, all);
    }

    private static readonly Dictionary<string, string> Genres = new()
    {
        ["rusrap"] = "русский рэп", ["foreignrap"] = "рэп", ["rap"] = "рэп", ["rusrock"] = "русский рок", ["rock"] = "рок", ["ruspop"] = "русская поп",
        ["pop"] = "поп", ["electronics"] = "электроника", ["dance"] = "танцевальная", ["alternative"] = "альтернатива", ["indie"] = "инди",
        ["soundtrack"] = "саундтрек", ["relax"] = "релакс", ["estrada"] = "эстрада", ["metal"] = "метал", ["jazz"] = "джаз", ["rnb"] = "r&b",
        ["soul"] = "соул", ["folk"] = "фолк", ["classical"] = "классика", ["dnb"] = "drum & bass", ["lounge"] = "лаунж", ["local_indie"] = "инди",
    };

    // ------------------------------------------------------------------ parsing

    private static Track? Parse(JsonNode? n)
    {
        if (n is null || n["available"]?.GetValue<bool>() == false) return null;
        var id = n["id"]?.ToString();
        if (string.IsNullOrEmpty(id)) return null;
        var album = n["albums"]?.AsArray().FirstOrDefault();
        var albumId = album?["id"]?.ToString();
        var title = n["title"]?.ToString() ?? "Без названия";
        var version = n["version"]?.ToString();
        if (!string.IsNullOrWhiteSpace(version)) title += $" ({version})";
        var artistNodes = n["artists"]?.AsArray() ?? new JsonArray();
        var artists = string.Join(", ", artistNodes.Select(a => a?["name"]?.ToString()).Where(s => !string.IsNullOrEmpty(s)));
        var refs = artistNodes.Where(a => a?["id"] is not null && !string.IsNullOrEmpty(a?["name"]?.ToString()) && a?["various"]?.GetValue<bool>() != true)
                              .Select(a => new ArtistRef(a!["id"]!.ToString(), a["name"]!.ToString())).ToList();
        var genre = album?["genre"]?.ToString();
        var cover = n["coverUri"]?.ToString() ?? album?["coverUri"]?.ToString();
        var lyr = n["lyricsInfo"];
        var synced = lyr?["hasAvailableSyncLyrics"]?.GetValue<bool>() == true;
        return new Track
        {
            Source = Source.Yandex,
            Id = string.IsNullOrEmpty(albumId) ? id : $"{id}:{albumId}",
            Title = title,
            Artist = string.IsNullOrEmpty(artists) ? "—" : artists,
            Album = album?["title"]?.ToString(),
            Artists = refs,
            Genre = string.IsNullOrEmpty(genre) ? null : Genres.TryGetValue(genre, out var gn) ? gn : genre,
            Year = album?["year"]?.GetValue<int>(),
            DurationMs = n["durationMs"]?.GetValue<int>() ?? 0,
            CoverUrl = string.IsNullOrEmpty(cover) ? null : "https://" + cover.Replace("%%", "200x200"),
            HasSyncedLyrics = synced,
            HasLyrics = synced || lyr?["hasAvailableTextLyrics"]?.GetValue<bool>() == true,
        };
    }
}
