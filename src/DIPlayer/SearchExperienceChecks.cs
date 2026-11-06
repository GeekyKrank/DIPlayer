using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DIPlayer.Core;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;
public partial class MainWindow
{
    sealed class SearchFixtureCatalog:ICatalog
    {
        public int Requests;
        public async Task<TrackPage> Search(string query,string? cursor,CancellationToken token)
        {
            Requests++;await Task.Delay(25,token);
            return new([new("fixture-track","Get Lucky","Daft Punk",100000,null,"https://soundcloud.com/fixture/track","playable")],null);
        }
        public Task<Track> GetTrack(string id,CancellationToken token)=>throw new NotSupportedException();
        public Task<List<Track>> GetTracks(IReadOnlyList<string> ids,CancellationToken token)=>throw new NotSupportedException();
        public Task<PlaylistInfo> ResolvePlaylist(string url,CancellationToken token)=>throw new NotSupportedException();
        public Task<TrackPage> PlaylistTracks(string id,string? cursor,CancellationToken token)=>throw new NotSupportedException();
        public Task<string> Stream(string id,CancellationToken token)=>throw new NotSupportedException();
    }
    async Task VerifySearchExperience(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory()!=null,"Search checks require isolated storage");
        var oldCatalog=catalog;var oldSC=settings.SearchHistorySC;var oldYT=settings.SearchHistoryYT;
        var fixture=new SearchFixtureCatalog();catalog=fixture;
        try
        {
            browserMode=false;YouTubeNativePanel.Visibility=Visibility.Collapsed;
            SearchHeader.Visibility=NativePlayer.Visibility=Visibility.Visible;
            settings.SearchHistorySC=["Daft Punk live"];settings.SearchHistoryYT=["YouTube only request"];
            SetView("search");Activate();SearchBox.Focus();
            suppressSearchTextChange=true;SearchBox.Text="daft";suppressSearchTextChange=false;
            RefreshSearchSuggestionsFromSoundCloud("daft",[new("fixture","Get Lucky","Daft Punk",100000,null,"https://soundcloud.com/fixture/track","playable")]);
            ShowSearchSuggestions();await Task.Delay(100);UpdateLayout();
            CheckInteraction(SuggestionsPopup?.IsOpen==true&&SuggestionsList?.Items.Count>=3,"Search suggestions did not show recent queries, tracks and artists");
            KeyEventArgs KeyEvent(Key key)=>new(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,key){RoutedEvent=Keyboard.KeyDownEvent};
            HandleSearchSuggestionKey(KeyEvent(Key.Down));
            CheckInteraction(SuggestionsList!.SelectedIndex==0,"Down did not select the first suggestion");
            HandleSearchSuggestionKey(KeyEvent(Key.Up));
            CheckInteraction(SuggestionsList.SelectedIndex==SuggestionsList.Items.Count-1,"Up did not wrap to the last suggestion");
            if(SuggestionsPopup!.Child is FrameworkElement menu)
            {
                menu.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(menu);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output=File.Create(Path.Combine(directory,"search-suggestions.png"));encoder.Save(output);
            }
            HandleSearchSuggestionKey(KeyEvent(Key.Escape));
            CheckInteraction(!SuggestionsPopup.IsOpen,"Escape did not close suggestions");
            ShowSearchSuggestions();HandleSearchSuggestionKey(KeyEvent(Key.Down));HandleSearchSuggestionKey(KeyEvent(Key.Enter));
            await Task.Delay(120);
            CheckInteraction(fixture.Requests==1&&SearchBox.Text=="Daft Punk live"&&!SuggestionsPopup.IsOpen,"Selecting a suggestion did not run exactly one search");
            CheckInteraction(settings.SearchHistoryYT.SequenceEqual(new[]{"YouTube only request"}),"SoundCloud request changed YouTube search history");
            CloseSearchSuggestions();searchHistorySaveTimer.Stop();searchCancel?.Cancel();
            await VerifyAccountAndPlaybackSnapshot(directory);
            await File.WriteAllTextAsync(Path.Combine(directory,"search-experience-check.txt"),"PASS local fixtures: track/artist/recent suggestions, Up/Down/Enter/Escape, exactly one selected SoundCloud query, service-separated history, navbar account avatar, full/lite snapshot separation. All browser requests intercepted.");
        }
        finally
        {
            CloseSearchSuggestions();searchHistorySaveTimer.Stop();searchCancel?.Cancel();
            settings.SearchHistorySC=oldSC;settings.SearchHistoryYT=oldYT;catalog=oldCatalog;
        }
    }
    async Task VerifyAccountAndPlaybackSnapshot(string directory)
    {
        ++browserGeneration;DisposeMusicBrowser();browserMode=true;
        var browser=new Microsoft.Web.WebView2.Wpf.WebView2();musicBrowser=browser;BrowserHost.Child=browser;
        browser.LayoutUpdated+=(_,_)=>ClipYouTubeHost();ParkYouTubeBrowser();
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(directory,"search-browser"));
        await browser.EnsureCoreWebView2Async(environment);var core=browser.CoreWebView2;
        const string html="""
            <!doctype html><html><body>
             <ytmusic-nav-bar><ytmusic-settings-button aria-label="Fixture listener"><img src="https://yt3.googleusercontent.com/fixture-account=s88-c"></ytmusic-settings-button></ytmusic-nav-bar>
             <ytmusic-detail-header-renderer style="display:block"><div class="title">Fixture album</div></ytmusic-detail-header-renderer>
             <ytmusic-carousel-shelf-renderer style="display:block"><h2>Suggestions fixture</h2><ytmusic-two-row-item-renderer style="display:block"><div class="title"><a href="/watch?v=fixture">Fixture track</a></div><div class="subtitle">Fixture artist</div></ytmusic-two-row-item-renderer></ytmusic-carousel-shelf-renderer>
             <ytmusic-player-bar><div class="title">Playing fixture</div><div class="byline">Fixture artist</div></ytmusic-player-bar>
             <script>window.fixtureReady=true;</script>
            </body></html>
            """;
        byte[] pixel=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jrZkAAAAASUVORK5CYII=");
        core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,e)=>
        {
            bool page=e.Request.Uri.StartsWith("https://music.youtube.com/",StringComparison.Ordinal);
            e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(page?Encoding.UTF8.GetBytes(html):pixel),200,"OK",page?"Content-Type: text/html; charset=utf-8":"Content-Type: image/png");
        };
        core.Navigate("https://music.youtube.com/diplayer-search-fixture");
        for(int i=0;i<100;i++){await Task.Delay(50);if(await core.ExecuteScriptAsync("window.fixtureReady===true")=="true")break;}
        var full=JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.Snapshot));
        CheckInteraction(full is {SignedIn:true,AccountName:"Fixture listener"}&&full.AccountAvatar=="https://yt3.googleusercontent.com/fixture-account=s88-c"&&full.Items.Length==1&&full.Page?.Title=="Fixture album","Full snapshot lost account metadata or catalog rows");
        youtubeState = full;
        YouTubeNativePanel.Visibility = Visibility.Visible;
        SyncYouTubeHeader(); UpdateLayout();
        CheckInteraction(YouTubeAccountLabel.Visibility == Visibility.Collapsed && YouTubeAccountAvatar.Url == full!.AccountAvatar, "Connected account did not display its avatar instead of text");
        ToggleYouTubeProfile(this, new RoutedEventArgs());
        CheckInteraction(YouTubeProfilePopup.IsOpen && YouTubeProfileName.Text == "Fixture listener", "Avatar did not open the account menu");
        YouTubeProfilePopup.IsOpen = false;
        CheckInteraction(YouTubeProfilePopup.AllowsTransparency && ((System.Windows.Controls.Border)YouTubeProfilePopup.Child).CornerRadius.TopLeft == 12, "Account popup lost its rounded transparent corners");
        CheckInteraction(BrandLogo.IsVisible && BrandLogo.Source != null && !BrandTitle.IsVisible, "Packaged wordmark fell back to plain text");
        CheckInteraction(IsYouTubeRadio(new("/watch?v=station", "JAY-Z30 Station", "", null, "Track")) && !IsYouTubeRadio(new("/watch?v=song", "Radio", "Radiohead", null, "Track")), "Radio card filtering hides ordinary songs or keeps station cards");
        Capture(Path.Combine(directory, "account-avatar.png"));
        await VerifyYouTubeAlbumPresentation(core, directory);
        await core.ExecuteScriptAsync("(()=>{const v=document.createElement('video');document.body.append(v);window.fixturePauses=0;v.pause=()=>{window.fixturePauses++;};const p=document.createElement('div');p.id='movie_player';p.getVideoData=()=>({video_id:'old-fixture'});document.body.append(p);document.querySelector('a[href*=watch]').addEventListener('click',e=>{e.preventDefault();window.fixturePausedBeforeNavigate=window.fixturePauses===1;});})()");
        await core.ExecuteScriptAsync(YouTubeBridge.Navigate("/watch?v=fixture"));
        CheckInteraction(await core.ExecuteScriptAsync("window.fixturePausedBeforeNavigate===true") == "true", "Old recording was not stopped before switching tracks");
        await core.ExecuteScriptAsync(YouTubeBridge.Navigate("/watch?v=old-fixture"));
        CheckInteraction(await core.ExecuteScriptAsync("window.fixturePauses===1") == "true", "Selecting the current recording unexpectedly paused it");
        await core.ExecuteScriptAsync("(()=>{const original=document.querySelectorAll.bind(document);document.querySelectorAll=selector=>{if(/ytmusic-responsive-list|ytmusic-carousel|ytmusic-detail-header|ytmusic-immersive-header/.test(selector))throw new Error('catalog read in lightweight snapshot');return original(selector);};})()");
        var light=JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.PlaybackSnapshot));
        CheckInteraction(light is {Items.Length:0,Page:null,Title:"Playing fixture"}&&light.AccountAvatar==full!.AccountAvatar,"Playback snapshot walked the catalog or lost account/playback data");
        await File.WriteAllTextAsync(Path.Combine(directory,"account-playback-snapshot.json"),JsonSerializer.Serialize(new{Full=full,Light=light},new JsonSerializerOptions{WriteIndented=true}));
        await core.ExecuteScriptAsync("""
            (() => {
              const video = document.querySelector('video') || document.body.appendChild(document.createElement('video'));
              Object.defineProperties(video, {videoWidth:{value:0,writable:true}, videoHeight:{value:0,writable:true}, readyState:{value:4}, duration:{value:180}, currentTime:{value:37,writable:true}, paused:{value:true}});
              window.clipFixtureVideo = video;
              const toggle = document.body.appendChild(document.createElement('ytmusic-av-toggle'));
              const button = toggle.appendChild(document.createElement('button'));
              button.className = 'video-button ytmusic-av-toggle';
              button.addEventListener('click', () => {
                document.getElementById('movie_player').getVideoData = () => ({ video_id: 'video-fixture' });
                video.videoWidth = 1280; video.videoHeight = 720; video.currentTime = 0;
              });
            })()
            """);
        youtubeLyricsOpen = true;
        youtubeState = full! with { VideoId = "old-fixture", Duration = 180, Position = 37 };
        youtubeLyricsTrack = CurrentLyricsTrack();
        RenderLyrics([new("Fixture lyric", false, 4), new("Fixture chorus", false, 35), new("Fixture outro", false, 55)], "Fixture", null);
        await ShowTrackClip();
        await VerifyClipTypeface(core);
        youtubeState = youtubeState with { VideoId = "video-fixture", Title = "Fixture music video", Duration = 181 };
        ObserveLyricsPlayback();
        CheckInteraction(youtubeRenderedLyrics.Length == 3 && CurrentLyricsTrack() == youtubeLyricsTrack,
            "Switching to the video version cleared the original track's lyrics");
        CheckInteraction(clipMode && System.Windows.Controls.Grid.GetColumn(BrowserPanel) == 0 && System.Windows.Controls.Grid.GetColumnSpan(BrowserPanel) == 2 &&
            BrowserPanel.Margin == new Thickness(0) && System.Windows.Controls.Panel.GetZIndex(BrowserPanel) > System.Windows.Controls.Panel.GetZIndex(YouTubeLyricsOverlay) && BrowserPanel.IsHitTestVisible,
            "Clip did not cover the entire window with the existing browser");
        CheckInteraction(await core.ExecuteScriptAsync("""
            document.querySelector('video') === window.clipFixtureVideo && window.clipFixtureVideo.paused &&
              window.clipFixtureVideo.currentTime === 37 && document.querySelector('.dc-line.dc-on')?.textContent === 'Fixture chorus' &&
              document.querySelector('.dc-caption') && document.querySelector('.dc-side') &&
              !document.querySelector('.dc-meta') && getComputedStyle(window.clipFixtureVideo).objectFit === 'cover'
            """) == "true", "Clip changed playback state, lost its lyrics or missed the full-window layout");
        CheckInteraction(await core.ExecuteScriptAsync("""
            (() => {
              const viewport = document.querySelector('.dc-viewport'), text = document.querySelector('.dc-lyrics');
              const chrome = document.querySelectorAll('.dc-caption img,.dc-side img');
              const controls = Array.from(document.querySelectorAll('.dc-back,.dc-top .dc-chip'), element => getComputedStyle(element));
              return chrome.length === 9 && Array.from(chrome).every(image => image.src.startsWith('data:image/png;base64,')) &&
                getComputedStyle(document.querySelector('.dc-side')).backgroundImage === 'none' &&
                controls.length === 6 && controls.every(style => style.backgroundColor === 'rgba(0, 0, 0, 0)' &&
                  style.backdropFilter === 'none' && style.color === 'rgb(255, 255, 255)') &&
                Math.abs(parseFloat(getComputedStyle(text).top) - viewport.clientHeight * .46) < 1;
            })()
            """) == "true", "Clip chrome, transparent controls or the raised lyric position changed");
        CloseTrackClip();
        CheckInteraction(!clipMode && await core.ExecuteScriptAsync("!document.querySelector('#diplayer-clip-ui') && window.clipFixtureVideo.currentTime === 37") == "true",
            "Closing the clip did not restore the original view");
        await VerifyPublicClipFallback(core);
        await VerifyPublicClipFallback(core, videoCard: true);
        StopYouTubeLyrics();
        await File.WriteAllTextAsync(Path.Combine(directory,"clip-check.txt"),"PASS local WebView fixtures: frameless clip with original chrome, raised active lyrics, lyrics retained across recording variants, public playback, seek and timing offset, 720p/1080p badge, and return to the song.");
        ++browserGeneration;DisposeMusicBrowser();browserMode=false;
    }

    async Task VerifyClipTypeface(CoreWebView2 core)
    {
        CheckInteraction(await core.ExecuteScriptAsync("""
            (() => {
              const style = getComputedStyle(document.querySelector('.dc-lyrics'));
              return style.fontFamily.includes('Segoe UI') && style.textRendering === 'optimizelegibility';
            })()
            """) == "true", "Clip did not restore the original font with smooth rendering");
    }

    async Task VerifyPublicClipFallback(CoreWebView2 music, bool videoCard = false)
    {
        await music.ExecuteScriptAsync("""
            (() => {
              const player = document.getElementById('movie_player');
              player.getVideoData = () => ({video_id:'SOURCE_ID'});
              player.getPlayerResponse = () => ({videoDetails:{musicVideoType:'MUSIC_VIDEO_TYPE_ATV'}});
              document.querySelector('ytmusic-player-bar .byline').textContent = 'Fixture Artist';
              document.querySelector('ytmusic-av-toggle button').disabled = true;
              window.clipFixtureVideo.currentTime = 37;
            })()
            """.Replace("SOURCE_ID", videoCard ? "FixtureClip" : "FixtureSong"));
        if (videoCard)
        {
            youtubeLyricsOpen = false;
            await ActivateYouTubeItem(new("/watch?v=FixtureClip", "Playing fixture", "Fixture Artist", null, "Track", IsVideo: true));
            youtubeTimer.Stop();
            for (int attempt = 0; attempt < 50 && youtubeReading; attempt++) await Task.Delay(20);
            CheckInteraction(!youtubeReading, "The catalog refresh did not finish before setting the fixture lyrics");
            youtubeLyricsOpen = true;
        }
        youtubeState = youtubeState! with { VideoId = videoCard ? "FixtureClip" : "FixtureSong", Artist = "Fixture Artist" };
        youtubeLyricsTrack = CurrentLyricsTrack();
        lyricsChoices.Clear();
        RenderLyrics([new("Music-side lyric", true)], "Fixture", null);
        bool searched = false;
        await ShowTrackClip(configurePublicBrowser: clip =>
        {
            clip.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            clip.WebResourceRequested += (_, request) =>
            {
                string path = new Uri(request.Request.Uri).AbsolutePath;
                if (path == "/results") searched = true;
                string html = path switch
                {
                    "/results" => """
                        <ytd-video-renderer>
                          <a id="video-title" href="/watch?v=FixtureFake">Playing fixture</a>
                          <div id="channel-name"><a>Fixture Artist - Topic</a></div>
                        </ytd-video-renderer>
                        <ytd-video-renderer>
                          <a id="video-title" href="/watch?v=FixtureClip">Fixture Artist − Playing fixture (prod. by Test Producer)</a>
                          <div id="channel-name"><a>Fixture Artist Skywalker</a></div>
                        </ytd-video-renderer>
                        """,
                    "/watch" => """
                        <div id="movie_player"><video></video></div>
                        <script>
                        const player = document.getElementById('movie_player');
                        player.getVideoData = () => ({video_id:'FixtureClip'});
                        player.getPlaybackQuality = () => 'hd1080';
                        player.getAvailableQualityLevels = () => ['hd720','hd1080','hd2160'];
                        player.setPlaybackQualityRange = (min, max) => { window.fixtureQuality = [min, max]; };
                        const video = player.querySelector('video');
                        Object.defineProperties(video, {videoWidth:{value:1280,writable:true}, videoHeight:{value:720,writable:true},
                          readyState:{value:4}, duration:{value:180}, currentTime:{value:0,writable:true}});
                        </script>
                        """,
                    _ => ""
                };
                request.Response = clip.Environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK",
                    "Content-Type: text/html; charset=UTF-8\r\nContent-Security-Policy: require-trusted-types-for 'script'");
            };
        });
        CheckInteraction(clipMode && publicClipBrowser?.CoreWebView2 is not null,
            "A disabled Music toggle did not fall back to a public clip");
        CheckInteraction(searched != videoCard, "Selecting a video card did not open that video's ID directly");
        var core = publicClipBrowser!.CoreWebView2;
        await VerifyClipTypeface(core);
        string clipState = await core.ExecuteScriptAsync("""
            ({
              id: new URL(location.href).searchParams.get('v'), position: video.currentTime, paused: video.paused,
              quality: window.fixtureQuality?.join(), badge: document.querySelector('.dc-quality')?.textContent,
              line: document.querySelector('.dc-line')?.textContent, active: !!document.querySelector('.dc-line.dc-on')
            })
            """);
        using (var document = JsonDocument.Parse(clipState))
        {
            var snapshot = document.RootElement;
            CheckInteraction(snapshot.TryGetProperty("id", out var id) && id.GetString() == "FixtureClip" &&
                snapshot.GetProperty("position").GetDouble() == 37 && snapshot.GetProperty("paused").GetBoolean() &&
                snapshot.GetProperty("quality").GetString() == "hd1080,hd1080" && snapshot.GetProperty("badge").GetString() == "720p" &&
                snapshot.TryGetProperty("line", out var line) && line.GetString() == "Music-side lyric" && !snapshot.GetProperty("active").GetBoolean(),
                "Public clip selected the wrong video, lost playback state or lyrics, or misreported quality: " + clipState);
        }
        lyricsChoices.Add(new("fixture-synced", "Fixture", [new("Verse", false, 0), new("Chorus", false, 36), new("Outro", false, 60)], "Fixture"));
        await UpdateClipLyrics();
        CheckInteraction(await core.ExecuteScriptAsync("document.querySelector('.dc-line.dc-on')?.textContent === 'Chorus'") == "true",
            "Public clip did not adopt timestamps that arrived after opening");
        await core.ExecuteScriptAsync("video.currentTime=65");
        await Task.Delay(100);
        CheckInteraction(await core.ExecuteScriptAsync("document.querySelector('.dc-line.dc-on')?.textContent === 'Outro'") == "true",
            "Clip lyrics did not follow a seek on the video clock");
        await core.ExecuteScriptAsync("video.currentTime=35.75");
        HandleClipMessage("diplayer-clip-lyrics-earlier");
        await UpdateClipLyrics();
        CheckInteraction(await core.ExecuteScriptAsync("document.querySelector('.dc-line.dc-on')?.textContent === 'Chorus'") == "true" && lyricsOffset == 0,
            "Clip timing adjustment did not move the highlight independently of song timing");
        await core.ExecuteScriptAsync("player.getAvailableQualityLevels=()=>['large','hd720']; window.__diplayerClip.requestQuality()");
        CheckInteraction(await core.ExecuteScriptAsync("window.fixtureQuality?.join() === 'hd720,hd720'") == "true",
            "Public clip did not select the highest available quality below 1080p");
        await core.ExecuteScriptAsync("video.videoWidth=1920; video.videoHeight=1080; video.currentTime=64; window.__diplayerClip.requestQuality()");
        CheckInteraction(await core.ExecuteScriptAsync("document.querySelector('.dc-quality')?.textContent === '1080p'") == "true",
            "Public clip did not show the decoded 1080p resolution");
        CloseTrackClip();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (await music.ExecuteScriptAsync("window.clipFixtureVideo.currentTime === 64") == "true") return;
            await Task.Delay(50);
        }
        CheckInteraction(false, "Closing the public clip did not restore its position to the Music player");
    }
}
