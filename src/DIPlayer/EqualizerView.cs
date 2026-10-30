using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DIPlayer.Audio;

namespace DIPlayer;

public partial class MainWindow
{
    sealed class EqualizerBand : INotifyPropertyChanged
    {
        double gain;
        public int Index { get; init; }
        public string Label { get; init; } = "";
        public double Gain { get => gain; set { if (gain == value) return; gain = value; PropertyChanged?.Invoke(this, new(nameof(Gain))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    readonly List<EqualizerBand> equalizerBands = [];
    sealed record EqualizerPresetOption(string Id, string Name, double[]? Gains)
    {
        public override string ToString() => Name;
    }
    static readonly EqualizerPresetOption[] equalizerPresets =
    [
        new("flat", "Flat", [0, 0, 0, 0, 0, 0]),
        new("bass", "Bass boost", [5, 3, 0, -1, 0, 1]),
        new("vocal", "Vocal", [-3, -2, 1, 3, 2, -1]),
        new("acoustic", "Acoustic", [2, 1, 0, 1, 2, 2]),
        new("electronic", "Electronic", [4, 2, -1, -1, 2, 3]),
        new("rock", "Rock", [3, 2, -1, 1, 3, 2]),
        new("warm", "Warm", [3, 2, 1, 0, -1, -2]),
        new("treble", "Treble boost", [-2, -1, 0, 1, 3, 4]),
        new("podcast", "Podcast", [-6, -3, 0, 4, 3, -2]),
        new("custom", "Custom", null)
    ];
    readonly DispatcherTimer equalizerApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly DispatcherTimer equalizerSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    bool equalizerInitializing = true, equalizerApplying;
    string equalizerYouTubeStatus = "pending";
    int equalizerRevision, equalizerYouTubeRevision = -1, equalizerYouTubeGeneration = -1;
    DateTime equalizerYouTubeReadAt;

    void InitializeEqualizer()
    {
        settings.EqualizerGains = EqualizerSampleProvider.NormalizeGains(settings.EqualizerGains);
        string[] labels = ["60", "170", "350", "1k", "3.5k", "10k"];
        for (int i = 0; i < labels.Length; i++) equalizerBands.Add(new() { Index = i, Label = labels[i], Gain = settings.EqualizerGains[i] });
        EqualizerBands.ItemsSource = equalizerBands;
        EqualizerEnabledToggle.IsChecked = settings.EqualizerEnabled;
        EqualizerHeadroomToggle.IsChecked = settings.EqualizerHeadroom;
        EqualizerPresetPicker.ItemsSource = equalizerPresets;
        EqualizerPresetPicker.DisplayMemberPath = nameof(EqualizerPresetOption.Name);
        EqualizerPresetPicker.SelectedValuePath = nameof(EqualizerPresetOption.Id);
        var preset = equalizerPresets.FirstOrDefault(option => option.Id == settings.EqualizerPresetName);
        if (preset == null || preset.Gains != null && !preset.Gains.SequenceEqual(settings.EqualizerGains))
            preset = equalizerPresets.FirstOrDefault(option => option.Gains?.SequenceEqual(settings.EqualizerGains) == true) ?? equalizerPresets[^1];
        settings.EqualizerPresetName = preset.Id;
        EqualizerPresetPicker.SelectedValue = preset.Id;
        equalizerApplyTimer.Tick += (_, _) => { equalizerApplyTimer.Stop(); ApplyEqualizerSettings(); };
        equalizerSaveTimer.Tick += (_, _) =>
        {
            equalizerSaveTimer.Stop();
            if (DiagnosticLibraryDirectory() == null) _ = Guard(() => { settings.Save(); return Task.CompletedTask; });
        };
        equalizerInitializing = false;
        ApplyEqualizerSettings();
    }

    void ToggleEqualizer(object sender, RoutedEventArgs e)
    {
        bool open = EqualizerOverlay.Visibility != Visibility.Visible;
        EqualizerOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) { SyncEqualizerStatus(); SyncEqualizerCurve(); EqualizerEnabledToggle.Focus(); }
    }

    void CloseEqualizer(object sender, RoutedEventArgs e) => EqualizerOverlay.Visibility = Visibility.Collapsed;
    void ClosePlaybackPanelsForNavigation()
    {
        CloseSearchSuggestions();
        YouTubeProfilePopup.IsOpen = false;
        StopYouTubeLyrics();
        EqualizerOverlay.Visibility = Visibility.Collapsed;
    }
    void EqualizerKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true; EqualizerOverlay.Visibility = Visibility.Collapsed;
    }

    void EqualizerEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (equalizerInitializing) return;
        settings.EqualizerEnabled = EqualizerEnabledToggle.IsChecked == true;
        QueueEqualizerUpdate();
    }

    void EqualizerBandChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (equalizerInitializing || sender is not Slider { DataContext: EqualizerBand band }) return;
        double value = double.IsFinite(e.NewValue) ? Math.Clamp(e.NewValue, -12, 12) : 0;
        if (settings.EqualizerGains[band.Index] == value) return;
        settings.EqualizerGains[band.Index] = value;
        settings.EqualizerPresetName = "custom";
        SyncEqualizerPresetPicker();
        QueueEqualizerUpdate();
    }

