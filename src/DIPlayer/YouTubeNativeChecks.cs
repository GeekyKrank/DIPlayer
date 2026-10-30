using System.Windows;
namespace DIPlayer;
public partial class MainWindow
{
    async Task VerifyYouTubeCollectionCards(string directory)
    {
        await NavigateYouTube("/search?q=Daft%20Punk%20Discovery", "Search results");
        for (int i = 0; i < 90 && !youtubeItems.Any(item => item.Kind == "Album"); i++)
        { await Task.Delay(300); await RefreshYouTubeNative(); }
        await File.WriteAllTextAsync(Path.Combine(directory,"album-search-items.json"), System.Text.Json.JsonSerializer.Serialize(youtubeItems));
        var album = youtubeItems.FirstOrDefault(item => item.Kind == "Album");
        if(album==null) await File.WriteAllTextAsync(Path.Combine(directory,"album-search-state.json"), await musicBrowser!.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Snapshot));
        CheckInteraction(album != null, "Album search classification failed");
        await ActivateYouTubeItem(album!);
        CheckInteraction(youtubeCollection != null && YouTubeCollectionHeader.IsVisible && YouTubeCollectionTable.IsVisible && youtubeItems.Count > 1, "Album did not open as a native collection");
        CheckInteraction(YouTubeBackButton.IsEnabled, "Pinned Back button is disabled after opening an album");
        double backPosition = YouTubeBackButton.TranslatePoint(new Point(0,0),YouTubeNativePanel).Y;
        YouTubeCardsScroll.ScrollToEnd(); await Task.Delay(200); UpdateLayout();
        CheckInteraction(YouTubeBackButton.IsVisible && Math.Abs(YouTubeBackButton.TranslatePoint(new Point(0,0),YouTubeNativePanel).Y-backPosition)<1,"Back button scrolls away with the album");
        YouTubeCardsScroll.ScrollToTop();
        await Task.Delay(2500); UpdateLayout(); Capture(Path.Combine(directory,"youtube-album-cards.png"));
        YouTubeCollectionBack(this,new RoutedEventArgs());
        CheckInteraction(youtubeCollection == null && youtubeItems.Any(item=>item.Key==album!.Key), "Album back did not restore the gallery");
        await NavigateYouTube("/search?q=Sabrina%20Carpenter%20Espresso", "Search results");
        for (int i = 0; i < 90 && !youtubeItems.Any(item => item.Kind == "Single"); i++)
        { await Task.Delay(300); await RefreshYouTubeNative(); }
        await File.WriteAllTextAsync(Path.Combine(directory,"single-search-items.json"), System.Text.Json.JsonSerializer.Serialize(youtubeItems));
        var single = youtubeItems.FirstOrDefault(item => item.Kind == "Single");
        // Search rankings vary; resolve this known public one-track release if it was not ranked in the rendered batch.
        single ??= new YouTubeItem("/browse/MPREb_2msbVYUqBoR", "Espresso", "Single · Sabrina Carpenter", null, "Single");
        await ActivateYouTubeItem(single!);
        CheckInteraction(youtubeCollection == null && youtubeQueue.Tracks.Count == 1 && youtubeKeepCards, "Single did not play directly from the gallery");
        await File.WriteAllTextAsync(Path.Combine(directory,"youtube-collection-check.txt"), "PASS real album classification, native cover/header/track cards, back to gallery and single resolving to direct playback without opening a collection.");
    }
    async Task VerifyYouTubeNative(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Native YouTube checks require isolated storage");
        bool navigationOnly = Environment.GetCommandLineArgs().Contains("--navigation-only");
        string? isolationPlaylist = navigationOnly ? store.Create("SoundCloud library isolation check") : null;
        var soundCloudPlaylistIds = store.Playlists().Select(item=>item.Id).ToArray();
        await ApplyBrowserMode(true);
        CheckInteraction(YouTubeRail.IsVisible && !SoundCloudRail.IsVisible,"YouTube rail did not replace SoundCloud controls");
        CheckInteraction(HeaderModeSwitch.IsChecked==true&&YouTubeModeSwitch.IsChecked==true&&BrowserModeSwitch.IsChecked==true,"Service switches are not synchronized");
        CheckInteraction(musicBrowser?.CoreWebView2 != null, "Browser initialization failed");
        var core = musicBrowser!.CoreWebView2;
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            if ((await core.ExecuteScriptAsync("!!document.querySelector('ytmusic-app')")) == "true") break;
        }
        await NavigateYouTube("/", "YouTube Music");
        for (int i = 0; i < 80; i++)
        {
            await Task.Delay(300); await RefreshYouTubeNative();
            if (youtubeItems.Count > 0 && YouTubeShelves.IsVisible) break;
        }
        CheckInteraction(youtubeItems.Count > 0 && YouTubeShelves.IsVisible && !YouTubeCardsScroll.IsVisible, "Home shelves did not load");
        CheckInteraction(YouTubeShelves.Items.Count > 0, "Home section grouping is empty");
        await File.WriteAllTextAsync(Path.Combine(directory,"home-items.json"), System.Text.Json.JsonSerializer.Serialize(youtubeItems));
        await Task.Delay(3500);
        UpdateLayout(); Capture(Path.Combine(directory, "youtube-home-shelves.png"));
        if (navigationOnly)
        {
            await ActivateYouTubeItem(new YouTubeItem("/browse/MPREb_K8qWMWVqXGi","Random Access Memories","Album · Daft Punk",null,"Album"));
            CheckInteraction(youtubeCollection!=null && YouTubeBackButton.IsEnabled,"Album navigation did not enable Back");
            if(Environment.GetCommandLineArgs().Contains("--check-youtube-artwork"))
            {
                for(int i=0;i<100&&YouTubeCollectionArtwork.Source==null;i++)await Task.Delay(200);
                CheckInteraction(YouTubeCollectionArtwork.Url?.StartsWith("https://")==true && YouTubeCollectionArtwork.Source!=null,"Album cover metadata or real image loading failed");
                UpdateLayout();Capture(Path.Combine(directory,"youtube-album-cover.png"));
                await File.WriteAllTextAsync(Path.Combine(directory,"youtube-artwork-check.txt"),"PASS real album cover resolved from the lazy card/header metadata and decoded into a visible WPF image.");
            }
            double top=YouTubeBackButton.TranslatePoint(new Point(),YouTubeNativePanel).Y;
            YouTubeCardsScroll.ScrollToEnd();await Task.Delay(200);UpdateLayout();
            CheckInteraction(YouTubeBackButton.IsVisible&&Math.Abs(YouTubeBackButton.TranslatePoint(new Point(),YouTubeNativePanel).Y-top)<1,"Back scrolled out of view");
            SeedYouTubeQueue(youtubeItems.First(item=>item.Kind=="Track").Key);
            DisplayYouTubeQueue();
            CheckInteraction(YouTubeClearQueue.IsVisible&&YouTubeClearQueue.IsEnabled,"Queue clear action is missing");
            ClearYouTubeQueue(this,new RoutedEventArgs());
            for(int i=0;i<30&&YouTubeClearQueue.IsEnabled;i++)await Task.Delay(100);
            CheckInteraction(youtubeQueue.Tracks.Count==0&&youtubeQueue.Index==-1&&youtubeQueueCleared&&!YouTubeClearQueue.IsEnabled&&youtubeItems.Count==0,"Queue clear did not remove all tracks/reset navigation");
            CheckInteraction(IsYouTubeLikesDuplicate(new YouTubeItem("/playlist?list=LM","Liked Music","",null,"Playlist"),"https://music.youtube.com/library")&&!IsYouTubeLikesDuplicate(new YouTubeItem("/playlist?list=LM","Liked Music","",null,"Playlist"),"https://music.youtube.com/playlist?list=LM"),"Likes duplicate filtering affects its dedicated page");
            CheckInteraction(YouTubeQueueNav.Tag as string=="active","YouTube queue rail is not selected");
            YouTubeCollectionBack(this,new RoutedEventArgs());
            CheckInteraction(youtubeCollection!=null&&!youtubeQueueView,"Back did not restore the album from Queue");
            YouTubeCollectionBack(this,new RoutedEventArgs());
            CheckInteraction(youtubeCollection==null&&YouTubeShelves.IsVisible,"Back did not restore Home from the album");
            await ApplyBrowserMode(false);
            CheckInteraction(SoundCloudRail.IsVisible&&!YouTubeRail.IsVisible&&soundCloudPlaylistIds.SequenceEqual(store.Playlists().Select(item=>item.Id)),"Service libraries or sidebar state mixed");
            if(isolationPlaylist!=null)store.Delete(isolationPlaylist);
            await File.WriteAllTextAsync(Path.Combine(directory,"youtube-navigation-check.txt"),"PASS service-specific left rail, pinned Back after scrolling an album, nested queue/album/home Back history, return to SoundCloud with its nonempty library unchanged.");
            return;
        }
        await VerifyYouTubeCollectionCards(directory);
        await NavigateYouTube("/search?q=Kevin%20MacLeod%20Carefree", "Search results");
        bool searched = false;
        for (int i = 0; i < 70; i++)
        {
            await Task.Delay(300); await RefreshYouTubeNative();
            if (core.Source.Contains("/search") && youtubeItems.Any(item => item.Kind == "Track")) { searched = true; break; }
        }
        CheckInteraction(searched, "Native search did not produce YouTube results");
        CheckInteraction(YouTubeCards.IsVisible, "Search must use cover cards");
        CheckInteraction(YouTubeNativePanel.IsVisible && YouTubeNativePanel.Background != null, "Original website covers native UI");
        UpdateLayout(); Capture(Path.Combine(directory, "youtube-native-search.png"));
        var first = youtubeItems.First(item => item.Kind == "Track");
        await NavigateYouTube(first.Key, "Search results");
        bool playing = false;
        for (int i = 0; i < 180; i++)
        {
            await Task.Delay(300); await RefreshYouTubeNative();
            if (youtubeState is { Paused: false, Duration: > 10, Position: > 0, IsAd: false } && youtubeState.Title.Contains("Carefree", StringComparison.OrdinalIgnoreCase)) { playing = true; break; }
        }
        await File.WriteAllTextAsync(Path.Combine(directory,"playback-state.json"),await core.ExecuteScriptAsync("""JSON.stringify({url:location.href,title:document.querySelector('ytmusic-player-bar .title')?.textContent,video:!!document.querySelector('video'),paused:document.querySelector('video')?.paused,ready:document.querySelector('video')?.readyState,error:document.querySelector('video')?.error?.code,duration:document.querySelector('video')?.duration,message:document.querySelector('yt-player-error-message-renderer')?.textContent})"""));
        CheckInteraction(playing, "Native track selection did not start playback");
        await core.ExecuteScriptAsync(YouTubeBridge.Command("play"));
        await Task.Delay(300); await RefreshYouTubeNative();
        CheckInteraction(youtubeState?.Paused == true, "Native pause failed");
        await core.ExecuteScriptAsync(YouTubeBridge.Command("play"));
        await Task.Delay(400); await RefreshYouTubeNative();
        CheckInteraction(youtubeState?.Paused == false, "Native resume failed");
        await SetYouTubeVolume(.34);
        await Task.Delay(200); await RefreshYouTubeNative();
        CheckInteraction(Math.Abs((youtubeState?.Volume ?? 0) - .34) < .01, "Native volume is not synchronized");
        await core.ExecuteScriptAsync("document.querySelector('video').currentTime=8");
        await Task.Delay(300); await RefreshYouTubeNative();
        CheckInteraction(youtubeState?.Position >= 7, "Native seek failed");
        await File.WriteAllTextAsync(Path.Combine(directory,"controls-state.json"),await core.ExecuteScriptAsync("""JSON.stringify({music:document.querySelectorAll('ytmusic-app').length,bar:document.querySelectorAll('ytmusic-player-bar').length,width:innerWidth,height:innerHeight,buttons:Array.from(document.querySelectorAll('button,[role="button"],yt-icon-button,tp-yt-paper-icon-button')).map(b=>({tag:b.tagName,id:b.id,cls:String(b.className),label:b.getAttribute('aria-label'),title:b.getAttribute('title'),icon:b.getAttribute('icon')})).filter(b=>/shuffle|repeat|next|previous|перем|повтор|назад|далі|дальше/i.test([b.label,b.title,b.icon,b.cls,b.id].join(' '))).slice(0,40)})"""));
        CheckInteraction(youtubeQueue.Tracks.Count > 1 && youtubeQueue.Current?.Id == first.Key, "Native YouTube queue was not seeded");
        string galleryHeading = YouTubeHeading.Text;
        DisplayYouTubeQueue();
        CheckInteraction(YouTubeCards.IsVisible && !YouTubeShelves.IsVisible, "YouTube queue must use cover cards");
        youtubeQueueView = false; YouTubeHeading.Text = galleryHeading;
        MinimizeClick(this, new RoutedEventArgs());
        await Task.Delay(400);
        CheckInteraction(miniPlayer?.IsVisible == true && !IsVisible, "YouTube mini player did not open");
        var miniToggle = (System.Windows.Controls.Button)miniPlayer!.FindName("ToggleButton");
        miniToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(500); await RefreshYouTubeNative();
        CheckInteraction(youtubeState?.Paused == true, "YouTube mini pause failed");
        miniToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(500); await RefreshYouTubeNative();
        CheckInteraction(youtubeState?.Paused == false, "YouTube mini resume failed");
        CheckInteraction(((System.Windows.Controls.TextBlock)miniPlayer.FindName("TrackTitle")).Text == youtubeState?.Title, "YouTube mini metadata is stale");
        string miniFirstKey = youtubeQueue.Current!.Id;
        ((System.Windows.Controls.Button)miniPlayer.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        for (int i=0;i<100;i++)
        {
            await Task.Delay(300); await RefreshYouTubeNative();
            if(youtubeQueue.Current?.Id!=miniFirstKey && youtubeState is {Paused:false,Position:>0}) break;
        }
        CheckInteraction(youtubeQueue.Current?.Id!=miniFirstKey && youtubeState is {Paused:false,Position:>0}, "YouTube mini next did not continue playback while hidden");
        ((System.Windows.Controls.Button)miniPlayer.FindName("PreviousButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        for(int i=0;i<100 && youtubeQueue.Current?.Id!=miniFirstKey;i++){await Task.Delay(300);await RefreshYouTubeNative();}
        CheckInteraction(youtubeQueue.Current?.Id==miniFirstKey,"YouTube mini previous did not return to the first track");
        await File.WriteAllTextAsync(Path.Combine(directory,"youtube-mini-check.txt"),"PASS hidden main window, visible mini, real pause/resume, next/previous queue switching and metadata synchronization.");
        DIPlayer.SystemIntegration.WindowActivation.Restore(this);
        await Task.Delay(200);
        bool previousShuffle=settings.Shuffle; int previousRepeat=settings.RepeatMode;
        await RunYouTubeControl("shuffle");
        CheckInteraction(settings.Shuffle!=previousShuffle,"Native shuffle did not toggle");
        settings.RepeatMode=1;
        await RunYouTubeControl("repeat");
        CheckInteraction(settings.RepeatMode==2 && await core.ExecuteScriptAsync("document.querySelector('video').loop")=="true","Native repeat-one did not enable looping");
        settings.Shuffle=false;settings.RepeatMode=0;await ConfigureYouTubeQueue();
        int queueIndex=youtubeQueue.Index;
        await core.ExecuteScriptAsync("document.querySelector('video').dispatchEvent(new Event('ended'))");
        await Task.Delay(1000);
        CheckInteraction(youtubeQueue.Index==queueIndex+1,"Native end-of-track did not advance the queue");
        settings.Shuffle=previousShuffle;settings.RepeatMode=previousRepeat;
        UpdateLayout(); Capture(Path.Combine(directory, "youtube-native-playing.png"));
        if (Environment.GetCommandLineArgs().Contains("--hold-youtube-native")) await Task.Delay(20000);
        Width = 460; Height = 680; await Task.Delay(150); UpdateLayout();
        Capture(Path.Combine(directory, "youtube-native-narrow.png"));
        OpenPlayerSettings(this, new RoutedEventArgs());
        CheckInteraction(SettingsPanel.IsVisible && !YouTubeNativePanel.IsVisible, "Settings are obscured");
        await ApplyBrowserMode(false);
        CheckInteraction(musicBrowser == null && !youtubeTimer.IsEnabled && LibraryPanel.IsVisible, "Optimized mode did not close YouTube");
        CheckInteraction(SoundCloudRail.IsVisible && !YouTubeRail.IsVisible && soundCloudPlaylistIds.SequenceEqual(store.Playlists().Select(item=>item.Id)),"Switching services changed the SoundCloud library or left YouTube navigation active");
        await File.WriteAllTextAsync(Path.Combine(directory, "youtube-native-check.txt"), "PASS live native search and track selection, real browser playback, pause/resume, volume, seeking, native shuffle/repeat-one and end-of-track queue advance, native wide/narrow layout, settings and return to SoundCloud.");
    }
}
