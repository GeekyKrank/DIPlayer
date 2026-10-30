using System.Windows;
using System.Windows.Threading;
namespace DIPlayer;

public partial class MainWindow
{
    sealed record YouTubeNavigationEntry(YouTubeItem[] Items, string Heading, string Url,
        YouTubeItem? Collection, string Title, string Subtitle, string? Artwork,
        bool Queue, double CardsOffset, double ShelvesOffset, YouTubePage? ArtistPage, string Description, string? CollectionArtist);
    readonly List<YouTubeNavigationEntry> youtubeHistory = [];

    YouTubeNavigationEntry CaptureYouTubeView() => new(youtubeItems.ToArray(), YouTubeHeading.Text,
        youtubeDisplayedUrl, youtubeCollection, YouTubeCollectionTitle.Text,
        YouTubeCollectionSubtitle.Text, YouTubeCollectionArtwork.Url, youtubeQueueView,
        YouTubeCardsScroll.VerticalOffset, YouTubeShelvesScroll.VerticalOffset,youtubeArtistPage,YouTubeCollectionDescription.Text,youtubeCollectionArtistName);

    void RememberYouTubeView(YouTubeNavigationEntry previous)
    {
        if (previous.Items.Length == 0) return;
        youtubeHistory.Add(previous);
        if (youtubeHistory.Count > 32) youtubeHistory.RemoveAt(0);
        SyncYouTubeNavigation();
    }

    void SyncYouTubeNavigation()
    {
        YouTubeBackButton.IsEnabled = youtubeHistory.Count > 0 && !youtubeOpeningCollection;
        YouTubeClearQueue.Visibility = youtubeQueueView ? Visibility.Visible : Visibility.Collapsed;
        YouTubeClearQueue.IsEnabled = youtubeQueue.Tracks.Count > 0;
        YouTubeEmpty.Text = youtubeQueueView ? "Queue is empty" : "Search for music, or sign in to access your library.";
        if (!browserMode) return;
        bool library = new Uri(youtubeDisplayedUrl).AbsolutePath == "/library";
        bool likes = youtubeDisplayedUrl.Contains("list=LM", StringComparison.Ordinal);
        YouTubeLibraryNav.Tag = library && !youtubeQueueView ? "active" : null;
        YouTubeLikesNav.Tag = likes && !youtubeQueueView ? "active" : null;
        YouTubeQueueNav.Tag = youtubeQueueView ? "active" : null;
        HomeNav.Tag = new Uri(youtubeDisplayedUrl).AbsolutePath == "/" && !youtubeQueueView ? "active" : null;
    }

    void YouTubeCollectionBack(object sender, RoutedEventArgs e)
    {
        if (youtubeOpeningCollection || youtubeHistory.Count == 0) return;
        ClosePlaybackPanelsForNavigation();
        var previous = youtubeHistory[^1];
        youtubeHistory.RemoveAt(youtubeHistory.Count - 1);
        youtubeKeepCards = true;
        youtubeExpectedRoute = null;
        youtubeCollection = previous.Collection;
        youtubeArtistPage = previous.ArtistPage;
        youtubeCollectionArtistName=previous.CollectionArtist;
        youtubeQueueView = previous.Queue;
        youtubeItems.Clear();
        if (previous.Queue) foreach (var track in youtubeQueue.Tracks) youtubeItems.Add(new(track.Id, track.Title, track.Artist, track.Artwork, "Track"));
        else foreach (var item in previous.Items) youtubeItems.Add(item);
        YouTubeHeading.Text = previous.Heading;
        YouTubeCollectionTitle.Text = previous.Title;
        YouTubeCollectionDescription.Text=previous.Description;
        YouTubeCollectionSubtitle.Text = previous.Subtitle;
        YouTubeCollectionArtwork.Url = previous.Artwork;
        UpdateYouTubeShelves(previous.Url);
        if (previous.Queue)
        {
            YouTubeCardsScroll.Visibility = Visibility.Visible;
            YouTubeShelvesScroll.Visibility = Visibility.Collapsed;
        }
        YouTubeStatus.Text = youtubeItems.Count + " items · YouTube Music";
        YouTubeEmpty.Visibility = youtubeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(() =>
        {
            YouTubeCardsScroll.ScrollToVerticalOffset(previous.CardsOffset);
            YouTubeShelvesScroll.ScrollToVerticalOffset(previous.ShelvesOffset);
        }, DispatcherPriority.Loaded);
        SyncYouTubeNavigation();
    }
}
