using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hum.App.Models;

public enum Source { Yandex, SoundCloud }

public sealed record ScTranscoding(string Url, string Protocol, string Mime);

/// <summary>One track from either service; the UI never cares which one.</summary>
public sealed class Track : INotifyPropertyChanged
{
    public required Source Source { get; init; }
    /// <summary>Yandex: "trackId:albumId" (what the API wants); SoundCloud: numeric id.</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public int DurationMs { get; init; }
    public string? CoverUrl { get; init; }
    public DateTime LikedAt { get; set; }
    public string? Album { get; init; }
    /// <summary>SoundCloud upload / release date (used by the «Свежее» filter).</summary>
    public DateTime? Released { get; init; }
    public string? Genre { get; init; }
    /// <summary>Yandex knows whether a track has lyrics (plain and/or time-synced) before asking for them.</summary>
    public bool HasLyrics { get; init; }
    public bool HasSyncedLyrics { get; init; }
    /// <summary>Same cover, large — for the cover view and Discord.</summary>
    public string? BigCoverUrl => CoverUrl?.Replace("200x200", "600x600").Replace("-t300x300.", "-t500x500.");

    // SoundCloud streaming details
    public IReadOnlyList<ScTranscoding> Transcodings { get; init; } = Array.Empty<ScTranscoding>();
    public string? TrackAuthorization { get; init; }

    /// <summary>Stable identity across services and API shapes.</summary>
    public string Key => $"{(Source == Source.Yandex ? "ya" : "sc")}:{Id.Split(':')[0]}";
    public string SourceTag => Source == Source.Yandex ? "ЯМ" : "SC";
    public string Duration => FormatTime(DurationMs);

    private bool _isLiked, _isCurrent;
    public bool IsLiked { get => _isLiked; set { if (_isLiked != value) { _isLiked = value; OnChanged(); } } }
    public bool IsCurrent { get => _isCurrent; set { if (_isCurrent != value) { _isCurrent = value; OnChanged(); } } }

    public static string FormatTime(double ms)
    {
        var s = (int)Math.Max(0, ms / 1000);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
