using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyLiveYouTubeAlbum(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Live album checks require isolated storage");
        await ApplyBrowserMode(true);
        CheckInteraction(musicBrowser?.CoreWebView2 != null, "Album check browser could not start");
        var core = musicBrowser!.CoreWebView2;
        for (int i = 0; i < 80; i++)
        {
            await Task.Delay(250);
            if (await core.ExecuteScriptAsync("!!document.querySelector('ytmusic-app')") == "true") break;
        }
        await ActivateYouTubeItem(new("/browse/MPREb_iB5dNteyYwR", "Ego Trippin'", "Album · ROCKET", null, "Album"));
        for (int i = 0; i < 5 && youtubeItems.Count < 10; i++)
        {
            youtubeNextMore = DateTime.MinValue;
            await LoadMoreYouTube();
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "album-live-items.json"), JsonSerializer.Serialize(youtubeItems));
        await File.WriteAllTextAsync(Path.Combine(directory, "album-live-snapshot.json"), await core.ExecuteScriptAsync(YouTubeBridge.Snapshot));
        CheckInteraction(youtubeCollection?.Kind == "Album" && youtubeItems.Count == 10,
            "The public album did not load all ten tracks");
        CheckInteraction(YouTubeCollectionArtwork.Url is { Length: > 0 } cover && youtubeItems.All(i => i.Artwork == cover),
            "The live album still has individual video thumbnails");
        CheckInteraction(youtubeItems.Single(i => VideoIdFromWatchKey(i.Key) == "8r32UzLTgM4").IsVideo
            && youtubeItems.Single(i => VideoIdFromWatchKey(i.Key) == "imKPXhvMp8Y").IsVideo
            && !youtubeItems.Single(i => VideoIdFromWatchKey(i.Key) == "I84r8kog_jA").VideoAvailable,
            "Live album renderer data did not distinguish the clips from Go! audio");
        for (int i = 0; i < 60 && YouTubeCollectionArtwork.Source == null; i++) await Task.Delay(150);
        CheckInteraction(AlbumBackdrop.IsVisible && !WallpaperLayer.IsVisible && !wallpaperTimer.IsEnabled
            && YouTubeCollectionArtwork.Source != null && ReferenceEquals(AlbumBackdropImage.Source, YouTubeCollectionArtwork.Source),
            "The live album did not replace the home wallpaper with its own cover");
        YouTubeCardsScroll.ScrollToTop(); UpdateLayout(); await Task.Delay(300); UpdateLayout();
        Capture(Path.Combine(directory, "album-live.png"));
        await File.WriteAllTextAsync(Path.Combine(directory, "album-live-check.txt"),
            $"PASS public signed-out Ego Trippin' album: {youtubeItems.Count} tracks with shared album artwork, {youtubeItems.Count(i => i.VideoAvailable)} confirmed Video labels, Go! remains audio-only in the available catalog data.");
        await ApplyBrowserMode(false);
    }

    async Task VerifyYouTubeAlbumPresentation(CoreWebView2 core, string directory)
    {
        var previous = CaptureYouTubeView();
        const string cover = "https://yt3.googleusercontent.com/fixture-album=w544-h544";
        await core.ExecuteScriptAsync("""
            (()=>{
              const shelf=document.querySelector('ytmusic-carousel-shelf-renderer');
              window.albumFixtureOriginal=shelf.innerHTML;
              shelf.replaceChildren();
              const header=document.querySelector('ytmusic-detail-header-renderer');
              header.data={thumbnail:{musicThumbnailRenderer:{thumbnail:{thumbnails:[
                {url:'https://yt3.googleusercontent.com/fixture-album=w544-h544',width:544,height:544}
              ]}}}};
              const watch=(id,type)=>({videoId:id,watchEndpointMusicSupportedConfigs:{watchEndpointMusicConfig:{musicVideoType:type}}});
              const specs=[
                ['8r32UzLTgM4','Buenos Días','MUSIC_VIDEO_TYPE_OMV'],
                ['I84r8kog_jA','Go! (feat. FRESCO and Money Kush)','MUSIC_VIDEO_TYPE_ATV'],
                ['imKPXhvMp8Y','Super Fly (feat. LILDRUGHILL)','MUSIC_VIDEO_TYPE_OMV'],
                ['FixtureAlt1','Audio with a video counterpart','MUSIC_VIDEO_TYPE_ATV'],
                ['FixtureUnk1','Unknown recording',null]
              ];
              for(const [id,title,type] of specs){
                const row=document.createElement('ytmusic-responsive-list-item-renderer');row.style.display='block';
                const cell=document.createElement('div');cell.className='title';
                const link=document.createElement('a');link.href='/watch?v='+id;link.textContent=title;cell.append(link);row.append(cell);
                const byline=document.createElement('div');byline.className='secondary-flex-columns';byline.textContent='ROCKET';row.append(byline);
                // Same endpoint layout as the public Ego Trippin' album response.
                row.data={overlay:{musicItemThumbnailOverlayRenderer:{content:{musicPlayButtonRenderer:{playNavigationEndpoint:{watchEndpoint:watch(id,type)}}}}}};
                if(id==='FixtureAlt1')row.data.menu={menuRenderer:{items:[{menuNavigationItemRenderer:{navigationEndpoint:{watchEndpoint:watch('FixtureClip','MUSIC_VIDEO_TYPE_OMV')}}}]}};
                shelf.append(row);
              }
            })()
            """);
        var snapshot = JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.Snapshot))!;
        CheckInteraction(snapshot.Items.Length == 5 && snapshot.Items[0].IsVideo && snapshot.Items[2].IsVideo
            && !snapshot.Items[1].VideoAvailable && !snapshot.Items[4].VideoAvailable,
            "Album renderer endpoints did not distinguish official videos, audio and unknown recordings");
        CheckInteraction(!snapshot.Items[3].IsVideo && snapshot.Items[3].VideoAvailable,
            "An audio recording with a video counterpart lost its label or was changed to a video card");

        await ActivateYouTubeItem(new("/browse/MPREb_fixture", "Fixture album", "Album · ROCKET", "https://yt3.googleusercontent.com/older-small-cover", "Album"));
        youtubeMoreExhausted = true;
        CheckInteraction(youtubeCollection?.Artwork == cover && youtubeItems.All(i => i.Artwork == cover),
            "Official album did not replace every per-track thumbnail with the header artwork");
        var backdropArt = new DrawingGroup();
        using (var drawing = backdropArt.Open())
        {
            drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(27, 18, 83), Color.FromRgb(16, 65, 113), 35), null, new Rect(0, 0, 400, 400));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(133, 72, 190)), null, new Point(280, 140), 120, 145);
        }
        backdropArt.Freeze();
        var backdropImage = new DrawingImage(backdropArt); backdropImage.Freeze();
        YouTubeCollectionArtwork.Source = backdropImage;
        UpdateLayout(); await Task.Delay(50);
        CheckInteraction(AlbumBackdrop.IsVisible && !WallpaperLayer.IsVisible && !wallpaperTimer.IsEnabled
            && ReferenceEquals(AlbumBackdropImage.Source, backdropImage)
            && AlbumBackdropImage.RenderTransform is ScaleTransform { ScaleX: > 1, ScaleY: > 1 }
            && AlbumBackdropImage.Effect is System.Windows.Media.Effects.BlurEffect { Radius: >= 40 },
            "Album backdrop did not reuse, enlarge and blur the cover or left the home animation running");
        YouTubeCollectionArtwork.Source = null;
        CheckInteraction(ReferenceEquals(AlbumBackdropImage.Source, backdropImage),
            "Scrolling the header cover out of view also cleared the page background");
        YouTubeCollectionArtwork.Source = backdropImage;
        SetYouTubeItems(youtubeItems.Append(new("/watch?v=FixtureNew1", "Later album track", "ROCKET", "https://i.ytimg.com/vi/FixtureNew1/hqdefault.jpg", "Track")));
        UpdateYouTubeShelves(snapshot.Url);
        youtubeMoreExhausted = true;
        CheckInteraction(youtubeItems.Last().Artwork == cover, "Continuation rows lost the official album cover");
        SeedYouTubeQueue(youtubeItems[0].Key);
        CheckInteraction(youtubeQueue.Tracks.All(t => t.Artwork == cover) && youtubeQueueVideoIds.Contains("8r32UzLTgM4")
            && !youtubeQueueVideoIds.Contains("I84r8kog_jA"), "Album queue lost shared artwork or recording type");
        var playing = snapshot with { VideoId = "8r32UzLTgM4", Artwork = "https://i.ytimg.com/vi/8r32UzLTgM4/hqdefault.jpg" };
        CheckInteraction(WithYouTubeAlbumArtwork(playing).Artwork == cover
            && WithYouTubeAlbumArtwork(playing with { IsAd = true }).Artwork == playing.Artwork
            && WithYouTubeAlbumArtwork(playing with { VideoId = "Unrelated01" }).Artwork == playing.Artwork,
            "Album cover was lost in playback or applied to an unrelated recording");
        UpdateLayout(); await Task.Delay(150); UpdateLayout();
        static IEnumerable<TextBlock> Labels(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock text && text.Text == "Video") yield return text;
                foreach (var nested in Labels(child)) yield return nested;
            }
        }
        CheckInteraction(Labels(YouTubeCollectionTracks).Count(label => label.IsVisible) == 3,
            "Album rows do not display Video for exactly the confirmed videos and counterpart");
        Capture(Path.Combine(directory, "album-video-labels.png"));
        Width = 760; UpdateLayout(); await Task.Delay(100); UpdateLayout();
        CheckInteraction(Labels(YouTubeCollectionTracks).Count(label => label.IsVisible) == 3,
            "Compact album rows lost their Video labels");
        Capture(Path.Combine(directory, "album-video-labels-compact.png"));
        Width = 1200;

        DisplayYouTubeQueue();
        CheckInteraction(AlbumBackdrop.Visibility == Visibility.Collapsed && WallpaperLayer.IsVisible,
            "Leaving an album for the queue kept its background");
        YouTubeCollectionBack(this, new RoutedEventArgs());
        YouTubeCollectionArtwork.Source = backdropImage;
        CheckInteraction(AlbumBackdrop.IsVisible && ReferenceEquals(AlbumBackdropImage.Source, backdropImage),
            "Back from the queue did not restore the album background");
        var originalAlbum = youtubeCollection!;
        youtubeCollection = originalAlbum with { Artwork = "https://yt3.googleusercontent.com/second-album=w544-h544" };
        YouTubeCollectionArtwork.Url = youtubeCollection.Artwork;
        UpdateYouTubeShelves(snapshot.Url); UpdateLayout(); await Task.Delay(50);
        CheckInteraction(AlbumBackdropImage.Source == null, "Changing albums left the old cover in the background while loading");
        youtubeCollection = originalAlbum;
        YouTubeCollectionArtwork.Url = cover;
        YouTubeCollectionArtwork.Source = backdropImage;

        youtubeCollection = new("/playlist?list=fixture", "User playlist", "Playlist", cover, "Playlist");
        SetYouTubeItems(snapshot.Items);
        UpdateYouTubeShelves(snapshot.Url);
        CheckInteraction(AlbumBackdrop.Visibility == Visibility.Collapsed && WallpaperLayer.IsVisible,
            "Leaving an album for another page did not restore the home wallpaper");
        CheckInteraction(youtubeItems.Select(i => i.Artwork).SequenceEqual(snapshot.Items.Select(i => i.Artwork)),
            "A user playlist incorrectly replaced its per-track covers");
        youtubeCollection = previous.Collection;
        youtubeItems.Clear(); foreach (var item in previous.Items) youtubeItems.Add(item);
        YouTubeCollectionArtwork.Url = previous.Artwork;
        youtubeQueue.Tracks.Clear(); youtubeQueueVideoIds.Clear(); youtubeQueue.Index = -1;
        youtubeAlbumArtwork.Clear();
        UpdateYouTubeShelves(previous.Url);
        await core.ExecuteScriptAsync("document.querySelector('ytmusic-carousel-shelf-renderer').innerHTML=window.albumFixtureOriginal;delete document.querySelector('ytmusic-detail-header-renderer').data");
        await File.WriteAllTextAsync(Path.Combine(directory, "album-video-check.txt"),
            "PASS official album cover in initial rows, continuation, queue and playback; playlist art unchanged; renderer OMV/ATV distinction, separate counterpart availability; Video labels in regular and compact album rows.");
    }
}