    void EqualizerBandReset(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider { DataContext: EqualizerBand band } slider || band.Gain == 0) return;
        e.Handled = true;
        slider.SetCurrentValue(Slider.ValueProperty, 0d);
    }

    void EqualizerHeadroomChanged(object sender, RoutedEventArgs e)
    {
        if (equalizerInitializing) return;
        settings.EqualizerHeadroom = EqualizerHeadroomToggle.IsChecked == true;
        QueueEqualizerUpdate();
    }

    void EqualizerPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!equalizerInitializing && EqualizerPresetPicker.SelectedItem is EqualizerPresetOption preset) ApplyEqualizerPreset(preset.Id);
    }

    void EqualizerPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset }) return;
        ApplyEqualizerPreset(preset);
    }

    void ApplyEqualizerPreset(string id)
    {
        var preset = equalizerPresets.FirstOrDefault(option => option.Id == id);
        if (preset == null) return;
        equalizerInitializing = true;
        if (preset.Gains != null)
        {
            settings.EqualizerGains = preset.Gains.ToArray();
            for (int i = 0; i < settings.EqualizerGains.Length; i++) equalizerBands[i].Gain = settings.EqualizerGains[i];
            if (preset.Id != "flat") settings.EqualizerEnabled = true;
        }
        settings.EqualizerPresetName = preset.Id;
        EqualizerEnabledToggle.IsChecked = settings.EqualizerEnabled;
        EqualizerPresetPicker.SelectedValue = preset.Id;
        equalizerInitializing = false;
        QueueEqualizerUpdate();
    }

    void SyncEqualizerPresetPicker()
    {
        bool previous = equalizerInitializing;
        equalizerInitializing = true;
        EqualizerPresetPicker.SelectedValue = settings.EqualizerPresetName;
        equalizerInitializing = previous;
    }

    void SyncEqualizerCurve() => EqualizerCurve.Configure(settings.EqualizerGains, settings.EqualizerEnabled, settings.EqualizerHeadroom);

    void QueueEqualizerUpdate()
    {
        SyncEqualizerCurve();
        equalizerApplyTimer.Stop(); equalizerApplyTimer.Start();
        equalizerSaveTimer.Stop(); equalizerSaveTimer.Start();
    }

    void ApplyEqualizerSettings()
    {
        equalizerRevision++;
        player.SetEqualizer(settings.EqualizerEnabled, settings.EqualizerGains, settings.EqualizerHeadroom);
        YouTubeEqualizerButton.Foreground = EqualizerButton.Foreground = settings.EqualizerEnabled ? Brushes.White : Brushes.Gray;
        SyncEqualizerCurve();
        SyncEqualizerStatus();
        ObserveYouTubeEqualizer();
    }

    void SyncEqualizerStatus()
    {
        EqualizerStatus.Text = !settings.EqualizerEnabled ? "Off · original sound"
            : settings.EqualizerGains.All(gain => Math.Abs(gain) < .0001) ? "Flat · original sound"
            : !browserMode ? "SoundCloud · equalizer active"
            : equalizerYouTubeStatus == "applied" ? "YouTube Music · equalizer active"
            : equalizerYouTubeStatus == "unavailable" ? "Equalizer is unavailable for this video."
            : "Start a track to hear the changes.";
    }

    void ObserveYouTubeEqualizer()
    {
        if (!browserMode || musicBrowser?.CoreWebView2 == null || equalizerApplying) return;
        bool installed = equalizerYouTubeGeneration == browserGeneration && equalizerYouTubeRevision == equalizerRevision;
        if (installed && (!settings.EqualizerEnabled || DateTime.UtcNow - equalizerYouTubeReadAt < TimeSpan.FromSeconds(2))) return;
        equalizerApplying = true;
        equalizerYouTubeReadAt = DateTime.UtcNow;
        int revision = equalizerRevision;
        _ = Guard(async () =>
        {
            try
            {
                int generation = browserGeneration;
                string result = await musicBrowser.CoreWebView2.ExecuteScriptAsync(installed
                    ? "window.__diplayerEqualizer ? window.__diplayerEqualizerStatus : 'missing'"
                    : YouTubeEqualizerBridge.Install(settings.EqualizerEnabled, settings.EqualizerGains, settings.EqualizerHeadroom));
                if (generation != browserGeneration) return;
                equalizerYouTubeStatus = JsonSerializer.Deserialize<string>(result) ?? "pending";
                if (equalizerYouTubeStatus == "missing")
                {
                    result = await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeEqualizerBridge.Install(settings.EqualizerEnabled, settings.EqualizerGains, settings.EqualizerHeadroom));
                    if (generation != browserGeneration) return;
                    equalizerYouTubeStatus = JsonSerializer.Deserialize<string>(result) ?? "pending";
                }
                equalizerYouTubeRevision = revision;
                equalizerYouTubeGeneration = generation;
                SyncEqualizerStatus();
            }
            finally { equalizerApplying = false; if (revision != equalizerRevision) ObserveYouTubeEqualizer(); }
        });
    }
}
