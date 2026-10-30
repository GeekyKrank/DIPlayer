using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DIPlayer;

public partial class MainWindow
{
    bool preferencesInitializing = true;
    void InitializePreferences()
    {
        Resources["EqualizerBandHeight"] = 120d;
        settings.StartupVolume = double.IsFinite(settings.StartupVolume) ? Math.Clamp(settings.StartupVolume, 0, 1) : .6;
        if (!settings.RememberVolume) settings.Volume = settings.StartupVolume;
        MotionPreference.IsChecked = settings.MotionEnabled;
        BackdropPreference.IsChecked = settings.SoftBackdrop;
        TrayPreference.IsChecked = settings.CloseToTray;
        MiniPreference.IsChecked = settings.MiniPlayerEnabled;
        RememberVolumePreference.IsChecked = settings.RememberVolume;
        StartupVolumePreference.Value = settings.StartupVolume;
        ArtworkQualityPreference.SelectedIndex = Math.Clamp(settings.ArtworkQuality, 0, 2);
        CompactRowsPreference.IsChecked = settings.CompactRows;
        TrackStatsPreference.IsChecked = settings.ShowTrackStats;
        preferencesInitializing = false;
        ApplyPreferences();
    }
    void PreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (preferencesInitializing) return;
        settings.MotionEnabled = MotionPreference.IsChecked == true;
        settings.SoftBackdrop = BackdropPreference.IsChecked == true;
        settings.CloseToTray = TrayPreference.IsChecked == true;
        settings.MiniPlayerEnabled = MiniPreference.IsChecked == true;
        settings.RememberVolume = RememberVolumePreference.IsChecked == true;
        settings.StartupVolume = StartupVolumePreference.Value;
        settings.ArtworkQuality = Math.Clamp(ArtworkQualityPreference.SelectedIndex, 0, 2);
        settings.CompactRows = CompactRowsPreference.IsChecked == true;
        settings.ShowTrackStats = TrackStatsPreference.IsChecked == true;
        ApplyPreferences();
        if (DiagnosticLibraryDirectory() == null) _ = Guard(() => { settings.Save(); return Task.CompletedTask; });
    }
    void ApplyPreferences()
    {
        UI.Motion.SetEnabled(this, settings.MotionEnabled);
        Resources["TrackRowPadding"] = new Thickness(10, settings.CompactRows ? 4 : 9, 10, settings.CompactRows ? 4 : 9);
        UI.Artwork.QualityMode = settings.ArtworkQuality;
        LyricsBackdrop.Effect = settings.SoftBackdrop ? new BlurEffect { Radius = 60, RenderingBias = RenderingBias.Performance } : null;
        LyricsBackdrop.CacheMode = settings.SoftBackdrop ? new BitmapCache { RenderAtScale = .3 } : null;
        LyricsBackdrop.Opacity = settings.SoftBackdrop ? .6 : 0;
        SyncTrackBackdrop();
        if (youtubeCollection?.Kind == "Artist") SyncYouTubeArtistPresentation();
        StartupVolumePreference.IsEnabled = !settings.RememberVolume;
        PreferencesStatus.Text = "Saved automatically · changes apply immediately";
        UpdateMemoryCacheStatus();
    }
    void UpdateMemoryCacheStatus() => ArtworkCacheStatus.Text = $"{UI.Artwork.CachedImageCount} cached covers · {UI.Artwork.CachedBytes / 1048576d:F1} MB";
    void FocusMusicSearch(object sender, RoutedEventArgs e)
    {
        var input = browserMode ? YouTubeSearch : SearchBox;
        input.Focus(); input.SelectAll();
    }
    void ClearArtworkCache(object sender, RoutedEventArgs e)
    {
        UI.Artwork.ClearMemoryCache(); UpdateMemoryCacheStatus();
        PreferencesStatus.Text = "Cover cache cleared. Your library and account are unchanged.";
    }
    void ClearRecentSearches(object sender, RoutedEventArgs e)
    {
        settings.SearchHistorySC = [];
        settings.SearchHistoryYT = [];
        CloseSearchSuggestions();
        if (DiagnosticLibraryDirectory() == null) _ = Guard(() => { settings.Save(); return Task.CompletedTask; });
        PreferencesStatus.Text = "Recent searches cleared for both services.";
    }
    void WorkspaceKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.K or Key.F)
        {
            e.Handled = true;
            if (SettingsPanel.IsVisible) { SettingsPanel.Visibility = Visibility.Collapsed; if (browserMode) ShowYouTubeNative(); }
            var input = browserMode ? YouTubeSearch : SearchBox;
            input.Focus(); input.SelectAll();
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        { e.Handled = true; OpenPlayerSettings(this, new RoutedEventArgs()); }
        else if (e.Key == Key.Escape && (EqualizerOverlay.IsVisible || YouTubeLyricsOverlay.IsVisible))
        { e.Handled = true; ClosePlaybackPanelsForNavigation(); }
    }
    void EqualizerLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (EqualizerCurve == null) return;
        bool compact = e.NewSize.Height < 520;
        Resources["EqualizerBandHeight"] = compact ? 88d : 120d;
        EqualizerCurve.Height = compact ? 52 : 96;
    }
}
