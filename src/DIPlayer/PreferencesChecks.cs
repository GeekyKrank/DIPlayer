using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;

namespace DIPlayer;
public partial class MainWindow
{
    async Task VerifyPreferencesExperience(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Preferences checks require isolated storage");
        await ApplyBrowserMode(false);
        Width = 1200; Height = 780;
        OpenPlayerSettings(this, new RoutedEventArgs());
        await Task.Delay(250); UpdateLayout();
        CheckInteraction(SettingsPanel.IsVisible && MotionPreference.IsVisible && ArtworkQualityPreference.Items.Count == 3, "New preferences page is incomplete");
        Capture(Path.Combine(directory, "settings-listening.png"));
        var scroll = InterfaceChildren<ScrollViewer>(SettingsPanel).First();
        scroll.ScrollToVerticalOffset(360); await Task.Delay(150); UpdateLayout();
        Capture(Path.Combine(directory, "settings-appearance.png"));

        MotionPreference.IsChecked = false; BackdropPreference.IsChecked = false;
        RememberVolumePreference.IsChecked = false; StartupVolumePreference.Value = .35;
        ArtworkQualityPreference.SelectedIndex = 2; CompactRowsPreference.IsChecked = true;
        PreferenceChanged(this, new RoutedEventArgs());
        CheckInteraction(!UI.Motion.GetEnabled(this) && LyricsBackdrop.Effect == null && UI.Artwork.QualityMode == 2, "Appearance preferences did not apply immediately");
        CheckInteraction(StartupVolumePreference.IsEnabled && Math.Abs(settings.StartupVolume - .35) < .001, "Startup volume preference is not usable");
        var restored = JsonSerializer.Deserialize<Storage.Settings>(JsonSerializer.Serialize(settings))!;
        CheckInteraction(!restored.MotionEnabled && restored.CompactRows && restored.ArtworkQuality == 2 && Math.Abs(restored.StartupVolume - .35) < .001, "Preferences do not survive serialization");
        settings.SearchHistorySC = ["A recent search"]; settings.SearchHistoryYT = ["Another search"];
        int playlists = store.Playlists().Count;
        ClearRecentSearches(this, new RoutedEventArgs());
        CheckInteraction(settings.SearchHistorySC.Length == 0 && settings.SearchHistoryYT.Length == 0 && store.Playlists().Count == playlists, "Clearing search history changes the library");

        Width = 460; Height = 680; scroll.ScrollToTop(); await Task.Delay(200); UpdateLayout();
        CheckInteraction(SettingsPanel.ActualWidth > 250 && RememberVolumePreference.ActualWidth < SettingsPanel.ActualWidth, "Preferences overflow the narrow window");
        Capture(Path.Combine(directory, "settings-narrow.png"));
        MotionPreference.IsChecked = true; BackdropPreference.IsChecked = true;
        ArtworkQualityPreference.SelectedIndex = 0; CompactRowsPreference.IsChecked = false;
        RememberVolumePreference.IsChecked = true; PreferenceChanged(this, new RoutedEventArgs());
        CheckInteraction(LyricsBackdrop.Effect is BlurEffect && UI.Motion.GetEnabled(this), "Appearance controls did not restore correctly");
        ClosePlayerSettings(this, new RoutedEventArgs());
        ToggleEqualizer(this, new RoutedEventArgs());
        await Task.Delay(200); UpdateLayout();
        CheckInteraction(EqualizerPresetPicker.Items.Count == 10 && EqualizerCurve.ActualWidth > 150 && EqualizerHeadroomToggle.IsVisible, "Improved equalizer is incomplete in narrow mode");
        Capture(Path.Combine(directory, "equalizer-curve-narrow.png"));
        CloseEqualizer(this, new RoutedEventArgs());
        Width = 1200; Height = 780;
        await File.WriteAllTextAsync(Path.Combine(directory, "preferences-check.txt"), "PASS practical settings: immediate motion/blur/quality/density changes; startup volume; persistence; history clearing preserves library; responsive settings and ten-preset equalizer curve/headroom.");
    }
}
