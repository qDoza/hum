using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>«Подключить iPhone»: a short one-time QR code (see <see cref="Pairing"/>).</summary>
public sealed class PairWindow : Window
{
    private readonly string? _ya, _sc;
    private readonly string _base;
    private readonly Image _qr = new() { Width = 220, Height = 220, SnapsToDevicePixels = true };
    private readonly TextBlock _status = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };

    public PairWindow(string? yaToken, string? scToken, string baseUrl)
    {
        _ya = yaToken; _sc = scToken; _base = baseUrl;
        Title = "Подключить iPhone";
        Width = 360; Height = 540;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("TextMain");
        var muted = (Brush)Application.Current.FindResource("Muted");
        _status.Foreground = muted;
        RenderOptions.SetBitmapScalingMode(_qr, BitmapScalingMode.NearestNeighbor);

        var root = new Border
        {
            Margin = new Thickness(10), CornerRadius = new CornerRadius(14), Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C)), BorderThickness = new Thickness(1), Padding = new Thickness(22),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Direction = 270, Opacity = 0.55 },
        };
        var stack = new StackPanel();
        root.Child = stack;
        root.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        stack.Children.Add(new TextBlock { Text = "Подключить iPhone", FontSize = 18, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = "Наведите камеру iPhone на код и откройте ссылку — аккаунты перенесутся в HUM на телефоне.",
            FontSize = 12.5, Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16),
        });
        stack.Children.Add(new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center,
            Width = 240, Height = 240, Child = _qr,
        });
        stack.Children.Add(_status);

        var again = MakeButton("Новый код", false);
        again.Click += async (_, _) => await Generate();
        var close = MakeButton("Готово", true);
        close.Click += (_, _) => Close();
        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(close, 2);
        row.Children.Add(again);
        row.Children.Add(close);
        stack.Children.Add(row);

        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += async (_, _) => await Generate();
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Style = (Style)Application.Current.FindResource("Bare"),
        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource(primary ? "TextMain" : "Chip"), CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 10, 0, 10),
            Child = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = primary ? Brushes.Black : (Brush)Application.Current.FindResource("TextMain") },
        },
    };

    private async System.Threading.Tasks.Task Generate()
    {
        _qr.Source = null;
        _status.Text = "Готовлю код…";
        try
        {
            var link = await Pairing.CreateLinkAsync(_ya, _sc, _base);
            var img = new BitmapImage();
            img.BeginInit(); img.StreamSource = new MemoryStream(Pairing.QrPng(link)); img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
            _qr.Source = img;
            _status.Text = "Код одноразовый и действует 15 минут. Потом: Safari → «Поделиться» → «На экран „Домой“».";
        }
        catch (Exception e)
        {
            Log.Write("pair: " + e.Message);
            _status.Text = "Не получилось: " + e.Message;
        }
    }
}
