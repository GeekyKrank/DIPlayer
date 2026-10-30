using System.Windows;
using System.Windows.Threading;
using DIPlayer.SystemIntegration;
namespace DIPlayer;
public partial class MainWindow
{
    readonly DiscordPresence discordPresence = new();
    readonly DispatcherTimer discordTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool discordUpdating;
    void InitializeDiscord()
    {
        DiscordEnabled.IsChecked = settings.DiscordPresenceEnabled;
        discordTimer.Tick += async (_, _) => await UpdateDiscord();
        if (!Environment.GetCommandLineArgs().Contains("--selftest")) discordTimer.Start();
    }
    async Task UpdateDiscord()
    {
        if (discordUpdating || quitting) return;
        discordUpdating = true;
        try
        {
            if (!settings.DiscordPresenceEnabled)
            {
                discordPresence.Dispose();
                DiscordStatus.Text = "Disabled";
                return;
            }
            var track = browserMode ? YouTubeCurrentTrack() : current;
            bool playing = browserMode ? youtubeState?.Paused == false && youtubeState?.IsAd == false && YouTubeFailure.Visibility != Visibility.Visible : player.Playing && !player.Paused;
            object? activity = track != null && playing && !string.IsNullOrWhiteSpace(track.Title)
                ? DiscordPresence.Activity(track.Title, track.Artist, browserMode ? "YouTube Music" : "SoundCloud", browserMode ? youtubeState!.Position : player.Position, browserMode ? youtubeState!.Duration : player.Duration, track.Artwork) : null;
            await discordPresence.Update("1561330201911103572", activity);
            if (!quitting) DiscordStatus.Text = discordPresence.Status;
        }
        finally
        {
            discordUpdating = false;
            if (!settings.DiscordPresenceEnabled)
            {
                discordPresence.Dispose();
                DiscordStatus.Text = "Disabled";
            }
        }
    }
    async void SaveDiscordSettings(object sender, RoutedEventArgs e)
    {
        settings.DiscordPresenceEnabled = DiscordEnabled.IsChecked == true;
        await Guard(async () =>
        {
            if (DiagnosticLibraryDirectory() == null) settings.Save();
            await UpdateDiscord();
        });
    }
}
