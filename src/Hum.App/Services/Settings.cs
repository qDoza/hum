using System;
using System.IO;
using System.Text.Json;

namespace Hum.App.Services;

/// <summary>User settings and tokens in %APPDATA%\Hum\settings.json.</summary>
public sealed class Settings
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hum");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public string? YandexToken { get; set; }
    public string? ScToken { get; set; }
    public string? ScClientId { get; set; }
    public double Volume { get; set; } = 0.6;
    /// <summary>"ya" or "sc" — the source for the wave and search.</summary>
    public string Source { get; set; } = "ya";
    public string Mix { get; set; } = "custom";
    public double PadX { get; set; } = 0.7;
    public double PadY { get; set; } = 0.75;
    /// <summary>favorite | default | discover</summary>
    public string Diversity { get; set; } = "default";
    // SoundCloud wave filters
    /// <summary>likes | recent | old | current — what the wave grows from.</summary>
    public string ScBase { get; set; } = "likes";
    /// <summary>Genre / vibe search phrase, null = any.</summary>
    public string? ScGenre { get; set; }
    public bool ScNoSets { get; set; } = true;
    public bool ScOnlyNew { get; set; }
    public bool ScFresh { get; set; }

    /// <summary>Where the iPhone version lives (the Cloudflare Worker from HumWeb).</summary>
    public string WebAppUrl { get; set; } = "https://hum.qdoza.workers.dev";

    /// <summary>Discord Application ID for «Слушает HUM»; empty = presence off.</summary>
    public string? DiscordAppId { get; set; }
    public bool DiscordEnabled { get; set; } = true;

    /// <summary>«Оформление»: colours, font, shapes, player and visualizer.</summary>
    public LookSettings Look { get; set; } = new();

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) { Log.Write("settings load: " + e.Message); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Log.Write("settings save: " + e.Message); }
    }
}
