using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hum.App.Motion;

/// <summary>
/// The "liquid" selection indicator: the leading edge runs toward the target, the tail
/// follows a moment later and overshoots a touch, and the blob squashes slightly mid-way.
/// The blob must live in a Canvas; its position is Canvas.Left + Width.
/// </summary>
public static class Liquid
{
    private static readonly ConditionalWeakTable<FrameworkElement, TweenHandle> Running = new();

    public static void MoveTo(FrameworkElement blob, double x1, double w1, bool animate = true, double seconds = 0.72)
    {
        var x0 = Canvas.GetLeft(blob);
        var w0 = blob.Width;
        if (double.IsNaN(x0) || double.IsNaN(w0) || !animate || (Math.Abs(x0 - x1) < 0.5 && Math.Abs(w0 - w1) < 0.5))
        {
            if (Running.TryGetValue(blob, out var old)) old.Cancel();
            Canvas.SetLeft(blob, x1);
            blob.Width = w1;
            SetScaleY(blob, 1);
            return;
        }

        if (Running.TryGetValue(blob, out var prev)) prev.Cancel();
        var fwd = x1 > x0;
        double r0 = x0 + w0, r1 = x1 + w1;

        var handle = Animator.Tween(seconds, t =>
        {
            var lead = Ease.OutBack(Ease.Clamp01(t / 0.7), 0.5);
            var tail = Ease.OutBack(Ease.Clamp01((t - 0.12) / 0.88), 0.9);
            var l = fwd ? Ease.Lerp(x0, x1, tail) : Ease.Lerp(x0, x1, lead);
            var r = fwd ? Ease.Lerp(r0, r1, lead) : Ease.Lerp(r0, r1, tail);
            if (r - l < 4) r = l + 4;
            Canvas.SetLeft(blob, l);
            blob.Width = r - l;
            SetScaleY(blob, 1 - 0.09 * Math.Sin(Math.PI * Math.Min(1, t * 1.15)));
        });
        Running.AddOrUpdate(blob, handle);
    }

    private static void SetScaleY(FrameworkElement el, double s)
    {
        if (el.RenderTransform is not ScaleTransform st)
        {
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = st = new ScaleTransform();
        }
        st.ScaleY = s;
    }
}

