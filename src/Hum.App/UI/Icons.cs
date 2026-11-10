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
