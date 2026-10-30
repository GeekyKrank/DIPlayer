using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyYouTubePaginationFixture(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory()!=null,"Pagination checks require isolated storage");
        ++browserGeneration;
        DisposeMusicBrowser();
        browserMode=true;
        var browser=new Microsoft.Web.WebView2.Wpf.WebView2();
        musicBrowser=browser;BrowserHost.Child=browser;
        browser.LayoutUpdated+=(_,_)=>ClipYouTubeHost();
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(directory,"pagination-browser"));
        await browser.EnsureCoreWebView2Async(environment);
        var core=browser.CoreWebView2;
        const string html="""
            <!doctype html><html><head><style>
            body{background:#111;color:white}ytmusic-carousel-shelf-renderer{display:block;min-height:360px}
            ytmusic-two-row-item-renderer{display:inline-block;width:110px;height:80px}
            ytmusic-continuation-item-renderer{display:block;height:20px}
            </style></head><body><div id="fixture-content"></div><ytmusic-continuation-item-renderer></ytmusic-continuation-item-renderer>
            <script>
            window.fixtureRequests=0;window.fixturePending=false;window.fixtureEnabled=false;window.fixturePage=0;
            window.fixtureAddShelf=(count=10)=>{
              const n=++window.fixturePage,s=document.createElement('ytmusic-carousel-shelf-renderer');
              s.innerHTML='<h2>Fixture section '+n+'</h2>';
              for(let i=0;i<count;i++){
                const row=document.createElement('ytmusic-two-row-item-renderer');
                row.innerHTML='<div class="title"><a href="/watch?v=fixture-'+n+'-'+i+'">Track '+n+' · '+i+'</a></div><div class="subtitle">Fixture artist</div>';
                s.appendChild(row);
              }
              document.getElementById('fixture-content').appendChild(s);
            };
            window.fixtureReset=(sections,count=10)=>{
              window.fixtureEnabled=false;window.fixturePending=false;window.fixtureRequests=0;window.fixturePage=0;
              document.getElementById('fixture-content').innerHTML='';
              for(let i=0;i<sections;i++)window.fixtureAddShelf(count);
              window.scrollTo(0,0);
            };
            window.fixtureReset(4);
            window.addEventListener('scroll',()=>{
              if(!window.fixtureEnabled||window.fixturePending)return;
              window.fixturePending=true;window.fixtureRequests++;
              setTimeout(()=>{window.fixtureAddShelf();window.fixturePending=false;},220);
            });
            </script></body></html>
            """;
        core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,e)=>
        {
            e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html; charset=utf-8");
        };
        core.Navigate("https://music.youtube.com/diplayer-pagination-fixture");
        for(int i=0;i<100;i++)
        {
            await Task.Delay(80);
            if(await core.ExecuteScriptAsync("typeof window.fixtureReset==='function'")=="true")break;
        }
        ShowYouTubeNative();youtubeTimer.Stop();
        await RefreshYouTubeNative();
        await Task.Delay(150);UpdateLayout();
        CheckInteraction(youtubeItems.Count==40&&YouTubeShelvesScroll.ScrollableHeight>500,"Pagination fixture did not produce a long gallery");
        await core.ExecuteScriptAsync("window.fixtureEnabled=true");
        await Task.Delay(800);
        CheckInteraction(await core.ExecuteScriptAsync("window.fixtureRequests")=="0","Long home eagerly loads without scrolling");
        var firstShelf=youtubeShelves[0];
        YouTubeShelvesScroll.ScrollToEnd();UpdateLayout();
        for(int i=0;i<70&&(youtubeLoadingMore||youtubeItems.Count==40);i++)await Task.Delay(100);
        CheckInteraction(youtubeItems.Count==50,"Scrolling to the end did not automatically append the next page");
        CheckInteraction(ReferenceEquals(firstShelf,youtubeShelves[0]),"Appending a page rebuilt an existing shelf");
        CheckInteraction(await core.ExecuteScriptAsync("window.fixtureRequests")=="1","One scroll launched duplicate continuation requests");
        CheckInteraction(YouTubeMoreIndicator.Visibility==Visibility.Collapsed,"Loading indicator remained after a successful page");

        youtubeNextMore=DateTime.MinValue;
        var continuation=LoadMoreYouTube();
        await LoadMoreYouTube();
        await continuation;
        CheckInteraction(youtubeItems.Count==60&&await core.ExecuteScriptAsync("window.fixtureRequests")=="2","Concurrent pagination was not coalesced into one request");

        youtubeNextMore=DateTime.MinValue;
        var stale=LoadMoreYouTube();
        var sentinel=new YouTubeItem("/watch?v=new-route","New route","",null,"Track","New route");
        youtubeKeepCards=true;
        SetYouTubeItems([sentinel]);
        UpdateYouTubeShelves("https://music.youtube.com/diplayer-pagination-next");
        await stale;
        CheckInteraction(youtubeItems.Count==1&&youtubeItems[0]==sentinel,"A stale continuation replaced the newly opened page");

        await core.ExecuteScriptAsync("history.replaceState({},'', '/diplayer-pagination-short');window.fixtureReset(1,2)");
        var shortState=JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.Snapshot))!;
        SetYouTubeItems(shortState.Items);
        UpdateYouTubeShelves(shortState.Url);
        YouTubeShelvesScroll.ScrollToTop();
        await Task.Delay(100);UpdateLayout();
        CheckInteraction(YouTubeShelvesScroll.ScrollableHeight==0,"Short pagination fixture unexpectedly overflows");
        await core.ExecuteScriptAsync("window.fixtureEnabled=true");
        RequestYouTubePageIfNeeded();
        for(int i=0;i<70&&(youtubeLoadingMore||youtubeItems.Count==2);i++)await Task.Delay(100);
        CheckInteraction(youtubeItems.Count==12,"A short viewport did not fill automatically");
        await File.WriteAllTextAsync(Path.Combine(directory,"youtube-pagination-check.txt"),
            "PASS isolated DOM: long home waits for scrolling, near-end auto-append, existing shelves preserved, concurrent requests coalesced, stale navigation ignored, short viewport filled, loading indicator cleared. No accounts or remote data modified.");
        await ApplyBrowserMode(false);
    }
}
