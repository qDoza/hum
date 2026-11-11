using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls.Primitives;
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
        CoverPage.ArtistRequested += (id, name) => ArtistPage.Open(id, name);
        LookBtn.Content = Icons.Make("palette", 17);
        LookBtn.Click += (_, _) => { BuildLook(); LookPopup.IsOpen = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && ArtistPage.IsVisible) { ArtistPage.Close(); e.Handled = true; }
            else if (e.Key == Key.Escape && CoverPage.IsVisible) { CoverPage.Close(); e.Handled = true; }
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
        if (ArtistPage.IsVisible) ArtistPage.Close();
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
                    Background = connected ? (Brush)FindResource("Chip") : (Brush)FindResource("Accent"),
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 6),
                    Child = new TextBlock { Text = connected ? "Выйти" : "Войти", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = connected ? (Brush)FindResource("TextMain") : (Brush)FindResource("OnAccent") },
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

        // the iPhone version: hand both accounts over through a QR code
        var lib2 = App.Hub.Library;
        var pair = new Button
        {
            Style = (Style)FindResource("Bare"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = lib2.Has(Source.Yandex) || lib2.Has(Source.SoundCloud),
            Content = new Border
            {
                Background = (Brush)FindResource("Accent"), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 8, 12, 8),
                Child = new TextBlock { Text = "Подключить iPhone", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("OnAccent"), HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        pair.Click += (_, _) =>
        {
            AccountsPopup.IsOpen = false;
            var s = App.Hub.Settings;
            new PairWindow(lib2.Has(Source.Yandex) ? s.YandexToken : null, lib2.Has(Source.SoundCloud) ? s.ScToken : null, s.WebAppUrl) { Owner = this }.ShowDialog();
        };
        AccountsList.Children.Add(pair);
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

    public void OpenArtist(string id, string name) => ArtistPage.Open(id, name);
    public void OpenLook() { BuildLook(); LookPopup.IsOpen = true; }

    // ------------------------------------------------------------------ «Оформление»

    private void BuildLook()
    {
        var L = App.Hub.Settings.Look;
        LookList.Children.Clear();
        LookList.Children.Add(new TextBlock { Text = "Оформление", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        void Changed() { Services.Look.Save(); BuildLook(); Tabs.Paint(_tabs, _tabs[_tab]); }
        TextBlock Cap(string t) => new() { Text = t.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 14, 0, 8) };

        // accent swatches
        LookList.Children.Add(Cap("Акцент"));
        var sw = new WrapPanel();
        foreach (var a in Services.Look.Accents)
        {
            Brush fill = a.Hex is { } h ? new SolidColorBrush(Services.Look.Hex(h)) : new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(0xFF, 0x6A, 0x55), 0), new(Color.FromRgb(0xFF, 0xB4, 0x3D), 0.25), new(Color.FromRgb(0x45, 0xE0, 0xB0), 0.5),
                new(Color.FromRgb(0x58, 0xB2, 0xFF), 0.75), new(Color.FromRgb(0xB3, 0x8C, 0xFF), 1),
            }, 45);
            var on = L.Accent == a.Id;
            var dot = new Grid { Width = 34, Height = 34 };
            dot.Children.Add(new System.Windows.Shapes.Ellipse { Fill = fill, Width = 26, Height = 26 });
            if (on) dot.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = (Brush)FindResource("TextMain"), StrokeThickness = 2 });
            var b = new Button { Style = (Style)FindResource("Bare"), Content = dot, ToolTip = a.Title, Margin = new Thickness(0, 0, 6, 6) };
            Press.SetEnabled(b, true);
            b.Click += (_, _) => { L.Accent = a.Id; Changed(); };
            sw.Children.Add(b);
        }
        LookList.Children.Add(sw);

        // a row of option tiles
        void Opts(string title, IEnumerable<(string Id, string Title, Brush? Preview, string? Glyph)> items, Func<string> get, Action<string> set)
        {
            LookList.Children.Add(Cap(title));
            var wrap = new UniformGrid { Columns = 3 };
            foreach (var (id, name, pv, glyph) in items)
            {
                var on = get() == id;
                var tile = new StackPanel();
                var preview = new Border { Height = 30, CornerRadius = new CornerRadius(7), Background = pv ?? (Brush)FindResource("ModeBox"), Margin = new Thickness(0, 0, 0, 6) };
                if (glyph is not null) preview.Child = new TextBlock { Text = glyph, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextMain") };
                tile.Children.Add(preview);
                tile.Children.Add(new TextBlock { Text = name, FontSize = 11.5, Foreground = (Brush)FindResource(on ? "TextMain" : "Muted") });
                var box = new Border
                {
                    Child = tile, Padding = new Thickness(8), CornerRadius = new CornerRadius(10), Background = (Brush)FindResource("Chip"),
                    BorderBrush = on ? (Brush)FindResource("Accent") : Brushes.Transparent, BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 6, 6),
                };
                var b = new Button { Style = (Style)FindResource("Bare"), Content = box };
                Press.SetEnabled(b, true);
                b.Click += (_, _) => { set(id); Changed(); };
                wrap.Children.Add(b);
            }
            LookList.Children.Add(wrap);
        }
        SolidColorBrush B(string hex) => new(Services.Look.Hex(hex));

        Opts("Фон", Services.Look.Backgrounds.Select(b => (b.Id, b.Title, b.Colors.Length > 0 ? (Brush?)B(b.Colors[1]) : new LinearGradientBrush(Color.FromRgb(0x3A, 0x1D, 0x14), Color.FromRgb(0x0D, 0x13, 0x22), 0), (string?)null)),
            () => L.Bg, v => L.Bg = v);
        Opts("Контраст", new (string, string, Brush?, string?)[] { ("normal", "Обычный", null, "Аа"), ("high", "Высокий", null, "Аа") }, () => L.Contrast, v => L.Contrast = v);
        Opts("Шрифт", Services.Look.Fonts.Select(f => (f.Id, f.Title, (Brush?)null, (string?)"Аа")), () => L.Font, v => L.Font = v);
        Opts("Форма кнопок", new (string, string, Brush?, string?)[] { ("soft", "Мягкая", null, null), ("pill", "Капсулы", null, null), ("sharp", "Острая", null, null) }, () => L.Shape, v => L.Shape = v);
        Opts("Обложка в плеере", new (string, string, Brush?, string?)[] { ("round", "Пластинка", null, "●"), ("square", "Квадрат", null, "■") }, () => L.Cover, v => L.Cover = v);
        Opts("Фон плеера", new (string, string, Brush?, string?)[] { ("glass", "Стекло", new LinearGradientBrush(Color.FromRgb(0x3A, 0x2A, 0x24), Color.FromRgb(0x14, 0x14, 0x14), 45), null),
            ("vivid", "Ярко", new LinearGradientBrush(Color.FromRgb(0xB4, 0x55, 0x3A), Color.FromRgb(0x3A, 0x1D, 0x60), 45), null), ("black", "Чёрный", B("#050505"), null) }, () => L.PlayerBg, v => L.PlayerBg = v);
        Opts("Визуализатор", new (string, string, Brush?, string?)[] { ("bars", "Полосы", null, "▮▮▮"), ("waves", "Волны", null, "◎"), ("minimal", "Минимум", null, "○"), ("off", "Выкл", null, "—") }, () => L.VizStyle, v => L.VizStyle = v);
        Opts("Сила визуализатора", new (string, string, Brush?, string?)[] { ("0.6", "Спокойно", null, "∿"), ("1", "Обычно", null, "〰"), ("1.5", "Мощно", null, "≋") },
            () => L.VizAmount.ToString(System.Globalization.CultureInfo.InvariantCulture), v => L.VizAmount = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture));

        // switches
        void Switch(string title, string sub, bool value, Action<bool> set)
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = title, FontSize = 13 });
            info.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(info);
            var on = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 6), Background = value ? (Brush)FindResource("Accent") : (Brush)FindResource("Chip"),
                Child = new TextBlock { Text = value ? "Вкл" : "Выкл", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = value ? (Brush)FindResource("OnAccent") : (Brush)FindResource("TextMain") },
            };
            var b = new Button { Style = (Style)FindResource("Bare"), Content = on, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(b, 1);
            b.Click += (_, _) => { set(!value); Changed(); };
            row.Children.Add(b);
            LookList.Children.Add(row);
        }
        Switch("Вращать обложку", "Крутится как пластинка, пока играет", L.Spin, v => L.Spin = v);
        Switch("Меньше движения", "Без дрожи, частиц и плавающего света", L.ReducedMotion, v => L.ReducedMotion = v);

        var reset = new Button { Style = (Style)FindResource("Bare"), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Content = new TextBlock { Text = "Вернуть как было", FontSize = 12, TextDecorations = TextDecorations.Underline, Foreground = (Brush)FindResource("TextMain") };
        reset.Click += (_, _) => { App.Hub.Settings.Look = new Services.LookSettings(); Changed(); };
        LookList.Children.Add(reset);
    }

    private void ShowToast(string text)
    {
        if (!IsVisible) return;
        ToastText.Text = text;
        _toastTween?.Cancel();
        _toastTween = Animator.Tween(3.2, t => Toast.Opacity = t < 0.1 ? t / 0.1 : t > 0.85 ? (1 - t) / 0.15 : 1);
    }
}
