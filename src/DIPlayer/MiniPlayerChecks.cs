using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DIPlayer.SystemIntegration;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyMiniPlayer(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Mini player checks require isolated storage");
        var result = await Catalog().Search("lofi", null, default);
        var tracks = result.Tracks.Where(t => t.Access == "playable").Take(2).ToArray();
        CheckInteraction(tracks.Length == 2, "Need two playable tracks for mini player check");
        queue.Tracks.Clear(); queue.Index = -1;
        string playlist = store.Create("Mini player switching check");
        store.Add(playlist, tracks);
        foreach (var track in tracks) Remember(track);
        OpenLocalPlaylist(playlist);
        await PlayRow(rows[0]); await WaitForClickedPlayback(tracks[0].Id);
        CheckInteraction(queue.Tracks.Count == 2 && queue.Index == 0, "Clicking a playlist track must prepare the playlist queue");
        WindowState = WindowState.Minimized;
        await Task.Delay(200);
        CheckInteraction(miniPlayer?.IsVisible != true && IsVisible, "System minimize must not open mini player or hide the taskbar window");
        WindowActivation.Restore(this);
        await Task.Delay(200);
        MinimizeClick(this, new RoutedEventArgs());
        await Task.Delay(400);
        var mini = miniPlayer!;
        CheckInteraction(mini is { IsVisible: true, Topmost: true, ShowInTaskbar: false } && !IsVisible, "Minimize did not show the mini player and hide the main window");
        CheckInteraction(!timer.IsEnabled, "Main UI timer is still running while hidden");
        var toggle = (Button)mini.FindName("ToggleButton");
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(100);
        CheckInteraction(player.Paused, "Mini pause failed");
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(100);
        CheckInteraction(!player.Paused, "Mini resume failed");
        ((Button)mini.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitForClickedPlayback(tracks[1].Id);
        SyncMiniPlayer();
        CheckInteraction(((TextBlock)mini.FindName("TrackTitle")).Text == tracks[1].Title, "Mini metadata did not follow the queue");
        mini.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(mini.ActualWidth), (int)Math.Ceiling(mini.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(mini);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, "mini-player.png"))) encoder.Save(file);
        ((Button)mini.FindName("PreviousButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitForClickedPlayback(tracks[0].Id);
        WindowActivation.Restore(this);
        await Task.Delay(200);
        CheckInteraction(IsVisible && !mini.IsVisible, "Restoring the main window did not hide the mini player");
        MinimizeClick(this, new RoutedEventArgs());
        await Task.Delay(200);
        CheckInteraction(ReferenceEquals(mini, miniPlayer) && mini.IsVisible, "Repeated minimize failed: same=" + ReferenceEquals(mini, miniPlayer) + ", miniVisible=" + mini.IsVisible + ", mainVisible=" + IsVisible + ", mainState=" + WindowState);
        ((Button)mini.FindName("HideButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        CheckInteraction(!mini.IsVisible && player.Playing, "Hiding mini player interrupted playback");
        WindowActivation.Restore(this);
        await File.WriteAllTextAsync(Path.Combine(directory, "mini-player-check.txt"), "PASS real MP3 HLS playback, minimize/show topmost mini, main UI timer stops, pause/resume, next/previous, metadata sync, restore/hide, reuse on repeated minimize, hide mini without stopping audio.");
    }
}
