using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using QRCoder;

namespace Hum.App.Services;

/// <summary>
/// Moves the accounts to the iPhone app with a short one-time code.
/// The tokens are encrypted here (AES-256-GCM); the web app's Worker keeps only the ciphertext under a
/// 6-letter id for a few minutes and deletes it on first read. The key travels only in the QR link's
/// #fragment, which browsers never send to a server — so the Worker can't read the tokens.
/// </summary>
public static class Pairing
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<string> CreateLinkAsync(string? yaToken, string? scToken, string baseUrl, int ttlSeconds = 900)
    {
        var payload = new JsonObject();
        if (!string.IsNullOrEmpty(yaToken)) payload["ya"] = yaToken;
        if (!string.IsNullOrEmpty(scToken)) payload["sc"] = scToken;
        var plain = Encoding.UTF8.GetBytes(payload.ToJsonString());

        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(iv, plain, cipher, tag);

        // WebCrypto expects ciphertext‖tag; prepend the IV so the phone has everything in one blob
        var blob = new byte[12 + cipher.Length + 16];
        iv.CopyTo(blob, 0);
        cipher.CopyTo(blob, 12);
        tag.CopyTo(blob, 12 + cipher.Length);

        var baseTrim = baseUrl.TrimEnd('/');
        using var r = await Http.PostAsJsonAsync(baseTrim + "/api/pair", new { blob = B64Url(blob), ttl = ttlSeconds });
        var res = await r.Content.ReadFromJsonAsync<JsonObject>();
        if (!r.IsSuccessStatusCode || res?["id"]?.ToString() is not { Length: 6 } id)
            throw new InvalidOperationException("сервер HUM не принял код: " + (res?["error"]?.ToString() ?? r.StatusCode.ToString()));
        return $"{baseTrim}/#p={id}.{B64Url(key)}";
    }

    /// <summary>A crisp QR (black on white, quiet zone) as PNG bytes.</summary>
    public static byte[] QrPng(string text, int pixelsPerModule = 10) =>
        new PngByteQRCode(new QRCodeGenerator().CreateQrCode(text, QRCodeGenerator.ECCLevel.M))
            .GetGraphic(pixelsPerModule, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, true);

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
