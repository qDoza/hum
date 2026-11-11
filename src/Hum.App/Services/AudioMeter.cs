using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace Hum.App.Services;

/// <summary>
/// Listens to what the speakers play (WASAPI loopback) and turns it into a spectrum: 64 log-spaced bands
/// from 30 Hz to 16 kHz plus bass / mid / high levels and a «hit» (bass attack) for the visualizer.
/// Runs only while someone looks at it (<see cref="Start"/> / <see cref="Stop"/>).
/// </summary>
public sealed class AudioMeter : IDisposable
{
    public const int Bands = 64;
    private const int N = 2048, M = 11;          // FFT size and log2 of it

    private WasapiLoopbackCapture? _cap;
    private readonly float[] _ring = new float[N];
    private int _ringPos, _fresh;
    private readonly Complex[] _fft = new Complex[N];
    private readonly float[] _window = new float[N];
    private readonly float[] _peak = new float[Bands];
    private readonly float[] _bands = new float[Bands];
    private float _low, _mid, _high, _hit, _prevLow, _sampleRate = 48000;
    private long _stamp;
    private readonly object _gate = new();

    public bool Running => _cap is not null;
    public bool Failed { get; private set; }

    public AudioMeter()
    {
        for (var i = 0; i < N; i++) _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));
        for (var i = 0; i < Bands; i++) _peak[i] = 1e-4f;
    }

    public void Start()
    {
        if (_cap is not null || Failed) return;
        try
        {
            _cap = new WasapiLoopbackCapture();
            _sampleRate = _cap.WaveFormat.SampleRate;
            _cap.DataAvailable += OnData;
            _cap.RecordingStopped += (_, e) => { if (e.Exception is not null) Log.Write("meter stopped: " + e.Exception.Message); };
            _cap.StartRecording();
        }
        catch (Exception e)
        {
            Log.Write("meter: " + e.Message);
            Failed = true;
            _cap?.Dispose();
            _cap = null;
        }
    }

    public void Stop()
    {
        var c = _cap;
        _cap = null;
        if (c is null) return;
        try { c.StopRecording(); } catch { }
        c.Dispose();
    }

    /// <summary>Copies the latest spectrum into <paramref name="bands"/>; returns false when there is no fresh sound.</summary>
    public bool Read(float[] bands, out float low, out float mid, out float high, out float hit)
    {
        lock (_gate)
        {
            var fresh = Environment.TickCount64 - _stamp < 200;
            Array.Copy(_bands, bands, Bands);
            low = fresh ? _low : 0; mid = fresh ? _mid : 0; high = fresh ? _high : 0; hit = fresh ? _hit : 0;
            if (!fresh) Array.Clear(bands);
            return fresh;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var wf = _cap?.WaveFormat;
        if (wf is null || e.BytesRecorded == 0) return;
        var ch = wf.Channels;
        var floats = wf.Encoding == WaveFormatEncoding.IeeeFloat || wf.Encoding == WaveFormatEncoding.Extensible && wf.BitsPerSample == 32;
        if (!floats) return;
        var frames = e.BytesRecorded / (4 * ch);
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < ch; c++) sum += BitConverter.ToSingle(e.Buffer, (f * ch + c) * 4);
            _ring[_ringPos] = sum / ch;
            _ringPos = (_ringPos + 1) % N;
            if (++_fresh >= N / 2) { _fresh = 0; Analyse(); }
        }
    }

    private void Analyse()
    {
        for (var i = 0; i < N; i++)
        {
            _fft[i].X = _ring[(_ringPos + i) % N] * _window[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, M, _fft);
        var binHz = _sampleRate / N;
        float lo = 0, mi = 0, hi = 0;
        var outBands = new float[Bands];
        for (var b = 0; b < Bands; b++)
        {
            // log-spaced edges, 30 Hz .. 16 kHz
            var f0 = 30 * Math.Pow(16000.0 / 30, b / (double)Bands);
            var f1 = 30 * Math.Pow(16000.0 / 30, (b + 1) / (double)Bands);
            int i0 = Math.Max(1, (int)(f0 / binHz)), i1 = Math.Max(i0 + 1, (int)(f1 / binHz));
            float m = 0;
            for (var i = i0; i < i1 && i < N / 2; i++) m = Math.Max(m, MathF.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y));
            // automatic gain per band: compare with its own recent peak, which fades slowly
            _peak[b] = Math.Max(m, _peak[b] * 0.997f);
            var v = m / Math.Max(_peak[b], 1e-4f);
            v = MathF.Pow(v, 1.4f);
            outBands[b] = v;
            var fc = (f0 + f1) / 2;
            if (fc < 160) lo = Math.Max(lo, v); else if (fc < 2500) mi += v; else if (fc > 4000) hi += v;
        }
        mi /= 28; hi /= 14;   // how many bands fall into each range
        lock (_gate)
        {
            Array.Copy(outBands, _bands, Bands);
            _hit = Math.Clamp((lo - _prevLow) * 3, 0, 1);
            _prevLow = lo;
            _low = lo; _mid = Math.Min(1.2f, mi); _high = Math.Min(1.2f, hi);
            _stamp = Environment.TickCount64;
        }
    }

    public void Dispose() => Stop();
}
