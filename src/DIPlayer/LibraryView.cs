using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DIPlayer.Core;
using DIPlayer.Storage;

namespace DIPlayer;

public sealed record PlaylistCard(string Id, string Name, int Count, string? Cover1, string? Cover2, string? Cover3, string? Cover4, System.Windows.Media.ImageSource? CustomCover = null)
{
    public string CountLabel => $"{Count} tracks";
}

public partial class MainWindow
{
    const int CardsPerPage = 12;
    readonly ObservableCollection<PlaylistCard> playlistCards = [];
    bool libraryOverview;
    int libraryPage;
    CancellationTokenSource? coverCancel;
    readonly Dictionary<string, System.Windows.Media.ImageSource> localCovers = [];

    PlaylistCard MakeCard(PlaylistHeader header)
    {
        var covers = store.Page(header.Id, 0).Take(4)
            .Select(r => metadata.GetValueOrDefault(r.Track.Id)?.Artwork).ToArray();
        return new(header.Id, header.Name, header.Count,
            covers.ElementAtOrDefault(0), covers.ElementAtOrDefault(1), covers.ElementAtOrDefault(2), covers.ElementAtOrDefault(3), LocalCover(header));
    }
    System.Windows.Media.ImageSource? LocalCover(PlaylistHeader header)
    {
        if (!header.HasCover) return null;
        if (localCovers.TryGetValue(header.Id, out var image)) return image;
        var bytes = store.Cover(header.Id);
        if (bytes == null) return null;
        var bitmap = DecodeCover(bytes, 256);
        if (localCovers.Count >= 16) localCovers.Remove(localCovers.Keys.First());
        return localCovers[header.Id] = bitmap;
    }

