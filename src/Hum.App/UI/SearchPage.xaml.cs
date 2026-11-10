using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hum.App.Models;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>Song search in the chosen service; likes from here land in the shared «Любимое».</summary>
public partial class SearchPage : UserControl
{
    private static Hub H => App.Hub;
    private readonly DispatcherTimer _debounce;
    private List<Track> _results = new();
    private int _req;
    private bool _sync;

    public SearchPage()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Normal, (_, _) => Run(), Dispatcher);
        _debounce.Stop();
        Rows.Attach(List, () => _results, PlayContext.Search, "Поиск");

        Query.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        Query.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(); };
        SrcYa.Checked += (_, _) => SetSource("ya");
        SrcSc.Checked += (_, _) => SetSource("sc");
        H.SourceChanged += () => { Sync(); Run(); };
        H.Library.Changed += Sync;
        Loaded += (_, _) => Sync();
    }

    public void FocusQuery() => Dispatcher.BeginInvoke(() => { Query.Focus(); Keyboard.Focus(Query); }, DispatcherPriority.Input);

    private Source CurrentSource => H.Settings.Source == "sc" ? Source.SoundCloud : Source.Yandex;

    private void SetSource(string s)
    {
        if (_sync || H.Settings.Source == s) return;
        H.Settings.Source = s;
        H.Settings.Save();
        H.RaiseSourceChanged();
    }

    private void Sync()
    {
        _sync = true;
        SrcYa.IsChecked = CurrentSource == Source.Yandex;
        SrcSc.IsChecked = CurrentSource == Source.SoundCloud;
        _sync = false;
        if (!H.Library.Has(CurrentSource))
            Status.Text = CurrentSource == Source.Yandex ? "Яндекс не подключён — подключите его на вкладке «Эфир»" : "SoundCloud не подключён — подключите его на вкладке «Эфир»";
        else if (Query.Text.Trim().Length == 0) Status.Text = "";
    }

    private async void Run()
    {
        _debounce.Stop();
        var q = Query.Text.Trim();
        var src = CurrentSource;
        if (q.Length < 2 || !H.Library.Has(src)) { Sync(); return; }
        var req = ++_req;
        Status.Text = "Ищу…";
        try
        {
            var list = await H.Library.SearchAsync(src, q);
            if (req != _req) return;
            _results = list;
            List.ItemsSource = list;
            Status.Text = list.Count == 0 ? "Ничего не нашлось" : $"{(src == Source.Yandex ? "Яндекс" : "SoundCloud")} · {list.Count} {LikesPage.Plural(list.Count)}";
        }
        catch (Exception e)
        {
            if (req != _req) return;
            Log.Write("search: " + e.Message);
            Status.Text = "Поиск не удался: " + e.Message;
        }
    }
}
