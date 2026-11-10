using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Hum.App.Models;
using Hum.App.Services;
using Microsoft.Web.WebView2.Core;

namespace Hum.App.UI;

/// <summary>
/// Sign-in inside an embedded Edge: Yandex hands the token back in the redirect URL,
/// SoundCloud keeps it in the oauth_token cookie (and its API calls reveal the client_id).
/// </summary>
public partial class LoginWindow : Window
{
    private readonly Source _source;
    private readonly DispatcherTimer _poll;

    public string? Token { get; private set; }
    public string? ClientId { get; private set; }

    public LoginWindow(Source source)
    {
        InitializeComponent();
        _source = source;
        HeaderText.Text = source == Source.Yandex ? "Вход в Яндекс" : "Вход в SoundCloud";
        Hint.Text = source == Source.Yandex
            ? "Войдите в аккаунт Яндекса. Окно закроется само, как только вход завершится."
            : "Войдите в SoundCloud. Окно закроется само, как только вход завершится.";
        CloseBtn.Click += (_, _) => { DialogResult = false; };
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(800), DispatcherPriority.Background, async (_, _) => await Poll(), Dispatcher);
        _poll.Stop();
        Loaded += async (_, _) => await Start();
        Closed += (_, _) => _poll.Stop();
    }

    private async System.Threading.Tasks.Task Start()
    {
        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hum", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await Web.EnsureCoreWebView2Async(env);
            var core = Web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.SourceChanged += (_, _) => CheckUrl(core.Source);
            core.NavigationStarting += (_, e) => CheckUrl(e.Uri);
            core.NavigationCompleted += (_, _) => { Wait.Visibility = Visibility.Collapsed; Web.Visibility = Visibility.Visible; };

            if (_source == Source.SoundCloud)
            {
                core.AddWebResourceRequestedFilter("https://api-v2.soundcloud.com/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    var m = Regex.Match(e.Request.Uri, "[?&]client_id=([A-Za-z0-9]{32})");
                    if (m.Success) ClientId = m.Groups[1].Value;
                };
                core.Navigate(SoundCloudClient.SignInUrl);
                _poll.Start();
            }
            else core.Navigate(YandexClient.OAuthUrl);
        }
        catch (Exception e)
        {
            Log.Write("webview: " + e);
            Wait.Text = "Не удалось открыть встроенный браузер (нужен Microsoft Edge WebView2).";
        }
    }

    private void CheckUrl(string? url)
    {
        if (_source != Source.Yandex || url is null) return;
        var m = Regex.Match(url, "access_token=([^&#]+)");
        if (!m.Success) return;
        Token = HttpUtility.UrlDecode(m.Groups[1].Value);
        DialogResult = true;
    }

    private async System.Threading.Tasks.Task Poll()
    {
        if (Web.CoreWebView2 is null || !IsVisible) return;
        try
        {
            var cookies = await Web.CoreWebView2.CookieManager.GetCookiesAsync("https://soundcloud.com");
            var token = cookies.FirstOrDefault(c => c.Name == "oauth_token")?.Value;
            if (string.IsNullOrEmpty(token)) return;
            _poll.Stop();
            Token = token;
            DialogResult = true;
        }
        catch (Exception e) { Log.Write("sc cookie poll: " + e.Message); }
    }
}
