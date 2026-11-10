using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hum.App.Models;
using Hum.App.Motion;
using Hum.App.UI;

namespace Hum.App;

/// <summary>The big window: same pages as the flyout plus a full player bar and account management.</summary>
public partial class MainWindow : Window
{
    private int _tab;
    private readonly Button[] _tabs;
    private TweenHandle? _toastTween;

    public bool Quitting { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _tabs = new[] { TabWave, TabLikes, TabSearch };
        Bar.Variant = NowPlayingVariant.Bar;

        TabWave.Click += (_, _) => ShowTab(0);
        TabLikes.Click += (_, _) => ShowTab(1);
        TabSearch.Click += (_, _) => ShowTab(2);
        Header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseBtn.Click += (_, _) => Hide();
        AccountsBtn.Click += (_, _) => { BuildAccounts(); AccountsPopup.IsOpen = true; };
        CoverPage.CloseRequested += () => CoverPage.Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && CoverPage.IsVisible) { CoverPage.Close(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { ShowTab(2); Search.FocusQuery(); }
            else if (e.Key == Key.Space && e.OriginalSource is not TextBox) { App.Hub.Player.Toggle(); e.Handled = true; }
        };
        StateChanged += (_, _) => Card.Margin = new Thickness(WindowState == WindowState.Maximized ? 0 : 10);

        App.Hub.Toast += ShowToast;
        App.Hub.Library.Changed += () => { if (AccountsPopup.IsOpen) BuildAccounts(); };
        Loaded += (_, _) => ShowTab(_tab, false);
        Closing += (_, e) => { if (!Quitting) { e.Cancel = true; Hide(); } };
    }

    public void BringUp()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;   // reliably in front even when another app is active
    }

    /// <summary>Opens the big cover with lyrics on top of everything.</summary>
    public void ShowCover()
    {
        BringUp();
        if (!CoverPage.IsVisible) CoverPage.Open();
    }

    private void ShowTab(int tab, bool animate = true)
    {
        if (CoverPage.IsVisible) CoverPage.Close();
        _tab = tab;
        Tabs.Paint(_tabs, _tabs[tab]);
        Tabs.Place(TabHost, TabBlob, _tabs[tab], animate);
        var pages = new FrameworkElement[] { WaveHost, Likes, Search };
        for (var i = 0; i < pages.Length; i++)
        {
            var p = pages[i];
            if (i != tab) { p.Visibility = Visibility.Collapsed; continue; }
            p.Visibility = Visibility.Visible;
            if (!animate) continue;
            var tr = new TranslateTransform(0, 8);
            p.RenderTransform = tr;
            Animator.Tween(0.5, t => { var e = Ease.OutExpo(t); p.Opacity = e; tr.Y = 8 * (1 - e); });
        }
        if (tab == 2) Search.FocusQuery();
    }

    private void BuildAccounts()
    {
        AccountsList.Children.Clear();
        AccountsList.Children.Add(new TextBlock { Text = "Аккаунты", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var s in new[] { Source.Yandex, Source.SoundCloud })
        {
            var lib = App.Hub.Library;
            var connected = lib.Has(s);
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = s == Source.Yandex ? "Яндекс" : "SoundCloud", FontSize = 13, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock
            {
                Text = connected ? (s == Source.Yandex ? lib.Ya?.Name : lib.Sc?.Name) + $" · {lib.Count(s)} лайков" : "не подключён",
                FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            row.Children.Add(info);
            var btn = new Button
            {
                Style = (Style)FindResource("Bare"),
                Content = new Border
                {
                    Background = connected ? (Brush)FindResource("Chip") : (Brush)FindResource("TextMain"),
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 6),
                    Child = new TextBlock { Text = connected ? "Выйти" : "Войти", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = connected ? (Brush)FindResource("TextMain") : Brushes.Black },
                },
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(btn, 1);
            btn.Click += (_, _) =>
            {
                AccountsPopup.IsOpen = false;
                if (connected) App.Hub.Library.Disconnect(s);
                else App.Hub.Connect(s);
            };
            row.Children.Add(btn);
            AccountsList.Children.Add(row);
        }
        var reload = new Button { Style = (Style)FindResource("Bare"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reload.Content = new TextBlock { Text = "Обновить лайки", FontSize = 12, TextDecorations = TextDecorations.Underline, Foreground = (Brush)FindResource("TextMain") };
        reload.Click += (_, _) => { AccountsPopup.IsOpen = false; _ = App.Hub.Library.ReloadLikesAsync(); };
        AccountsList.Children.Add(reload);
        AccountsList.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line"), Margin = new Thickness(0, 14, 0, 12) });
        AccountsList.Children.Add(DiscordSection());
    }

    /// <summary>«Слушает HUM» in Discord: an on/off switch and the Application ID it needs.</summary>
    private FrameworkElement DiscordSection()
    {
        var s = App.Hub.Settings;
        var box = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = "Активность в Discord", FontSize = 13, FontWeight = FontWeights.SemiBold });
        var status = new TextBlock { FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap };
        info.Children.Add(status);
        head.Children.Add(info);

        var on = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 6) };
        var onText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
        on.Child = onText;
        var toggle = new Button { Style = (Style)FindResource("Bare"), Content = on, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(toggle, 1);
        head.Children.Add(toggle);
        box.Children.Add(head);

        var id = new TextBox { Style = (Style)FindResource("Field"), Tag = "Application ID", Text = s.DiscordAppId ?? "", Margin = new Thickness(0, 10, 0, 0) };
        box.Children.Add(id);
        var link = new TextBlock { FontSize = 11, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Text = "Создайте приложение «HUM» на discord.com/developers и вставьте его Application ID — Discord покажет «Слушает HUM»." };
        box.Children.Add(link);

        void Paint()
        {
            var enabled = s.DiscordEnabled && !string.IsNullOrWhiteSpace(s.DiscordAppId);
            on.Background = s.DiscordEnabled ? (Brush)FindResource("TextMain") : (Brush)FindResource("Chip");
            onText.Text = s.DiscordEnabled ? "Вкл" : "Выкл";
            onText.Foreground = s.DiscordEnabled ? Brushes.Black : (Brush)FindResource("TextMain");
            status.Text = !s.DiscordEnabled ? "выключена"
                : string.IsNullOrWhiteSpace(s.DiscordAppId) ? "нужен Application ID"
                : App.Hub.Discord.IsConnected ? "показывается в профиле" : "ждёт запущенный Discord";
        }
        toggle.Click += (_, _) => { s.DiscordEnabled = !s.DiscordEnabled; s.Save(); App.Hub.ApplyDiscord(); Paint(); };
        id.LostKeyboardFocus += (_, _) =>
        {
            var v = id.Text.Trim();
            if (v == (s.DiscordAppId ?? "")) return;
            s.DiscordAppId = v.Length == 0 ? null : v;
            s.Save();
            App.Hub.ApplyDiscord();
            Paint();
        };
        id.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
        Paint();
        return box;
    }

    private void ShowToast(string text)
    {
        if (!IsVisible) return;
        ToastText.Text = text;
        _toastTween?.Cancel();
        _toastTween = Animator.Tween(3.2, t => Toast.Opacity = t < 0.1 ? t / 0.1 : t > 0.85 ? (1 - t) / 0.15 : 1);
    }
}
