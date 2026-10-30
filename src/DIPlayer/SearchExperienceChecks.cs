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
        Capture(Path.Combine(directory, "account-avatar.png"));
        await core.ExecuteScriptAsync("(()=>{const original=document.querySelectorAll.bind(document);document.querySelectorAll=selector=>{if(/ytmusic-responsive-list|ytmusic-carousel|ytmusic-detail-header|ytmusic-immersive-header/.test(selector))throw new Error('catalog read in lightweight snapshot');return original(selector);};})()");
        var light=JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.PlaybackSnapshot));
        CheckInteraction(light is {Items.Length:0,Page:null,Title:"Playing fixture"}&&light.AccountAvatar==full!.AccountAvatar,"Playback snapshot walked the catalog or lost account/playback data");
        await File.WriteAllTextAsync(Path.Combine(directory,"account-playback-snapshot.json"),JsonSerializer.Serialize(new{Full=full,Light=light},new JsonSerializerOptions{WriteIndented=true}));
        ++browserGeneration;DisposeMusicBrowser();browserMode=false;
    }
}
