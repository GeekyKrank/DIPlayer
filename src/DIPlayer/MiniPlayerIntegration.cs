using DIPlayer.SystemIntegration;

namespace DIPlayer;

public partial class MainWindow
{
    void WindowStateChanged(object? sender, EventArgs e) => VisibilityChanged(sender, e);
    MiniPlayer? miniPlayer;
    void ShowMiniPlayer()
    {
        var args = Environment.GetCommandLineArgs();
        if (quitting || (args.Contains("--selftest") && !args.Contains("--check-mini-player"))) return;
        miniPlayer ??= new MiniPlayer(id => Guard(() => id == 0 ? TogglePlayback() : Advance(id)), () => WindowActivation.Restore(this), SyncMiniPlayer);
        miniPlayer.ShowAbove(this);
    }
    void SyncMiniPlayer() => miniPlayer?.Update(current, player.Playing && !player.Paused, queue.Index > 0, queue.Index + 1 < queue.Tracks.Count);
}
