using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DIPlayer.Core;
using DIPlayer.Storage;

namespace DIPlayer;

// Runs only through explicit --selftest switches with an isolated test library.
public partial class MainWindow
{
    void CheckInteraction(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    void ClickQueueButton() => QueueToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    async Task VerifyInlineActions(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "UI checks require --test-library");
        int windows = System.Windows.Application.Current.Windows.Count;
        var track = new Track("interaction-test", "Проверка трека", "DIPlayer", 120000, null, "", "playable");
        SetView("search");
        ShowRows([new DisplayRow(track)]);
        Tracks.SelectedIndex = 0;
        AddToPlaylist(this, new RoutedEventArgs());
        CheckInteraction(playlistAction == PlaylistAction.CreateAndAdd && ActionOverlay.IsVisible, "First track must offer an inline create-and-add form");
        UpdateLayout();
        Capture(Path.Combine(directory, "create-playlist.png"));
        ConfirmAction(this, new RoutedEventArgs());
        CheckInteraction(ActionError.IsVisible && store.Playlists().Count == 0, "Empty playlist name must be rejected inline");
        ActionName.Text = "Проверка плейлиста";
        ConfirmAction(this, new RoutedEventArgs());
        string created = playlistId!;
        CheckInteraction(store.Playlists().Single(p => p.Id == created).Count == 1 && !ActionOverlay.IsVisible, "Inline create-and-add did not persist");
        RenamePlaylist(this, new RoutedEventArgs());
        CheckInteraction(ActionName.Text == "Проверка плейлиста", "Rename must prefill the current name");
        Width = 460; Height = 680;
        await Task.Delay(150); UpdateLayout();
        Capture(Path.Combine(directory, "rename-narrow.png"));
        ActionName.Text = "Новое имя";
        ConfirmAction(this, new RoutedEventArgs());
        CheckInteraction(store.Playlists().Single(p => p.Id == created).Name == "Новое имя", "Inline rename did not persist");
        SetView("search"); ShowRows([new DisplayRow(track)]); Tracks.SelectedIndex = 0;
        AddToPlaylist(this, new RoutedEventArgs());
        CheckInteraction(playlistAction == PlaylistAction.Add && ActionPicker.IsVisible, "Existing playlist must use the inline picker");
        ActionPicker.SelectedItem = store.Playlists().Single(p => p.Id == created);
        UpdateLayout(); Capture(Path.Combine(directory, "add-to-playlist-narrow.png"));
        ConfirmAction(this, new RoutedEventArgs());
        CheckInteraction(store.Playlists().Single(p => p.Id == created).Count == 2, "Inline add did not persist");

        queue.Tracks.Clear(); queue.Add(track); queue.Index = 0;
        page = 3; next = "test-next"; Heading.Text = "Проверка возврата";
        ClickQueueButton();
        CheckInteraction(view == "queue", "Bottom queue button did not open queue");
        ClickQueueButton();
        CheckInteraction(view == "search" && page == 3 && next == "test-next" && Heading.Text == "Проверка возврата" && rows[0].Track.Id == track.Id, "Queue did not return to the same search page");
        SetView("home"); ClickQueueButton(); ClickQueueButton();
        CheckInteraction(view == "home", "Queue did not return home");
        ImportClick(this, new RoutedEventArgs()); ClickQueueButton(); ClickQueueButton();
        CheckInteraction(view == "import" && ImportPanel.IsVisible, "Queue did not return to inline import");
        RefreshPlaylists(created); SetView("library"); page = 0; ShowLibrary();
        ClickQueueButton(); ClickQueueButton();
        CheckInteraction(view == "library" && playlistId == created, "Queue did not return to the selected playlist");

        DeletePlaylist(this, new RoutedEventArgs());
        UpdateLayout(); Capture(Path.Combine(directory, "delete-playlist-narrow.png"));
        CancelAction(this, new RoutedEventArgs());
        CheckInteraction(store.Playlists().Any(p => p.Id == created), "Cancel must preserve the playlist");
        DeletePlaylist(this, new RoutedEventArgs()); ConfirmAction(this, new RoutedEventArgs());
        CheckInteraction(!store.Playlists().Any(p => p.Id == created), "Inline delete did not remove the playlist");
        CreatePlaylist(this, new RoutedEventArgs()); CancelAction(this, new RoutedEventArgs());
        CheckInteraction(store.Playlists().Count == 0, "Cancel create must not persist a playlist");
        ReportApplicationError("Проверка встроенного сообщения");
        CheckInteraction(ActionOverlay.IsVisible && playlistAction == PlaylistAction.Notice, "Action errors must stay inside the app");
        ConfirmAction(this, new RoutedEventArgs());
        CheckInteraction(System.Windows.Application.Current.Windows.Count == windows, "Playlist actions opened another window");
        await File.WriteAllTextAsync(Path.Combine(directory, "inline-actions-check.txt"), "PASS inline create/add, empty-name validation, rename, picker, cancel/delete, cancel/create and notice; queue toggles back to search page, home, import and selected library; no new application windows.");
        Width = 1200; Height = 780; SetView("home");
    }

    void RaiseTrackClick(int index)
    {
        var row = rows[index];
        Tracks.ScrollIntoView(row); UpdateLayout();
        var item = (ListBoxItem)Tracks.ItemContainerGenerator.ContainerFromIndex(index);
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
    }
    async Task WaitForClickedPlayback(string id)
    {
        for (int wait = 0; wait < 200 && !(current?.Id == id && player.Playing && player.Position > .2); wait++) await Task.Delay(100);
        CheckInteraction(current?.Id == id && player.Playing && player.Position > .2, "Single click did not start real playback: " + latestStatus);
    }
    async Task VerifySingleClick(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Playback interaction checks require --test-library");
        var found = await Catalog().Search("lofi", null, default);
        var playable = found.Tracks.Where(t => t.Access == "playable").Take(2).ToArray();
        CheckInteraction(playable.Length == 2, "Need two publicly playable tracks for click check");
        queue.Tracks.Clear(); queue.Index = -1;
        SetView("search"); ShowRows(playable.Select(t => new DisplayRow(t)));
        int before = playGeneration;
        Tracks.SelectedIndex = 1;
        await Task.Delay(100);
        CheckInteraction(playGeneration == before, "Programmatic row selection must not autoplay");
        RaiseTrackClick(0);
        await WaitForClickedPlayback(playable[0].Id);
        int generation = playGeneration, count = queue.Tracks.Count;
        RaiseTrackClick(0);
        await Task.Delay(150);
        CheckInteraction(playGeneration == generation && queue.Tracks.Count == count, "Clicking the playing row must not restart or duplicate it");
        player.Pause(); RaiseTrackClick(0);
        await Task.Delay(150);
        CheckInteraction(!player.Paused && player.Playing, "One click must resume a paused current track");
        RaiseTrackClick(1);
        await WaitForClickedPlayback(playable[1].Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "single-click-check.txt"), "PASS actual routed mouse down/up starts first track and switches to second with real MP3 HLS playback; selecting a row in code does not play; clicking current track does not restart/duplicate it and resumes pause.");
        await player.Stop();
    }
}
