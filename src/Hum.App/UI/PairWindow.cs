using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Hum.App.UI;

/// <summary>
/// «Подключить iPhone»: a QR code with a link to the web app that carries both tokens in the URL
/// fragment (#pair=…). The fragment never leaves the phone, so the tokens are not sent to any server.
/// </summary>
public sealed class PairWindow : Window
{
    public PairWindow(string? yaToken, string? scToken, string baseUrl)
    {
        Title = "Подключить iPhone";
        Width = 380; Height = 560;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("TextMain");

        var payload = new JsonObject();
        if (!string.IsNullOrEmpty(yaToken)) payload["ya"] = yaToken;
        if (!string.IsNullOrEmpty(scToken)) payload["sc"] = scToken;
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var link = baseUrl.TrimEnd('/') + "/#pair=" + b64;

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
            Text = "Наведите камеру iPhone на код и откройте ссылку. Аккаунты Яндекса и SoundCloud перенесутся в HUM на телефоне.",
            FontSize = 12.5, Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16),
        });

        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(link, QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(8, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, true);
        var img = new BitmapImage();
        img.BeginInit(); img.StreamSource = new MemoryStream(png); img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
        stack.Children.Add(new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Image { Source = img, Width = 270, Height = 270, SnapsToDevicePixels = true },
        });
        RenderOptions.SetBitmapScalingMode(stack.Children[^1], BitmapScalingMode.NearestNeighbor);

        stack.Children.Add(new TextBlock
        {
            Text = "В коде ваши токены — не показывайте его другим. После переноса добавьте HUM на экран «Домой»: Safari → «Поделиться» → «На экран „Домой“».",
            FontSize = 12, Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 14),
        });

        var close = new Button
        {
            Style = (Style)Application.Current.FindResource("Bare"), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new Border
            {
                Background = (Brush)Application.Current.FindResource("TextMain"), CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 10, 0, 10),
                Child = new TextBlock { Text = "Готово", Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        close.Click += (_, _) => Close();
        stack.Children.Add(close);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
