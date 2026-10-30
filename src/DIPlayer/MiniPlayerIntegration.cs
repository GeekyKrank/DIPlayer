using DIPlayer.SystemIntegration;

namespace DIPlayer;

public partial class MainWindow
{
    void WindowStateChanged(object? sender, EventArgs e) { if(startupInitialized) LogWindowTransition("Window state changed"); VisibilityChanged(sender, e); }
    MiniPlayer? miniPlayer;
    void ShowMiniPlayer()
    {
        var args = Environment.GetCommandLineArgs();
        if (quitting || !settings.MiniPlayerEnabled || (args.Contains("--selftest") && !args.Contains("--check-mini-player") && !args.Contains("--check-youtube-native"))) return;
        miniPlayer ??= new MiniPlayer(id => Guard(() => browserMode ? BrowserMedia(id == 0 ? 0 : id > 0 ? 1 : 2) : id == 0 ? TogglePlayback() : Advance(id)), () => WindowActivation.Restore(this), SyncMiniPlayer);
        miniPlayer.ShowAbove(this);
    }
    void SyncMiniPlayer()
    {
        if (browserMode)
        {
            bool wrap = settings.RepeatMode == 1;
            miniPlayer?.Update(YouTubeCurrentTrack(), youtubeState?.Paused == false,
                !youtubeQueueCleared && (youtubeQueue.Tracks.Count == 0 || settings.Shuffle || wrap || youtubeQueue.Index > 0),
                !youtubeQueueCleared && (youtubeQueue.Tracks.Count == 0 || settings.Shuffle || wrap || youtubeQueue.Index + 1 < youtubeQueue.Tracks.Count));
            if (miniPlayer?.IsVisible == true && !youtubeTimer.IsEnabled) youtubeTimer.Start();
        }
        else miniPlayer?.Update(current, player.Playing && !player.Paused, queue.Index > 0, queue.Index + 1 < queue.Tracks.Count);
    }
}
