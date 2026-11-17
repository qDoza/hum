using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Hum.App.Services;

/// <summary>«Оформление»: what the user picked. Stored in settings.json.</summary>
public sealed class LookSettings
{
    public string Accent { get; set; } = "mono";
    public string Bg { get; set; } = "black";
    public string Contrast { get; set; } = "normal";
    public string Font { get; set; } = "system";
    public string Shape { get; set; } = "soft";
    public string Cover { get; set; } = "round";
    public bool Spin { get; set; } = true;
    public string PlayerBg { get; set; } = "glass";
    public string VizStyle { get; set; } = "bars";
    public double VizAmount { get; set; } = 1;
    public bool ReducedMotion { get; set; }
}

/// <summary>
/// Applies <see cref="LookSettings"/> by swapping the app's colour, radius and font resources. Everything in XAML
/// refers to them as DynamicResource, so the whole interface follows at once.
/// </summary>
public static class Look
{
    public sealed record Option(string Id, string Title, string? Hex = null);

    public static readonly Option[] Accents =
    {
        new("mono", "Белый", "#F2F2F2"), new("vibe", "По вайбу"), new("cover", "По обложке"), new("lime", "Лайм", "#C8F53C"), new("coral", "Коралл", "#FF6A55"),
        new("lilac", "Сирень", "#B38CFF"), new("mint", "Мята", "#45E0B0"), new("amber", "Янтарь", "#FFB43D"), new("sky", "Небо", "#58B2FF"),
        new("pink", "Розовый", "#FF5CA8"), new("red", "Красный", "#FF3B3B"),
    };

    // Bg, Panel, Chip, ModeBox, Line
    public static readonly (string Id, string Title, string[] Colors)[] Backgrounds =
    {
        ("black", "Чёрный", new[] { "#000000", "#131313", "#222222", "#2C2C2C", "#2A2A2A" }),
        ("graphite", "Графит", new[] { "#111114", "#1B1B1F", "#26262B", "#34343B", "#303037" }),
        ("midnight", "Полночь", new[] { "#04060D", "#0D1322", "#161E35", "#222C4A", "#1F2943" }),
        ("warm", "Тёплый", new[] { "#0C0907", "#19140F", "#251D17", "#332821", "#2D241D" }),
        ("forest", "Лес", new[] { "#050A07", "#0E1812", "#16241B", "#213327", "#1C2C22" }),
        ("cover", "По обложке", Array.Empty<string>()),
    };

    public static readonly (string Id, string Title, string Family)[] Fonts =
    {
        ("system", "Обычный", "Segoe UI Variable Display, Segoe UI"),
        ("techno", "Техно", "Bahnschrift, Segoe UI"),
        ("serif", "С засечками", "Georgia, Cambria, Times New Roman"),
        ("mono", "Моно", "Cascadia Mono, Consolas"),
    };

    /// <summary>The colour of the playing cover (bright version), for «По обложке».</summary>
    public static Color CoverColor { get; private set; } = Color.FromRgb(200, 200, 200);

    /// <summary>Anything drawn in code (visualizer, rings) repaints on this.</summary>
    public static event Action? Changed;

    private static LookSettings L => App.Hub.Settings.Look;

    /// <summary>The accent right now: a fixed colour, white, the cover's, or the nebula's («По вайбу»).</summary>
    public static Color Accent => L.Accent == "cover" ? CoverColor : L.Accent == "vibe" ? VibeColor : Hex(Accents.FirstOrDefault(a => a.Id == L.Accent)?.Hex ?? "#F2F2F2");

    // «По вайбу»: the light, pastel side of the nebula now on screen
    private static readonly Dictionary<string, string> Vibes = new() { ["space"] = "#C4B2FF", ["aurora"] = "#86ECD0", ["nova"] = "#FFB08A", ["ice"] = "#9FD0FF" };
    private static string _vibe = "space";
    public static Color VibeColor => Hex(Vibes.GetValueOrDefault(_vibe, "#C4B2FF"));

