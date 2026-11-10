using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hum.App.Models;
using Hum.App.Services;

namespace Hum.App.UI;

/// <summary>«Эфир»: source switch, «Мой эфир» and the Yandex mixes with the mood pad.</summary>
public partial class WavePage : UserControl
{
    private static Hub H => App.Hub;
    private bool _sync;

    public WavePage()
    {
        InitializeComponent();

        foreach (var group in PlayerService.Mixes.GroupBy(m => m.Group))
        {
            var row = group.Key == "Настроение" ? MoodChips : AddGroup(group.Key);
            foreach (var mix in group)
            {
                var chip = Chip(mix.Title, mix.Id);
                chip.Click += (_, _) => { H.Settings.Mix = mix.Id; H.Settings.Save(); _ = H.Player.PlayMixAsync(mix.Id); };
                row.Children.Add(chip);
            }
        }

        // SoundCloud wave filters: what it grows from, a vibe, and on/off extras
        var baseRow = AddGroup("Основа", ScGroups);
        foreach (var b in PlayerService.ScBases)
        {
            var chip = new RadioButton { Content = b.Title, Tag = b.Id, Style = (Style)FindResource("ChipToggle"), GroupName = "scbase" };
            chip.Checked += (_, _) => { if (_sync || H.Settings.ScBase == b.Id) return; H.Settings.ScBase = b.Id; H.Player.ScFiltersChanged(); };
            _scBase.Add(chip);
            baseRow.Children.Add(chip);
        }
        var genreRow = AddGroup("Жанр и вайб", ScGroups);
        foreach (var g in PlayerService.ScGenres)
        {
            var chip = new RadioButton { Content = g.Title, Tag = g.Query, Style = (Style)FindResource("ChipToggle"), GroupName = "scgenre" };
            chip.Checked += (_, _) => { if (_sync || H.Settings.ScGenre == g.Query) return; H.Settings.ScGenre = g.Query; H.Player.ScFiltersChanged(); };
            _scGenre.Add(chip);
            genreRow.Children.Add(chip);
        }
        void Flag(System.Windows.Controls.Primitives.ToggleButton tb, System.Action<bool> set) =>
            tb.Click += (_, _) => { set(tb.IsChecked == true); H.Player.ScFiltersChanged(); Refresh(); };
        Flag(ScNoSets, v => H.Settings.ScNoSets = v);
        Flag(ScOnlyNew, v => H.Settings.ScOnlyNew = v);
        Flag(ScFresh, v => H.Settings.ScFresh = v);

        SrcYa.Checked += (_, _) => SetSource("ya");
        SrcSc.Checked += (_, _) => SetSource("sc");
        DivFav.Checked += (_, _) => SetDiversity("favorite");
        DivAny.Checked += (_, _) => SetDiversity("default");
        DivNew.Checked += (_, _) => SetDiversity("discover");

        MyWave.Click += (_, _) =>
        {
            var p = H.Player;
            if (p.Context == PlayContext.MyWave && p.ContextSource == CurrentSource && p.Current is not null) p.Toggle();
            else _ = p.PlayMyWaveAsync(CurrentSource);
        };
        ConnectButton.Click += (_, _) => H.Connect(CurrentSource);

        Pad.Set(H.Settings.PadX, H.Settings.PadY);
        Pad.Committed += (x, y) =>
        {
            H.Settings.PadX = x; H.Settings.PadY = y; H.Settings.Mix = "custom"; H.Settings.Save();
            _ = H.Player.PlayMixAsync("custom");
        };

        H.Player.Changed += Refresh;
        H.Library.Changed += Refresh;
        H.SourceChanged += Refresh;
        Loaded += (_, _) => Refresh();
    }

    public object? Top { get => TopSlot.Content; set => TopSlot.Content = value; }

    private readonly List<RadioButton> _chips = new();
    private Panel? _activityRow;
    private int _activityCount = -1;

    private RadioButton Chip(string title, string id)
    {
        var chip = new RadioButton { Content = title, Tag = id, Style = (Style)FindResource("ChipToggle"), GroupName = "wmix" };
        _chips.Add(chip);
        return chip;
    }

    /// <summary>A captioned, horizontally scrolling row of chips under the pad.</summary>
    private readonly List<RadioButton> _scBase = new(), _scGenre = new();

