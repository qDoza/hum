using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hum.App.Services;

/// <summary>
/// «Слушает HUM» in Discord via the local Discord client's IPC pipe (the same channel the official
/// Rich Presence SDK uses). Needs an Application ID from discord.com/developers — its name is what
/// Discord shows after «Слушает». Everything runs on a background task; failures only get logged.
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private JsonNode? _pending;     // latest activity to send (JsonValue null = clear)
    private bool _dirty;
    private string? _appId;

    public bool IsConnected { get; private set; }

    public DiscordPresence() => Task.Run(Loop);

    public void Configure(string? appId)
    {
        lock (_gate) { _appId = string.IsNullOrWhiteSpace(appId) ? null : appId.Trim(); _dirty = true; }
        _wake.Release();
    }

    /// <summary>Shows a track. Position/duration in ms; when paused the time bar is hidden.</summary>
    public void SetTrack(string title, string artist, string? album, string? coverUrl, string sourceName, bool playing, double positionMs, double durationMs)
    {
        var a = new JsonObject
        {
            ["type"] = 2,                                           // «Слушает»
            ["details"] = Clip(title),
            ["state"] = Clip(playing ? artist : "На паузе · " + artist),
            ["assets"] = new JsonObject
            {
                ["large_image"] = coverUrl ?? "hum",
                ["large_text"] = Clip(album is { Length: > 0 } ? album : sourceName),
            },
        };
        if (playing && durationMs > 0)
        {
            var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)positionMs;
            a["timestamps"] = new JsonObject { ["start"] = start, ["end"] = start + (long)durationMs };
        }
        Push(a);
    }

    public void Clear() => Push(null);

    private void Push(JsonNode? activity)
    {
        lock (_gate) { _pending = activity; _dirty = true; }
        _wake.Release();
    }

    private static string Clip(string s) => s.Length < 2 ? s + "  " : s.Length > 120 ? s[..120] : s;

    // ------------------------------------------------------------------ IPC

    private async Task Loop()
    {
        NamedPipeClientStream? pipe = null;
        string? connectedId = null;
        while (!_stop.IsCancellationRequested)
        {
            await _wake.WaitAsync(TimeSpan.FromSeconds(20), _stop.Token).ContinueWith(_ => { });
            string? appId; JsonNode? activity; bool dirty;
            lock (_gate) { appId = _appId; activity = _pending?.DeepClone(); dirty = _dirty; _dirty = false; }
            try
            {
                if (appId is null)
                {
                    pipe?.Dispose(); pipe = null; IsConnected = false; connectedId = null;
                    continue;
                }
                if (pipe is null || !pipe.IsConnected || connectedId != appId)
                {
                    pipe?.Dispose();
                    pipe = await ConnectAsync(appId);
                    connectedId = pipe is null ? null : appId;
                    IsConnected = pipe is not null;
                    if (pipe is null) { lock (_gate) _dirty = true; continue; }   // Discord not running: retry later
                    dirty = true;
                }
                if (!dirty) continue;
                var msg = new JsonObject
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity },
                    ["nonce"] = Guid.NewGuid().ToString(),
                };
                await WriteAsync(pipe, 1, msg);
                await ReadAsync(pipe);
            }
            catch (Exception e)
            {
                Log.Write("discord: " + e.Message);
                pipe?.Dispose(); pipe = null; IsConnected = false;
                lock (_gate) _dirty = true;
            }
        }
        pipe?.Dispose();
    }

    private static async Task<NamedPipeClientStream?> ConnectAsync(string appId)
    {
        for (var i = 0; i < 10; i++)
        {
            var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await p.ConnectAsync(150);
                await WriteAsync(p, 0, new JsonObject { ["v"] = 1, ["client_id"] = appId });
                var (op, body) = await ReadAsync(p);
                if (op == 1 && body.Contains("READY")) { Log.Write("discord: connected"); return p; }
                Log.Write("discord handshake: " + body);
                p.Dispose();
                return null;
            }
            catch (TimeoutException) { p.Dispose(); }
            catch (IOException) { p.Dispose(); }
        }
        return null;
    }

    private static async Task WriteAsync(Stream s, int op, JsonNode payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await s.WriteAsync(frame);
        await s.FlushAsync();
    }

    private static async Task<(int Op, string Body)> ReadAsync(Stream s)
    {
        var head = new byte[8];
        await s.ReadExactlyAsync(head);
        var len = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
        var body = new byte[len];
        await s.ReadExactlyAsync(body);
        return (BinaryPrimitives.ReadInt32LittleEndian(head), Encoding.UTF8.GetString(body));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _wake.Release();
    }
}