    void ShowLibraryOverview()
    {
        CloseActionPanel(false);
        libraryOverview = true;
        UpdatePlaylistLayout();
        metadataCancel?.Cancel();
        coverCancel?.Cancel();
        rows.Clear();
        ContentPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        var all = store.Playlists().Where(p => p.Name.Contains(LibraryFilter.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
        libraryPage = Math.Min(libraryPage, Math.Max(0, (all.Count - 1) / CardsPerPage));
        var visible = all.Skip(libraryPage * CardsPerPage).Take(CardsPerPage).ToArray();
        playlistCards.Clear();
        foreach (var header in visible) playlistCards.Add(MakeCard(header));
        PlaylistCards.ItemsSource = playlistCards;
        LibraryEmpty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryEmpty.Text = LibraryFilter.Text.Length == 0 ? "No playlists yet. Click + or import a public playlist." : "No playlists found.";
        LibraryPrevious.IsEnabled = libraryPage > 0;
        LibraryNext.IsEnabled = (libraryPage + 1) * CardsPerPage < all.Count;
        LibraryPageLabel.Text = $"{libraryPage + 1} / {Math.Max(1, (all.Count + CardsPerPage - 1) / CardsPerPage)}";
        if (IsVisible && WindowState != WindowState.Minimized) _ = LoadPlaylistCovers(visible);
    }

    async Task LoadPlaylistCovers(PlaylistHeader[] headers)
    {
        coverCancel?.Cancel();
        coverCancel = new();
        var ct = coverCancel.Token;
        try
        {
            var ids = headers.SelectMany(h => store.Page(h.Id, 0).Take(4)).Select(r => r.Track.Id)
                .Distinct().Where(id => !metadata.ContainsKey(id)).ToArray();
            foreach (var batch in ids.Chunk(25))
            {
                foreach (var track in await Catalog().GetTracks(batch, ct)) Remember(track);
                ct.ThrowIfCancellationRequested();
                if (!libraryOverview || view != "library" || !IsVisible) return;
                for (int i = 0; i < playlistCards.Count; i++)
                {
                    var header = headers.FirstOrDefault(h => h.Id == playlistCards[i].Id);
                    if (header != null) playlistCards[i] = MakeCard(header);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { SetStatus(e is CatalogException ? e.Message : "Could not load artwork. Local playlists remain available."); }
    }

    void FilterPlaylists(object s, TextChangedEventArgs e)
    {
        if (LibraryPanel == null || view != "library" || !libraryOverview) return;
        libraryPage = 0;
        ShowLibraryOverview();
    }
    void PreviousLibraryCards(object s, RoutedEventArgs e) { libraryPage = Math.Max(0, libraryPage - 1); ShowLibraryOverview(); }
    void NextLibraryCards(object s, RoutedEventArgs e) { libraryPage++; ShowLibraryOverview(); }
    void OpenPlaylistCard(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(PlaylistCards, source) is ListBoxItem { Content: PlaylistCard card }) OpenLocalPlaylist(card.Id);
    }
    void PlaylistCardKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PlaylistCards.SelectedItem is PlaylistCard card) { e.Handled = true; OpenLocalPlaylist(card.Id); }
    }
    void OpenLocalPlaylist(string id)
    {
        coverCancel?.Cancel();
        RefreshPlaylists(id);
        SetView("library");
        page = 0;
        ShowLibrary();
    }
    void BackToLibrary(object s, RoutedEventArgs e) => ShowLibraryOverview();
    void UpdatePlaylistLayout()
    {
        if (PlaylistSummary == null || LibraryBar == null) return;
        bool detail = view == "library" && !libraryOverview;
        PlaylistBack.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        SearchHeader.Margin = new Thickness(detail && ActualWidth < 950 ? 110 : 22, 6, 22, 12);
        bool wide = detail && ActualWidth >= 950;
        TrackContent.Margin = new Thickness(0, wide ? 10 : 0, 0, 0);
        Heading.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        var actionHost = wide ? SummaryPlaylistActions : CompactPlaylistActions;
        if (!ReferenceEquals(LibraryBar.Parent, actionHost))
        {
            (LibraryBar.Parent as Panel)?.Children.Remove(LibraryBar);
            actionHost.Children.Add(LibraryBar);
        }
        PlaylistSummary.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        PlaylistSummaryColumn.Width = new GridLength(wide ? 254 : 0);
        CompactPlayAll.Visibility = detail && !wide ? Visibility.Visible : Visibility.Collapsed;
    }
    void UpdatePlaylistSummary(PlaylistHeader? header)
    {
        DetailCover.Content = header == null ? null : MakeCard(header);
        DetailName.Text = header?.Name ?? "Playlist";
        DetailCount.Text = header == null ? "" : $"{header.Count} tracks" + (header.Complete ? "" : " · import incomplete");
    }

    List<Track> PlaylistQueueTracks()
    {
        var header = store.Playlists().FirstOrDefault(p => p.Id == playlistId);
        if (header == null || header.Count == 0) throw new CatalogException("This playlist has no tracks yet.");
        if (header.Count > 1000) throw new CatalogException("The queue supports up to 1,000 tracks. Add tracks from larger playlists individually.");
        var result = new List<Track>(header.Count);
        for (int i = 0; i * 25 < header.Count; i++)
            result.AddRange(store.Page(header.Id, i).Select(r => metadata.GetValueOrDefault(r.Track.Id) ?? r.Track));
        return result;
    }
    async void PlayWholePlaylist(object s, RoutedEventArgs e) => await Guard(async () =>
    {
        var tracks = PlaylistQueueTracks();
        queue.Tracks.Clear();
        queue.Tracks.AddRange(tracks);
        queue.Index = 0;
        store.SaveQueue(queue.Tracks);
        await Play(tracks[0]);
    });
    async void EnqueueWholePlaylist(object s, RoutedEventArgs e) => await Guard(() =>
    {
        var tracks = PlaylistQueueTracks();
        if (queue.Tracks.Count + tracks.Count > 1000) throw new CatalogException("The queue is limited to 1,000 tracks.");
        queue.Tracks.AddRange(tracks);
        store.SaveQueue(queue.Tracks);
        SetStatus($"Added to queue: {tracks.Count}");
        return Task.CompletedTask;
    });
}
