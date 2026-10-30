using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
namespace DIPlayer;
public partial class MainWindow
{
    async Task VerifyYouTubeMenuFixture(string directory)
    {
        ++browserGeneration;
        DisposeMusicBrowser();
        var browser=new Microsoft.Web.WebView2.Wpf.WebView2();
        musicBrowser=browser;BrowserHost.Child=browser;
        browser.LayoutUpdated+=(_,_)=>ClipYouTubeHost();
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(directory,"fixture-browser"));
        await browser.EnsureCoreWebView2Async(environment);
        var core=browser.CoreWebView2;
        core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        const string html="""
            <!doctype html><html><head><style>ytmusic-responsive-list-item-renderer,ytmusic-menu-popup-renderer,ytmusic-menu-service-item-renderer,ytmusic-playlist-add-to-option-renderer,ytmusic-player-page,ytmusic-synced-lyrics-renderer{display:block}</style></head><body style="background:#111;color:white">
            <ytmusic-responsive-list-item-renderer><a href="/watch?v=diplayer-test">Test track</a><ytmusic-menu-renderer><button onclick="document.getElementById('menu').style.display='block'">Menu</button></ytmusic-menu-renderer></ytmusic-responsive-list-item-renderer>
            <ytmusic-menu-popup-renderer id="menu" style="display:none"><ytmusic-menu-service-item-renderer onclick="this.parentElement.style.display='none';document.getElementById('dialog').setAttribute('opened','');document.getElementById('dialog').style.display='block'">Add to playlist</ytmusic-menu-service-item-renderer><ytmusic-menu-service-item-renderer onclick="window.removed=true;this.parentElement.style.display='none'">Remove from this playlist</ytmusic-menu-service-item-renderer></ytmusic-menu-popup-renderer>
            <tp-yt-paper-dialog id="dialog" style="display:none"><h2>Add to playlist</h2><ytmusic-playlist-add-to-option-renderer onclick="window.added=true;this.parentElement.style.display='none';this.parentElement.removeAttribute('opened')">Test playlist</ytmusic-playlist-add-to-option-renderer></tp-yt-paper-dialog>
            <ytmusic-player-page><div role="tab" onclick="document.getElementById('lyrics').style.display='block'">Lyrics</div><ytmusic-synced-lyrics-renderer id="lyrics" style="display:none"><div class="lyric-line active">First fixture line</div><div class="lyric-line">Second fixture line</div></ytmusic-synced-lyrics-renderer></ytmusic-player-page>
            </body></html>
            """;
        core.WebResourceRequested+=(_,e)=>
        {
            if(e.Request.Uri.StartsWith("https://music.youtube.com/diplayer-fixture",StringComparison.Ordinal))e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html; charset=utf-8");
        };
        core.Navigate("https://music.youtube.com/diplayer-fixture");
        for(int i=0;i<100;i++){await Task.Delay(100);if(await core.ExecuteScriptAsync("location.pathname==='/diplayer-fixture'&&!!document.getElementById('menu')")=="true")break;}
        ShowYouTubeNative();
        string FixtureOpen()=>YouTubeMenuBridge.Open("/watch?v=diplayer-test");
        string opened=await core.ExecuteScriptAsync(FixtureOpen());
        if(opened!="true")await File.WriteAllTextAsync(Path.Combine(directory,"fixture-dom.json"),await core.ExecuteScriptAsync("JSON.stringify({url:location.href,body:document.body?.innerHTML.slice(0,12000)})"));
        CheckInteraction(opened=="true","Fixture track menu not found");
        await ShowYouTubeMenu();
        CheckInteraction(YouTubeActionChoices.Children.Count==2&&YouTubeActionsOverlay.IsVisible,"Native actions not rendered");
        var state=JsonSerializer.Deserialize<YouTubeMenuState>(await core.ExecuteScriptAsync(YouTubeMenuBridge.Read))!;
        await core.ExecuteScriptAsync(YouTubeMenuBridge.Choose(state.Choices.First(c=>c.Label=="Add to playlist").Token,""));
        state=JsonSerializer.Deserialize<YouTubeMenuState>(await core.ExecuteScriptAsync(YouTubeMenuBridge.Read))!;
        CheckInteraction(state.Choices.Any(c=>c.Label=="Test playlist"),"Playlist chooser not mirrored");
        await core.ExecuteScriptAsync(YouTubeMenuBridge.Choose(state.Choices.First(c=>c.Label=="Test playlist").Token,""));
        CheckInteraction(await core.ExecuteScriptAsync("window.added===true")=="true","Add action not forwarded");
        CheckInteraction(await core.ExecuteScriptAsync(YouTubeMenuBridge.Choose("0:0",""))=="false","Stale menu token accepted");
        await core.ExecuteScriptAsync(FixtureOpen());
        state=JsonSerializer.Deserialize<YouTubeMenuState>(await core.ExecuteScriptAsync(YouTubeMenuBridge.Read))!;
        await core.ExecuteScriptAsync(YouTubeMenuBridge.Choose(state.Choices.First(c=>c.Label.StartsWith("Remove")).Token,""));
        CheckInteraction(await core.ExecuteScriptAsync("window.removed===true")=="true","Remove action not forwarded");
        youtubeMenuOpen=false;YouTubeActionsOverlay.Visibility=Visibility.Collapsed;
        youtubeState=new("https://music.youtube.com/watch?v=diplayer-test","Lyrics fixture","Test artist",null,10,100,false,.4,false,0,false,[]);
        YouTubeNowTitle.Text="Lyrics preview";YouTubeNowArtist.Text="Fixture artist";
        youtubeLyricsOpen=true;YouTubeLyricsOverlay.Visibility=Visibility.Visible;
        await core.ExecuteScriptAsync("document.getElementById('lyrics').style.display='block'");
        await RefreshYouTubeLyrics(true);
        CheckInteraction(YouTubeLyricsLines.Children.Count==2,"Timed lyrics not rendered");
        await Task.Delay(150);UpdateLayout();Capture(Path.Combine(directory,"lyrics-preview.png"));
        await File.WriteAllTextAsync(Path.Combine(directory,"lyrics-layout.txt"),string.Join(Environment.NewLine,new FrameworkElement[]{YouTubeNativePanel,YouTubeLyricsOverlay,YouTubeTransport,YouTubePlay,YouTubeFooterDetails,YouTubeLyricsButton}.Select(el=>el.Name+" visible="+el.IsVisible+" size="+el.ActualWidth+"x"+el.ActualHeight+" top="+el.TranslatePoint(new Point(0,0),this))));
        await core.ExecuteScriptAsync("document.getElementById('lyrics').remove()");
        youtubeLyricsSignature="";await Task.Delay(1050);await RefreshYouTubeLyrics();
        CheckInteraction(YouTubeLyricsMessage.IsVisible&&YouTubeLyricsLines.Children.Count==0,"Missing lyrics must clear old track text");
        youtubeState=youtubeState! with {Position=12,Paused=true};ObserveLyricsPlayback();
        RenderLyrics([new("First fixture",false,0),new("Next fixture",false,10),new("Last fixture",false,20)],"Fixture",null);
        CheckInteraction(youtubeActiveLyric==1,"Lyric clock did not follow playback");
        youtubeState=youtubeState with {Position=2};ObserveLyricsPlayback();ApplyLyricsHighlight();
        CheckInteraction(youtubeActiveLyric==0,"Backward seek did not update lyric");
        youtubeState=youtubeState with {Position=22};ObserveLyricsPlayback();ApplyLyricsHighlight();
        CheckInteraction(youtubeActiveLyric==2,"Forward seek did not update lyric");
        StopYouTubeLyrics();
        await File.WriteAllTextAsync(Path.Combine(directory,"menu-lyrics-check.txt"),"PASS isolated website fixtures: add to playlist, playlist chooser, remove from playlist, stale token refusal, native lyric lines and unavailable lyrics clear previous text. No account playlists modified.");
    }
}
