namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyWallpaper(string directory)
    {
        int firstFrame = wallpaperFrame;
        await Task.Delay(240);
        CheckInteraction(AnimatedWallpaperPreference.IsChecked == true && wallpaperTimer.IsEnabled &&
            WallpaperVideo.Source != null && wallpaperFrame != firstFrame && wallpaperFrames.Length > 24 && wallpaperFrames.All(frame => frame.Name.EndsWith(".png", StringComparison.Ordinal)),
            "Default wallpaper animation did not advance");
        Capture(Path.Combine(directory, "animated-wallpaper.png"));

        AnimatedWallpaperPreference.IsChecked = false;
        PreferenceChanged(this, new System.Windows.RoutedEventArgs());
        CheckInteraction(!settings.AnimatedWallpaper && WallpaperVideo.Source == null && wallpaperArchive == null && !wallpaperTimer.IsEnabled,
            "Disabling animation did not release the video frames");
        await Task.Delay(150);
        Capture(Path.Combine(directory, "static-wallpaper.png"));
        await File.WriteAllTextAsync(Path.Combine(directory, "wallpaper-check.txt"), "PASS default animation advances and the setting releases the frames to reveal the static photo.");
    }
}