    public static void SetVibe(string scheme)
    {
        if (scheme == _vibe) return;
        _vibe = scheme;
        if (L.Accent == "vibe") Apply();
    }

    /// <summary>Colour for the visualizer: the accent, unless it is plain white (then the cover's).</summary>
    public static Color VizColor => L.Accent is "mono" or "cover" ? CoverColor : Accent;

    /// <summary>For «По вайбу»: accent surfaces get a soft depth (light top, deeper bottom) instead of a flat fill.</summary>
    private static Brush AccentBrush(Color c)
    {
        if (L.Accent != "vibe") return Frozen(c);
        var deep = Color.FromRgb((byte)(c.R * 0.72 + 26 * 0.28), (byte)(c.G * 0.72 + 16 * 0.28), (byte)(c.B * 0.72 + 48 * 0.28));
        var light = Color.FromRgb((byte)Math.Min(255, c.R * 0.92 + 255 * 0.08), (byte)Math.Min(255, c.G * 0.92 + 255 * 0.08), (byte)Math.Min(255, c.B * 0.92 + 255 * 0.08));
        var b = new LinearGradientBrush(light, deep, 70);
        b.Freeze();
        return b;
    }

    public static void SetCoverColor(Color c)
    {
        CoverColor = c;
        if (L.Accent == "cover" || L.Bg == "cover") Apply();
        else Changed?.Invoke();
    }

    public static void Apply()
    {
        var r = Application.Current.Resources;
        void Brush(string key, Color c) => r[key] = Frozen(c);

        // background family
        string[] bg;
        if (L.Bg == "cover")
        {
            var c = CoverColor;
            string M(double k, int add) => $"#{Ch(c.R * k + add):X2}{Ch(c.G * k + add):X2}{Ch(c.B * k + add):X2}";
            bg = new[] { M(0.055, 0), M(0.1, 8), M(0.13, 14), M(0.19, 20), M(0.15, 14) };
        }
        else bg = Backgrounds.FirstOrDefault(b => b.Id == L.Bg).Colors ?? Backgrounds[0].Colors;
        if (bg.Length == 0) bg = Backgrounds[0].Colors;
        Brush("Bg", Hex(bg[0]));
        Brush("Panel", Hex(bg[1]));
        Brush("Chip", Hex(bg[2]));
        Brush("ChipHover", Lighten(Hex(bg[2]), 10));
        Brush("ModeBox", Hex(bg[3]));
        Brush("Line", L.Contrast == "high" ? Hex("#4A4A4A") : Hex(bg[4]));

        Brush("Muted", Hex(L.Contrast == "high" ? "#C2C2C2" : "#8A8A8A"));
        Brush("Dim", Hex(L.Contrast == "high" ? "#A3A3A3" : "#7A7A7A"));

        var ac = Accent;
        r["Accent"] = AccentBrush(ac);
        Brush("OnAccent", (0.2126 * ac.R + 0.7152 * ac.G + 0.0722 * ac.B) / 255 > 0.5 ? Colors.Black : Colors.White);

        var (s, m, l) = L.Shape switch { "sharp" => (2.0, 3.0, 3.0), "pill" => (17.0, 17.0, 20.0), _ => (8.0, 9.0, 10.0) };
        r["R8"] = new CornerRadius(s);
        r["R9"] = new CornerRadius(m);
        r["R10"] = new CornerRadius(l);

        r["UiFont"] = new FontFamily(Fonts.FirstOrDefault(f => f.Id == L.Font).Family ?? Fonts[0].Family);
        Changed?.Invoke();
    }

    public static void Save() { App.Hub.Settings.Save(); Apply(); }

    public static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
    private static byte Ch(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
    private static Color Lighten(Color c, int d) => Color.FromRgb(Ch(c.R + d), Ch(c.G + d), Ch(c.B + d));
    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
