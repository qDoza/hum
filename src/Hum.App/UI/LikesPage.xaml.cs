using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hum.App.Models;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>«Любимое»: one list for both services, each row tagged ЯМ or SC.</summary>
public partial class LikesPage : UserControl
{
    private static Hub H => App.Hub;
    private readonly ListCollectionView _view;
    private Source? _filter;

    public LikesPage()
    {
        InitializeComponent();
        _view = new ListCollectionView(H.Library.Liked) { Filter = Match };
        List.ItemsSource = _view;
        Rows.Attach(List, () => _view.Cast<Track>().ToList(), PlayContext.Likes, "Любимое");

        Query.TextChanged += (_, _) => _view.Refresh();
        FAll.Checked += (_, _) => SetFilter(null);
        FYa.Checked += (_, _) => SetFilter(Source.Yandex);
        FSc.Checked += (_, _) => SetFilter(Source.SoundCloud);
        PlayAllBtn.Click += (_, _) => Play(false);
        ShuffleBtn.Click += (_, _) => Play(true);

        H.Library.Changed += Refresh;
        H.Library.Liked.CollectionChanged += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
    }

    private bool Match(object o)
    {
        if (o is not Track t) return false;
        if (_filter is { } f && t.Source != f) return false;
        var q = Query.Text.Trim();
        return q.Length == 0 || t.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || t.Artist.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SetFilter(Source? s) { _filter = s; _view.Refresh(); Refresh(); }

    private void Play(bool shuffle)
    {
        var list = _view.Cast<Track>().ToList();
        if (list.Count == 0) return;
        if (shuffle) list = list.OrderBy(_ => Random.Shared.Next()).ToList();
        H.Player.PlayList(list, 0, PlayContext.Likes, shuffle ? "Любимое · вперемешку" : "Любимое");
    }

    private void Refresh()
    {
        var lib = H.Library;
        int ya = lib.Count(Source.Yandex), sc = lib.Count(Source.SoundCloud);
        CountText.Text = $"{lib.Liked.Count} {Plural(lib.Liked.Count)}";
        FYa.Content = $"Яндекс · {ya}";
        FSc.Content = $"SoundCloud · {sc}";
        FYa.Visibility = lib.Has(Source.Yandex) ? Visibility.Visible : Visibility.Collapsed;
        FSc.Visibility = lib.Has(Source.SoundCloud) ? Visibility.Visible : Visibility.Collapsed;

        EmptyText.Text =
            !lib.Has(Source.Yandex) && !lib.Has(Source.SoundCloud) ? "Подключите Яндекс или SoundCloud — лайки из обоих сервисов соберутся здесь."
            : lib.LikesLoading && lib.Liked.Count == 0 ? "Загружаю лайки…"
            : _view.IsEmpty ? (Query.Text.Length > 0 ? "Ничего не нашлось" : "Пока пусто")
            : "";
        EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static string Plural(int n) =>
        n % 10 == 1 && n % 100 != 11 ? "трек" : n % 10 is >= 2 and <= 4 && (n % 100 < 12 || n % 100 > 14) ? "трека" : "треков";
}
