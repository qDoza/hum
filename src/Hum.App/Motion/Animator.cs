using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Hum.App.Motion;

/// <summary>
/// One frame clock for every hand-written animation in the app (springs, liquid blobs, tweens).
/// Hooks CompositionTarget.Rendering only while something is moving.
/// </summary>
public static class Animator
{
    private static readonly List<Func<double, bool>> Active = new();
    private static TimeSpan _last = TimeSpan.Zero;
    private static bool _hooked;

    /// <summary>Adds a per-frame callback. It receives dt in seconds and returns false when finished.</summary>
    public static void Run(Func<double, bool> tick)
    {
        Active.Add(tick);
        if (_hooked) return;
        _hooked = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>Time-based tween: progress 0..1 goes to <paramref name="apply"/>. Returns a handle to cancel it.</summary>
    public static TweenHandle Tween(double seconds, Action<double> apply, Action? done = null, double delay = 0)
    {
        var handle = new TweenHandle();
        double t = -delay;
        Run(dt =>
        {
            if (handle.Cancelled) return false;
            t += dt;
            if (t < 0) return true;
            var p = Math.Min(1, t / seconds);
            apply(p);
            if (p < 1) return true;
            done?.Invoke();
            return false;
        });
        return handle;
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;                  // Rendering can fire twice per frame
        var dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;

        for (var i = Active.Count - 1; i >= 0; i--)
        {
            if (i >= Active.Count) continue;
            if (!Active[i](dt)) Active.RemoveAt(i);
        }

        if (Active.Count == 0)
        {
            CompositionTarget.Rendering -= OnFrame;
            _hooked = false;
        }
    }
}

public sealed class TweenHandle
{
    public bool Cancelled { get; private set; }
    public void Cancel() => Cancelled = true;
}

/// <summary>
/// Damped spring integrated in 1 ms steps, so it feels identical at 60/120/144 Hz.
/// Defaults: ~12.7 rad/s with damping ratio 0.68 → one soft ~5% overshoot, then it settles.
/// </summary>
public sealed class Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public double Omega = 12.7;
    public double Zeta = 0.68;

    public Spring(double value = 0) { Value = Target = value; }

    /// <summary>Advances the spring; returns true while it is still moving.</summary>
    public bool Step(double dt)
    {
        var k = Omega * Omega;
        var c = 2 * Zeta * Omega;
        const double h = 0.001;
        for (var t = 0.0; t < dt; t += h)
        {
            var a = k * (Target - Value) - c * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
        if (Math.Abs(Target - Value) < 0.0005 && Math.Abs(Velocity) < 0.0005)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }
        return true;
    }
}

public static class Ease
{
    public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double OutQuart(double t) => 1 - Math.Pow(1 - t, 4);
    public static double OutExpo(double t) => t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t);
    public static double InOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>Ease-out with a small overshoot. s≈0.6 gives ~3%, s≈1.7 the classic back-ease.</summary>
    public static double OutBack(double t, double s = 1.0)
    {
        var c3 = s + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + s * Math.Pow(t - 1, 2);
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
}

