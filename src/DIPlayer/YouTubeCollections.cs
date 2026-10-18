using System.Text.Json;
using System.Windows;
namespace DIPlayer;

public partial class MainWindow
{
    bool youtubeKeepCards, youtubeOpeningCollection;
    YouTubeItem? youtubeCollection;


    async Task PlayYouTubeKey(string key, bool seedQueue = true)
    {
        if (musicBrowser?.CoreWebView2 == null) return;
        if (seedQueue) SeedYouTubeQueue(key);
        // Keep the user's gallery/album open while the browser starts the audio.
        youtubeKeepCards = true;
        await ConfigureYouTubeQueue();
        await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Navigate(key));
        ShowYouTubeNative();
        await RefreshYouTubeNative();
    }

    async Task ActivateYouTubeItem(YouTubeItem item)
    {
        if (youtubeOpeningCollection || musicBrowser?.CoreWebView2 == null) return;
        if (item.Kind == "Track") { await PlayYouTubeKey(item.Key); return; }
        if (item.Kind == "Browse") { await NavigateYouTube(item.Key, item.Title); return; }
        int generation = browserGeneration;
        youtubeOpeningCollection = true;
        YouTubeStatus.Text = "Loading " + item.Title + "…";
        var previousItems = youtubeItems.ToArray();
        var previousView = CaptureYouTubeView();
        try
        {
            await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Navigate(item.Key));
            YouTubeState? detail = null;
            string lastSnapshot = "null";
            for (int i = 0; i < 45; i++)
            {
                await Task.Delay(250);
                if (!browserMode || generation != browserGeneration || musicBrowser?.CoreWebView2 == null) return;
                var json = await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Snapshot);
                lastSnapshot = json;
                if (json.Length > 256 * 1024) continue;
                var state = JsonSerializer.Deserialize<YouTubeState>(json);
                if (state == null) continue;
                bool matchingPage = state.Url.StartsWith("https://music.youtube.com" + item.Key, StringComparison.Ordinal)
                    || string.Equals(state.Page?.Title, item.Title, StringComparison.OrdinalIgnoreCase);
                if (!matchingPage) continue;
                if (state.Page?.Title is { Length: > 0 } && state.Items.Any(track => track.Kind == "Track")) { detail = state; break; }
            }
            if (detail == null)
            {
                if (DiagnosticLibraryDirectory() is string diagnostics)
                {
                    await File.WriteAllTextAsync(Path.Combine(diagnostics,"collection-last-state.json"),lastSnapshot);
                    await File.WriteAllTextAsync(Path.Combine(diagnostics,"collection-dom.json"), await musicBrowser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,headers:Array.from(document.querySelectorAll('ytmusic-detail-header-renderer,ytmusic-responsive-header-renderer,ytmusic-immersive-header-renderer')).map(e=>e.outerHTML.slice(0,12000)),rows:document.querySelectorAll('ytmusic-responsive-list-item-renderer').length})"));
                }
                YouTubeStatus.Text = "This release could not be loaded. Try again."; return;
            }
            var tracks = detail.Items.Where(track => track.Kind == "Track").DistinctBy(track => track.Key).ToArray();
            if (item.Kind == "Single" && tracks.Length == 1)
            {
                // Seed from the resolved release, but do not replace the visible gallery.
                youtubeItems.Clear(); youtubeItems.Add(tracks[0]); SeedYouTubeQueue(tracks[0].Key);
                youtubeItems.Clear(); foreach (var previous in previousItems) youtubeItems.Add(previous);
                await PlayYouTubeKey(tracks[0].Key, false);
                YouTubeStatus.Text = "Playing " + tracks[0].Title;
                return;
            }
            RememberYouTubeView(previousView);
            youtubeCollection = item;
            youtubeQueueView = false;
            youtubeKeepCards = true;
            youtubeItems.Clear(); foreach (var track in tracks) youtubeItems.Add(track);
            YouTubeHeading.Text = item.Kind == "Album" ? "Album" : "Playlist";
            YouTubeCollectionTitle.Text = detail.Page?.Title is { Length: > 0 } title ? title : item.Title;
            YouTubeCollectionSubtitle.Text = item.Subtitle + " · " + tracks.Length + " tracks";
            YouTubeCollectionArtwork.Url = detail.Page?.Artwork is string art && art.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? art : item.Artwork;
            UpdateYouTubeShelves(detail.Url);
            YouTubeCardsScroll.ScrollToTop();
            YouTubeStatus.Text = tracks.Length + " tracks · YouTube Music";
            YouTubeEmpty.Visibility = Visibility.Collapsed;
        }
        finally { youtubeOpeningCollection = false; SyncYouTubeNavigation(); }
    }

    async void PlayYouTubeCollection(object sender, RoutedEventArgs e)
    {
        var first = youtubeItems.FirstOrDefault(item => item.Kind == "Track");
        if (first != null) await Guard(() => PlayYouTubeKey(first.Key));
    }
}
