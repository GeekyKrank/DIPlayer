using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DIPlayer.Core;
using DIPlayer.Storage;

namespace DIPlayer;

public sealed record PlaylistCard(string Id, string Name, int Count, string? Cover1, string? Cover2, string? Cover3, string? Cover4, System.Windows.Media.ImageSource? CustomCover = null, bool Complete = true)
{
    public string CountLabel => $"{Count} tracks";
    public string StatusLabel => Complete ? "Playlist" : "Import incomplete";
    public string? SingleCover => Cover1;
}

public partial class MainWindow
{
    const int CardsPerPage = 12;
    readonly ObservableCollection<PlaylistCard> playlistCards = [];
    bool libraryOverview;
    int libraryPage;
    CancellationTokenSource? libraryArtworkCancel;
    readonly Dictionary<string, System.Windows.Media.ImageSource> localCovers = [];

    PlaylistCard MakeCard(PlaylistHeader header)
    {
        return new(header.Id, header.Name, header.Count,
            header.Artwork, null, null, null, LocalCover(header), header.Complete);
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
        SyncLibraryImportStatus();
        libraryArtworkCancel?.Cancel();
        CloseActionPanel(false);
        libraryOverview = true;
        UpdatePlaylistLayout();
        metadataCancel?.Cancel();
        rows.Clear();
        ContentPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        var all = store.Playlists(settings.OldestPlaylistsFirst).Where(p => p.Name.Contains(LibraryFilter.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
        libraryPage = Math.Min(libraryPage, Math.Max(0, (all.Count - 1) / CardsPerPage));
        var visible = all.Skip(libraryPage * CardsPerPage).Take(CardsPerPage).ToArray();
        playlistCards.Clear();
        foreach (var header in visible) playlistCards.Add(MakeCard(header));
        PlaylistCards.ItemsSource = playlistCards;
        ApplyLibraryView();
        LibraryEmpty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryEmpty.Text = LibraryFilter.Text.Length == 0 ? "No playlists yet. Click + or import a public playlist." : "No playlists found.";
        LibraryPrevious.IsEnabled = libraryPage > 0;
        LibraryNext.IsEnabled = (libraryPage + 1) * CardsPerPage < all.Count;
        LibraryPageLabel.Text = $"{libraryPage + 1} / {Math.Max(1, (all.Count + CardsPerPage - 1) / CardsPerPage)}";
        if (IsVisible && WindowState != WindowState.Minimized && visible.Any(h => h.Artwork == null && !h.HasCover && h.Count > 0))
            _ = FillMissingPlaylistArtwork(visible);


    }

    async Task FillMissingPlaylistArtwork(PlaylistHeader[] headers)
    {
        libraryArtworkCancel?.Cancel();
        libraryArtworkCancel = new();
        var ct = libraryArtworkCancel.Token;
        try
        {
            var candidates = headers.Where(h => h.Artwork == null && !h.HasCover && h.Count > 0).ToArray();
            var references = candidates.ToDictionary(h => h.Id, h => store.Page(h.Id, 0).Take(4).Select(r => r.Track.Id).ToArray());
            var resolved = new Dictionary<string, Track>();
            foreach (var batch in references.Values.SelectMany(ids => ids).Distinct().Chunk(25))
                foreach (var track in await Catalog().GetTracks(batch, ct)) resolved[track.Id] = track;
            ct.ThrowIfCancellationRequested();
            foreach (var header in candidates)
            {
                string? artwork = references[header.Id].Select(id => resolved.GetValueOrDefault(id)?.Artwork).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
                if (artwork == null) continue;
                // A custom cover or an import may have completed while the request was in flight.
                var currentHeader = store.Playlists().FirstOrDefault(h => h.Id == header.Id);
                if (currentHeader == null || currentHeader.Artwork != null || currentHeader.HasCover) continue;
                store.SetArtwork(header.Id, artwork);
                if (!libraryOverview || view != "library") continue;
                for (int i = 0; i < playlistCards.Count; i++)
                    if (playlistCards[i].Id == header.Id) playlistCards[i] = MakeCard(currentHeader with { Artwork = artwork });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is CatalogException or HttpRequestException) { }
    }

    void FilterPlaylists(object s, TextChangedEventArgs e)
    {
        if (LibraryPanel == null || view != "library" || !libraryOverview) return;
        libraryPage = 0;
        ShowLibraryOverview();
    }
    void TogglePlaylistSort(object s, RoutedEventArgs e)
    {
        settings.OldestPlaylistsFirst = !settings.OldestPlaylistsFirst;
        libraryPage = 0;
        ShowLibraryOverview();
    }
    void ToggleLibraryView(object s, RoutedEventArgs e)
    {
        settings.LibraryTableView = !settings.LibraryTableView;
        ApplyLibraryView();
    }
    void ApplyLibraryView()
    {
        PlaylistSortButton.Content = settings.OldestPlaylistsFirst ? "\uE74A" : "\uE74B";
        PlaylistSortButton.ToolTip = settings.OldestPlaylistsFirst ? "Oldest playlists first · click for newest" : "Newest playlists first · click for oldest";
        System.Windows.Automation.AutomationProperties.SetName(PlaylistSortButton, (string)PlaylistSortButton.ToolTip);
        bool table = settings.LibraryTableView;
        PlaylistCards.ItemTemplate = (DataTemplate)FindResource(table ? "PlaylistTableTemplate" : "PlaylistCardTemplate");
        PlaylistCards.ItemsPanel = (ItemsPanelTemplate)FindResource(table ? "PlaylistTablePanel" : "PlaylistCardPanel");
        PlaylistCards.ItemContainerStyle = (Style)FindResource(table ? "PlaylistTableItem" : "PlaylistCardItem");
        PlaylistTableHeading.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
        LibraryViewToggle.Content = table ? "\uE80A" : "\uE8FD";
        LibraryViewToggle.ToolTip = table ? "Show playlist cards" : "Show playlist table";
        System.Windows.Automation.AutomationProperties.SetName(LibraryViewToggle, (string)LibraryViewToggle.ToolTip);
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
        libraryArtworkCancel?.Cancel();
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
        DetailCount.Text = header == null ? "" : $"{header.Count} tracks" + (header.Complete ? "" : " Â· import incomplete");
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
