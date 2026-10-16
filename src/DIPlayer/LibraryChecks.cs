using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DIPlayer.Core;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyLibraryCards(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Library checks require isolated storage");
        int windows = System.Windows.Application.Current.Windows.Count;
        queue.Tracks.Clear();
        queue.Index = -1;
        var result = await Catalog().Search("lofi", null, default);
        var tracks = result.Tracks.Where(t => t.Access == "playable").Take(4).ToArray();
        CheckInteraction(tracks.Length == 4, "Need public tracks for artwork and playback checks");
        foreach (var track in tracks) Remember(track);
        for (int i = 0; i < 14; i++)
        {
            string id = store.Create(i == 0 ? "Мой плейлист" : $"Подборка {i:00}");
            store.Add(id, Enumerable.Range(0, 31).Select(n => tracks[n % tracks.Length]));
        }
        LibraryView(this, new RoutedEventArgs());
        await Task.Delay(1300); UpdateLayout();
        CheckInteraction(libraryOverview && LibraryPanel.IsVisible && playlistCards.Count == 12 && LibraryNext.IsEnabled, "Card pagination failed");
        Capture(Path.Combine(directory, "library-cards.png"));
        NextLibraryCards(this, new RoutedEventArgs());
        CheckInteraction(playlistCards.Count == 2 && !LibraryNext.IsEnabled, "Last card page failed");
        LibraryFilter.Text = "Мой";
        CheckInteraction(playlistCards.Count == 1 && playlistCards[0].Count == 31, "Playlist filter failed");
        string selected = playlistCards[0].Id;
        PlaylistCards.SelectedIndex = 0;
        UpdateLayout();
        var cardItem = (ListBoxItem)PlaylistCards.ItemContainerGenerator.ContainerFromIndex(0);
        cardItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
        await Task.Delay(250); UpdateLayout();
        CheckInteraction(!libraryOverview && ContentPanel.IsVisible && playlistId == selected && rows.Count == 25 && PlaylistSummary.IsVisible, "Opening card did not show detail page");
        Capture(Path.Combine(directory, "playlist-detail.png"));
        FollowingPage(this, new RoutedEventArgs());
        CheckInteraction(page == 1 && rows.Count == 6, "Detail track pagination failed");
        PreviousPage(this, new RoutedEventArgs());
        EnqueueWholePlaylist(this, new RoutedEventArgs());
        CheckInteraction(queue.Tracks.Count == 31, "Enqueue whole playlist failed");
        PlayWholePlaylist(this, new RoutedEventArgs());
        await WaitForClickedPlayback(tracks[0].Id);
        CheckInteraction(queue.Tracks.Count == 31 && queue.Index == 0, "Play all did not replace queue in order");
        player.Pause();
        ClickQueueButton(); ClickQueueButton();
        CheckInteraction(!libraryOverview && playlistId == selected, "Queue did not restore detail");
        Width = 460; Height = 680;
        await Task.Delay(200); UpdateLayout();
        CheckInteraction(!PlaylistSummary.IsVisible && CompactPlayAll.IsVisible && Tracks.ActualWidth > 200, "Narrow detail layout failed");
        Capture(Path.Combine(directory, "playlist-detail-narrow.png"));
        BackToLibrary(this, new RoutedEventArgs());
        CheckInteraction(libraryOverview && LibraryFilter.Text == "Мой", "Back lost library filter");
        ClickQueueButton(); ClickQueueButton();
        CheckInteraction(libraryOverview && LibraryPanel.IsVisible, "Queue did not restore card library");
        LibraryFilter.Text = "Несуществующее название";
        CheckInteraction(playlistCards.Count == 0 && LibraryEmpty.IsVisible, "Empty filter state failed");
        LibraryFilter.Text = "";
        UpdateLayout(); Capture(Path.Combine(directory, "library-cards-narrow.png"));
        CheckInteraction(System.Windows.Application.Current.Windows.Count == windows, "Library opened another window");
        await File.WriteAllTextAsync(Path.Combine(directory, "library-cards-check.txt"), "PASS card pages, filtering, empty state, opening detail, track pages, queue all, real MP3 HLS play all, return from queue to detail/cards, narrow layout and one-window navigation.");
        Width = 1200; Height = 780; SetView("home");
    }
}
