using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Hum.App.Motion;

/// <summary>Attached behaviour: the element sinks a little when pressed and springs back on release.</summary>
public static class Press
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Press), new PropertyMetadata(false, OnChanged));

    public static void SetEnabled(UIElement el, bool v) => el.SetValue(EnabledProperty, v);
    public static bool GetEnabled(UIElement el) => (bool)el.GetValue(EnabledProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || e.NewValue is not true) return;
        var spring = new Spring(1) { Omega = 22, Zeta = 0.55 };
        var running = false;

        void Go(double target)
        {
            spring.Target = target;
            if (running) return;
            running = true;
            Animator.Run(dt =>
            {
                var moving = spring.Step(dt);
                if (el.RenderTransform is not ScaleTransform st)
                {
                    el.RenderTransformOrigin = new Point(0.5, 0.5);
                    el.RenderTransform = st = new ScaleTransform();
                }
                st.ScaleX = st.ScaleY = spring.Value;
                running = moving;
                return moving;
            });
        }

        el.PreviewMouseLeftButtonDown += (_, _) => Go(0.94);
        el.PreviewMouseLeftButtonUp += (_, _) => Go(1);
        el.MouseLeave += (_, _) => Go(1);
    }
}

