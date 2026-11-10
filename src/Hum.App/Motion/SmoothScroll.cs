using System;
using System.Windows;
using System.Windows.Controls;

namespace Hum.App.Motion;

/// <summary>Replaces WPF's stepped wheel scrolling with a spring-smoothed one.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(string), typeof(SmoothScroll), new PropertyMetadata(null, OnChanged));

    public static void SetMode(ScrollViewer sv, string v) => sv.SetValue(ModeProperty, v);
    public static string GetMode(ScrollViewer sv) => (string)sv.GetValue(ModeProperty);

    private static readonly DependencyProperty ScrollerProperty = DependencyProperty.RegisterAttached(
        "Scroller", typeof(Scroller), typeof(SmoothScroll));

    private sealed class Scroller(ScrollViewer sv, bool horizontal)
    {
        private readonly Spring _spring = new() { Omega = 16, Zeta = 1.0 };
        private bool _running;

        private double Current => horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
        private double Max => horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;

        public bool CanScroll => Max > 0;

        public void By(double delta)
        {
            if (!_running) _spring.Value = _spring.Target = Current;
            To(_spring.Target + delta);
        }

        public void To(double target)
        {
            if (!_running) _spring.Value = Current;
            _spring.Target = Math.Clamp(target, 0, Max);
            if (_running) return;
            _running = true;
            Animator.Run(dt =>
            {
                var moving = _spring.Step(dt);
                if (horizontal) sv.ScrollToHorizontalOffset(_spring.Value);
                else sv.ScrollToVerticalOffset(_spring.Value);
                _running = moving;
                return moving;
            });
        }
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv || e.NewValue is not string mode) return;
        var scroller = new Scroller(sv, mode == "Horizontal");
        sv.SetValue(ScrollerProperty, scroller);
        sv.PreviewMouseWheel += (_, args) =>
        {
            if (!scroller.CanScroll) return;            // let the parent scroll instead
            args.Handled = true;
            scroller.By(-args.Delta * 0.6);
        };
    }

    /// <summary>Smoothly scrolls to an offset (used to bring the selected preset into view).</summary>
    public static void AnimateTo(ScrollViewer sv, double offset)
    {
        if (sv.GetValue(ScrollerProperty) is Scroller s) s.To(offset);
        else if (GetMode(sv) == "Horizontal") sv.ScrollToHorizontalOffset(offset);
        else sv.ScrollToVerticalOffset(offset);
    }
}