    /// <summary>A captioned group of chips that wrap onto new lines (nothing hides behind the edge).</summary>
    private Panel AddGroup(string caption, Panel? into = null)
    {
        into ??= MoreGroups;
        into.Children.Add(new TextBlock
        {
            Text = caption.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 10, 0, 8),
        });
        var row = new WrapPanel();
        into.Children.Add(row);
        return row;
    }

    /// <summary>«Занятия» come from the service once Yandex is connected.</summary>
    private void SyncActivities()
    {
        var acts = H.Library.Activities;
        if (acts.Count == _activityCount) return;
        _activityCount = acts.Count;
        if (_activityRow is null)
        {
            if (acts.Count == 0) return;
            _activityRow = AddGroup("Занятия");
        }
        foreach (RadioButton old in _activityRow.Children) _chips.Remove(old);
        _activityRow.Children.Clear();
        foreach (var (name, seed) in acts)
        {
            var chip = Chip(name, "act:" + seed);
            chip.Click += (_, _) => _ = H.Player.PlayActivityAsync(name, seed);
            _activityRow.Children.Add(chip);
        }
    }

    private Source CurrentSource => H.Settings.Source == "sc" ? Source.SoundCloud : Source.Yandex;

    private void SetSource(string s)
    {
        if (_sync || H.Settings.Source == s) return;
        H.Settings.Source = s;
        H.Settings.Save();
        H.RaiseSourceChanged();
    }

    private void SetDiversity(string d)
    {
        if (_sync || H.Settings.Diversity == d) return;
        H.Settings.Diversity = d;
        H.Settings.Save();
        if (H.Player.Context == PlayContext.Mix && H.Player.MixId == "custom") _ = H.Player.PlayMixAsync("custom");
    }

    private void Refresh()
    {
        _sync = true;
        var src = CurrentSource;
        var p = H.Player;
        SrcYa.IsChecked = src == Source.Yandex;
        SrcSc.IsChecked = src == Source.SoundCloud;
        DivFav.IsChecked = H.Settings.Diversity == "favorite";
        DivAny.IsChecked = H.Settings.Diversity == "default";
        DivNew.IsChecked = H.Settings.Diversity == "discover";

        var connected = H.Library.Has(src);
        ConnectBox.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        WaveBox.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ConnectTitle.Text = src == Source.Yandex ? "Яндекс не подключён" : "SoundCloud не подключён";
        ConnectText.Text = src == Source.Yandex
            ? "Войдите через Яндекс ID — появятся «Мой эфир», миксы и ваши лайки."
            : "Войдите в SoundCloud — лайки попадут в общее «Любимое», а эфир соберётся из похожих треков.";

        var isYa = src == Source.Yandex;
        MixBox.Visibility = isYa ? Visibility.Visible : Visibility.Collapsed;
        ScBox.Visibility = isYa ? Visibility.Collapsed : Visibility.Visible;
        var st = H.Settings;
        foreach (var c in _scBase) c.IsChecked = (string)c.Tag == st.ScBase;
        foreach (var c in _scGenre) c.IsChecked = (string?)c.Tag == st.ScGenre;
        ScNoSets.IsChecked = st.ScNoSets;
        ScOnlyNew.IsChecked = st.ScOnlyNew;
        ScFresh.IsChecked = st.ScFresh;
        var scOn = p.Context == PlayContext.MyWave && p.ContextSource == Source.SoundCloud;
        ScHint.Text = scOn ? "применяются сразу" : "для «Мой эфир»";

        // «Мой эфир» turns white while it is what's playing
        var onMine = p.Context == PlayContext.MyWave && p.ContextSource == src;
        var fg = onMine ? Brushes.Black : (Brush)FindResource("TextMain");
        MyWaveBg.Background = onMine ? (Brush)FindResource("TextMain") : (Brush)FindResource("Panel");
        MyWaveTitle.Foreground = MyWaveRing.Stroke = MyWaveLine.Stroke = MyWaveGlyph.Foreground = fg;
        MyWaveSub.Foreground = onMine ? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) : (Brush)FindResource("Muted");
        MyWaveSub.Text = onMine && p.IsBusy ? "Подбираю…" : "Просто то, что вам нравится. Без настроек";
        MyWaveGlyph.Text = onMine && p.IsPlaying ? "" : "";

        SyncActivities();
        var onMix = p.Context == PlayContext.Mix;
        foreach (var chip in _chips)
            chip.IsChecked = onMix && (string)chip.Tag == p.MixId;
        Pad.Active = onMix && p.MixId == "custom";
        _sync = false;
    }
}

