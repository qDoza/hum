using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hum.App.Models;

namespace Hum.App.Services;

/// <summary>
/// SoundCloud through its web API (api-v2), the one soundcloud.com itself uses: the public
/// client_id from the site's scripts plus the user's oauth_token cookie from signing in.
/// </summary>
public sealed class SoundCloudClient
{
    public const string SignInUrl = "https://soundcloud.com/signin";
    private const string Api = "https://api-v2.soundcloud.com/";

    private static readonly HttpClient Plain = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;
    public string ClientId { get; private set; }
    public long UserId { get; private set; }
    public string Name { get; private set; } = "";

    public SoundCloudClient(string token, string? clientId)
    {
        ClientId = clientId ?? "";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "OAuth " + token);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
    }

    // ------------------------------------------------------------------ plumbing

    private string WithId(string url) => url + (url.Contains('?') ? "&" : "?") + "client_id=" + ClientId;

    private async Task<JsonNode?> GetAsync(string url)
    {
        var full = url.StartsWith("http") ? url : Api + url;
        using var r = await _http.GetAsync(WithId(full));
        var text = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"SoundCloud {(int)r.StatusCode} на {full.Split('?')[0]}");
        return JsonNode.Parse(text);
    }

    /// <summary>The site's public client_id changes now and then; it is read from its JS bundles.</summary>
    public static async Task<string> FetchClientIdAsync()
    {
        var html = await Plain.GetStringAsync("https://soundcloud.com/");
        var scripts = Regex.Matches(html, "<script crossorigin src=\"(https://[^\"]+\\.js)\"").Select(m => m.Groups[1].Value).Reverse();
        foreach (var src in scripts)
        {
            var js = await Plain.GetStringAsync(src);
            var m = Regex.Match(js, "client_id[:=]\"?([A-Za-z0-9]{32})");
            if (m.Success) return m.Groups[1].Value;
        }
        throw new InvalidOperationException("не удалось получить client_id SoundCloud");
    }

    public async Task InitAsync()
    {
        if (string.IsNullOrEmpty(ClientId)) ClientId = await FetchClientIdAsync();
        JsonNode? me;
        try { me = await GetAsync("me"); }
        catch (HttpRequestException)
        {
            ClientId = await FetchClientIdAsync();   // stale client_id is the usual cause
            me = await GetAsync("me");
        }
        UserId = me?["id"]?.GetValue<long>() ?? throw new InvalidOperationException("вход в SoundCloud не удался");
        Name = me?["username"]?.ToString() ?? "SoundCloud";
    }

    // ------------------------------------------------------------------ library

    public async Task<List<Track>> LikedAsync(int maxPages = 15)
    {
        var list = new List<Track>();
        string? next = $"users/{UserId}/track_likes?limit=200&linked_partitioning=1";
        for (var page = 0; page < maxPages && next is not null; page++)
        {
            var res = await GetAsync(next);
            foreach (var item in res?["collection"]?.AsArray() ?? new JsonArray())
            {
                if (Parse(item?["track"]) is not { } t) continue;
                if (DateTime.TryParse(item?["created_at"]?.ToString(), out var at)) t.LikedAt = at.ToUniversalTime();
                list.Add(t);
            }
            next = res?["next_href"]?.ToString();
        }
        return list;
    }

    public async Task<List<Track>> SearchAsync(string q, int offset = 0)
    {
        var res = await GetAsync($"search/tracks?q={Uri.EscapeDataString(q)}&limit=40&offset={offset}");
        return (res?["collection"]?.AsArray() ?? new JsonArray()).Select(Parse).OfType<Track>().ToList();
    }

    public async Task<List<Track>> RelatedAsync(Track seed)
    {
        var res = await GetAsync($"tracks/{seed.Id}/related?limit=25");
        return (res?["collection"]?.AsArray() ?? new JsonArray()).Select(Parse).OfType<Track>().ToList();
    }

    public async Task SetLikeAsync(Track t, bool liked)
    {
        var url = WithId($"{Api}users/{UserId}/track_likes/{t.Id}");
        using var r = liked ? await _http.PutAsync(url, null) : await _http.DeleteAsync(url);
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"SoundCloud {(int)r.StatusCode}");
    }

    /// <summary>Resolves a playable URL: progressive mp3 if offered, otherwise the HLS playlist.</summary>
    public async Task<string> StreamUrlAsync(Track t)
    {
        // SoundCloud retired most mp3 / adaptive streams (they answer 404); plain AAC HLS is what works now.
        // Order by what is most likely to resolve and try each until one does. Encrypted (DRM) ones are unplayable here.
        static int Rank(ScTranscoding x) =>
            x.Protocol == "hls" && x.Mime.Contains("mp4a") ? 0
            : x.Protocol == "progressive" ? 1
            : x.Protocol == "hls" && x.Mime.Contains("mpeg") && !x.Mime.Contains("mpegurl") ? 2
            : x.Protocol == "hls" ? 3
            : 9;
        var candidates = t.Transcodings.Where(x => !x.Protocol.Contains("encrypted")).OrderBy(Rank).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException("у трека нет открытого потока");
        Exception? last = null;
        foreach (var tc in candidates)
        {
            try
            {
                var url = tc.Url + (string.IsNullOrEmpty(t.TrackAuthorization) ? "" : "?track_authorization=" + t.TrackAuthorization);
                var res = await GetAsync(url);
                if (res?["url"]?.ToString() is { Length: > 0 } direct) return direct;
            }
            catch (Exception e) { last = e; }
        }
        throw last ?? new InvalidOperationException("поток не выдан");
    }

    // ------------------------------------------------------------------ parsing

    private static Track? Parse(JsonNode? n)
    {
        if (n is null || n["kind"]?.ToString() is { } kind && kind != "track") return null;
        if (n["policy"]?.ToString() == "BLOCK") return null;
        var id = n["id"]?.ToString();
        if (string.IsNullOrEmpty(id)) return null;
        var trans = (n["media"]?["transcodings"]?.AsArray() ?? new JsonArray())
            .Select(x => new ScTranscoding(x?["url"]?.ToString() ?? "", x?["format"]?["protocol"]?.ToString() ?? "", x?["format"]?["mime_type"]?.ToString() ?? ""))
            .Where(x => x.Url.Length > 0 && !x.Url.Contains("/preview"))
            .ToList();
        var art = n["artwork_url"]?.ToString() ?? n["user"]?["avatar_url"]?.ToString();
        var artist = n["publisher_metadata"]?["artist"]?.ToString();
        if (string.IsNullOrWhiteSpace(artist)) artist = n["user"]?["username"]?.ToString() ?? "—";
        return new Track
        {
            Source = Source.SoundCloud,
            Id = id,
            Title = n["title"]?.ToString() ?? "Без названия",
            Artist = artist,
            DurationMs = (int)(n["full_duration"]?.GetValue<long>() ?? n["duration"]?.GetValue<long>() ?? 0),
            CoverUrl = art?.Replace("-large.", "-t300x300."),
            Transcodings = trans,
            TrackAuthorization = n["track_authorization"]?.ToString(),
            Released = DateTime.TryParse(n["release_date"]?.ToString() ?? n["created_at"]?.ToString(), out var rel) ? rel.ToUniversalTime() : null,
            Genre = n["genre"]?.ToString(),
        };
    }
}
