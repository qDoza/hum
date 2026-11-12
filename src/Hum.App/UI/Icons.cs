using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hum.App.UI;

/// <summary>
/// Player icons drawn on one 24×24 grid with the same stroke and round joins, so every corner is soft
/// and they look like one set (the Segoe glyphs mixed thin outlines with heavy fills).
/// The colour follows the hosting button's Foreground.
/// </summary>
public static class Icons
{
    private record Part(Geometry Geo, bool Fill);

    private static readonly Dictionary<string, Part[]> Set = new()
    {
        ["play"] = new[] { P("M8.5,6.2 C8.5,5.4 9.4,4.9 10.1,5.4 L18.2,11.1 C18.8,11.5 18.8,12.5 18.2,12.9 L10.1,18.6 C9.4,19.1 8.5,18.6 8.5,17.8 Z", true) },
        ["pause"] = new[] { P("M7,6.5 C7,5.7 7.6,5 8.5,5 C9.4,5 10,5.7 10,6.5 V17.5 C10,18.3 9.4,19 8.5,19 C7.6,19 7,18.3 7,17.5 Z M14,6.5 C14,5.7 14.6,5 15.5,5 C16.4,5 17,5.7 17,6.5 V17.5 C17,18.3 16.4,19 15.5,19 C14.6,19 14,18.3 14,17.5 Z", true) },
        ["next"] = new[] { P("M5.5,7.4 C5.5,6.6 6.4,6.1 7.1,6.6 L13.6,11.2 C14.2,11.6 14.2,12.4 13.6,12.8 L7.1,17.4 C6.4,17.9 5.5,17.4 5.5,16.6 Z", true), P("M18,6.5 V17.5", false) },
        ["prev"] = new[] { P("M18.5,7.4 C18.5,6.6 17.6,6.1 16.9,6.6 L10.4,11.2 C9.8,11.6 9.8,12.4 10.4,12.8 L16.9,17.4 C17.6,17.9 18.5,17.4 18.5,16.6 Z", true), P("M6,6.5 V17.5", false) },
        ["heart"] = new[] { P("M12,19.5 C6.5,16 4,12.8 4,9.6 C4,7.2 5.8,5.5 8,5.5 C9.6,5.5 11,6.4 12,7.8 C13,6.4 14.4,5.5 16,5.5 C18.2,5.5 20,7.2 20,9.6 C20,12.8 17.5,16 12,19.5 Z", false) },
        ["heart-fill"] = new[] { P("M12,19.5 C6.5,16 4,12.8 4,9.6 C4,7.2 5.8,5.5 8,5.5 C9.6,5.5 11,6.4 12,7.8 C13,6.4 14.4,5.5 16,5.5 C18.2,5.5 20,7.2 20,9.6 C20,12.8 17.5,16 12,19.5 Z", true) },
        ["dislike"] = new[] { P("M12,4.5 A7.5,7.5 0 1 1 11.99,4.5 Z", false), P("M6.8,17.2 L17.2,6.8", false) },
        ["shuffle"] = new[] { P("M4,7 H7 C12,7 12,17 17,17 H20 M4,17 H7 C9,17 10,15.5 11,14 M14,9.5 C15,8 16,7 17,7 H20 M18,4 L21,7 L18,10 M18,14 L21,17 L18,20", false) },
        ["repeat"] = new[] { P("M5,11 V9.5 A2.5,2.5 0 0 1 7.5,7 H19 L16,4 M19,13 V14.5 A2.5,2.5 0 0 1 16.5,17 H5 L8,20", false) },
        ["lyrics"] = new[] { P("M5,6.5 H19 M5,11.5 H19 M5,16.5 H12", false), P("M17,15 V19.5", false), P("M15.6,17.8 A1.6,1.6 0 1 1 15.59,17.8 Z", true) },
        ["down"] = new[] { P("M6,9.5 L12,15.5 L18,9.5", false) },
        ["tune"] = new[] { P("M4,7.5 H13 M17,7.5 H20 M4,16.5 H7 M11,16.5 H20", false), P("M15,5.3 A2.2,2.2 0 1 1 14.99,5.3 Z M9,14.3 A2.2,2.2 0 1 1 8.99,14.3 Z", false) },
        ["back"] = new[] { P("M14.5,6 L8.5,12 L14.5,18", false) },
        ["palette"] = new[] { P("M12,4 A8,8 0 1 0 12,20 C13.1,20 13.7,19.2 13.7,18.4 C13.7,17.9 13.5,17.6 13.3,17.3 C13,17 12.9,16.7 12.9,16.3 C12.9,15.4 13.6,14.8 14.5,14.8 H16 A4,4 0 0 0 20,10.8 C20,7.1 16.4,4 12,4 Z", false), P("M8,9.7 A1.3,1.3 0 1 1 7.99,9.7 Z M10.5,6.3 A1.3,1.3 0 1 1 10.49,6.3 Z M14.6,6.5 A1.3,1.3 0 1 1 14.59,6.5 Z", true) },
        ["hum"] = new[] { P("M12,3 A9,9 0 1 1 11.99,3 Z", false), P("M5,12 C6.15,9.5 7.35,9.5 8.5,12 S10.85,14.5 12,12 S14.35,9.5 15.5,12 S17.85,14.5 19,12", false) },
        ["vol0"] = new[] { Speaker(), P("M16,9.5 L20,13.5 M20,9.5 L16,13.5", false) },
        ["vol1"] = new[] { Speaker(), P("M15.5,9.5 C16.6,10.6 16.6,13.4 15.5,14.5", false) },
        ["vol2"] = new[] { Speaker(), P("M15.5,9.5 C16.6,10.6 16.6,13.4 15.5,14.5", false), P("M18,7.5 C20.3,9.8 20.3,14.2 18,16.5", false) },
    };

    private static Part Speaker() => P("M4.5,10 C4.5,9.4 4.9,9 5.5,9 H8 L11.4,6.3 C12,5.8 12.8,6.2 12.8,7 V17 C12.8,17.8 12,18.2 11.4,17.7 L8,15 H5.5 C4.9,15 4.5,14.6 4.5,14 Z", true);

    private static Part P(string data, bool fill)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return new Part(g, fill);
    }

    /// <summary>A sized icon whose colour is bound to the nearest Button's Foreground.</summary>
    public static FrameworkElement Make(string name, double size = 18, double stroke = 1.9)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var part in Set[name])
        {
            var path = new Path
            {
                Data = part.Geo,
                StrokeThickness = stroke,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            var fg = new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) };
            path.SetBinding(Shape.StrokeProperty, fg);
            if (part.Fill) path.SetBinding(Shape.FillProperty, fg);
            canvas.Children.Add(path);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>Swaps a button's icon only when it actually changes (no flicker on every refresh).</summary>
    public static void Set_(ContentControl target, string name, double size = 18)
    {
        if (target.Tag as string == name) return;
        target.Tag = name;
        target.Content = Make(name, size);
    }
}
