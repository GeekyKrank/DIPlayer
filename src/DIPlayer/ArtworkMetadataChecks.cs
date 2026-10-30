using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyArtworkMetadataFixture(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory()!=null,"Artwork metadata checks require isolated storage");
        ++browserGeneration;
        DisposeMusicBrowser();
        browserMode=true;
        var browser=new Microsoft.Web.WebView2.Wpf.WebView2();
        musicBrowser=browser;BrowserHost.Child=browser;
        browser.LayoutUpdated+=(_,_)=>ClipYouTubeHost();
        ParkYouTubeBrowser();
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(directory,"metadata-browser"));
        await browser.EnsureCoreWebView2Async(environment);
        var core=browser.CoreWebView2;
        const string html="""
            <!doctype html><html><head><style>
            ytmusic-carousel-shelf-renderer,ytmusic-two-row-item-renderer,ytmusic-immersive-header-renderer,ytmusic-player-bar{display:block}
            </style></head><body>
            <ytmusic-immersive-header-renderer id="header"><div class="title">Fixture artist</div></ytmusic-immersive-header-renderer>
            <ytmusic-carousel-shelf-renderer><h2>Metadata fixture</h2>
             <ytmusic-two-row-item-renderer id="unordered"><div class="title"><a href="/watch?v=unordered">Unordered cover</a></div><div class="subtitle">Fixture artist</div><ytmusic-thumbnail-renderer><img src="https://lh3.googleusercontent.com/cover-small=w64-h64-rj"></ytmusic-thumbnail-renderer><img src="https://lh3.googleusercontent.com/unrelated-avatar=w2048-h2048-rj"></ytmusic-two-row-item-renderer>
             <ytmusic-two-row-item-renderer id="video"><div class="title"><a href="/watch?v=video">Video image</a></div><div class="subtitle">Video · Fixture artist</div></ytmusic-two-row-item-renderer>
             <ytmusic-two-row-item-renderer id="dom"><div class="title"><a href="/watch?v=dom">DOM fallback</a></div><div class="subtitle">Fixture artist</div><img src="https://lh3.googleusercontent.com/dom-small=w64-h64-rj" data-srcset="https://lh3.googleusercontent.com/dom-medium=w320-h320-rj 320w, https://lh3.googleusercontent.com/dom-large=w1024-h1024-rj 1024w"></ytmusic-two-row-item-renderer>
            </ytmusic-carousel-shelf-renderer>
            <ytmusic-player-bar id="bar"><div class="title">Now playing</div><div class="byline">Fixture artist</div></ytmusic-player-bar>
            <script>
            document.getElementById('unordered').data={thumbnail:{musicThumbnailRenderer:{thumbnail:{thumbnails:[
              {url:'https://lh3.googleusercontent.com/cover-best=w640-h640-rj',width:640,height:640},
              {url:'https://lh3.googleusercontent.com/cover-small=w64-h64-rj',width:64,height:64},
              {url:'https://lh3.googleusercontent.com/cover-medium=w256-h256-rj',width:256,height:256}
            ]}}}};
            document.getElementById('video').data={thumbnail:{thumbnails:[
              {url:'https://lh3.googleusercontent.com/video-best=w960-h540-rw',width:960,height:540},
              {url:'https://lh3.googleusercontent.com/video-small=w120-h68-rw',width:120,height:68}
            ]}};
            document.getElementById('header').data={backgroundThumbnail:{thumbnails:[
              {url:'https://lh3.googleusercontent.com/banner-best=w1200-h675-rj',width:1200,height:675},
              {url:'https://lh3.googleusercontent.com/banner-small=w320-h180-rj',width:320,height:180}
            ]},foregroundThumbnail:{thumbnails:[{url:'https://lh3.googleusercontent.com/avatar=w240-h240-rj',width:240,height:240}]}};
            document.getElementById('bar').data={thumbnail:{thumbnails:[{url:'https://lh3.googleusercontent.com/player-small=w48-h48-rj',width:48,height:48}]}};
            navigator.mediaSession.metadata=new MediaMetadata({title:'Now playing',artist:'Fixture artist',artwork:[
              {src:'https://lh3.googleusercontent.com/player-best=w1024-h1024-rj',sizes:'1024x1024',type:'image/jpeg'},
              {src:'https://lh3.googleusercontent.com/player-medium=w256-h256-rj',sizes:'256x256',type:'image/jpeg'}
            ]});
            window.fixtureReady=true;
            </script></body></html>
            """;
        byte[] pixel=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jrZkAAAAASUVORK5CYII=");
        core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,e)=>
        {
            bool page=e.Request.Uri.StartsWith("https://music.youtube.com/",StringComparison.Ordinal);
            e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(page?Encoding.UTF8.GetBytes(html):pixel),200,"OK",page?"Content-Type: text/html; charset=utf-8":"Content-Type: image/png");
        };
        core.Navigate("https://music.youtube.com/diplayer-artwork-fixture");
        for(int i=0;i<100;i++)
        {
            await Task.Delay(80);
            if(await core.ExecuteScriptAsync("window.fixtureReady===true")=="true")break;
        }
        var state=JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.Snapshot));
        CheckInteraction(state!=null&&state.Items.Length==3,"Artwork metadata fixture did not render three tracks");
        CheckInteraction(state!.Items.Single(item=>item.Title=="Unordered cover").Artwork=="https://lh3.googleusercontent.com/cover-best=w640-h640-rj","Artwork extraction chose a smaller thumbnail from an unordered metadata array");
        CheckInteraction(state.Items.Single(item=>item.Title=="Video image").Artwork=="https://lh3.googleusercontent.com/video-best=w960-h540-rw","Video extraction changed the source URL or lost its 16:9 dimensions");
        CheckInteraction(state.Items.Single(item=>item.Title=="DOM fallback").Artwork=="https://lh3.googleusercontent.com/dom-large=w1024-h1024-rj","Lazy srcset fallback did not select its largest available candidate");
        CheckInteraction(state.Page?.Artwork=="https://lh3.googleusercontent.com/banner-best=w1200-h675-rj","Artist header chose its small avatar instead of the full banner");
        CheckInteraction(state.Artwork=="https://lh3.googleusercontent.com/player-best=w1024-h1024-rj","Now-playing extraction did not compare MediaSession image sizes");
        await File.WriteAllTextAsync(Path.Combine(directory,"artwork-metadata-state.json"),JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
        await core.ExecuteScriptAsync("""
            (()=>{
              const album=document.createElement('ytmusic-detail-header-renderer');
              album.style.display='block';album.innerHTML='<div class="title">Fixture album</div>';
              album.data={thumbnail:{thumbnails:[{url:'https://lh3.googleusercontent.com/album-cover=w640-h640-rj',width:640,height:640}]},backgroundThumbnail:{thumbnails:[{url:'https://lh3.googleusercontent.com/decorative-background=w2000-h1125-rj',width:2000,height:1125}]}};
              document.getElementById('header').replaceWith(album);
            })()
            """);
        var albumState=JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.Snapshot));
        CheckInteraction(albumState?.Page?.Artwork=="https://lh3.googleusercontent.com/album-cover=w640-h640-rj","A decorative album background replaced the album cover");
        await File.WriteAllTextAsync(Path.Combine(directory,"artwork-metadata-check.txt"),"PASS isolated DOM: unordered thumbnails choose largest image; unrelated avatar ignored; wide video URL and 16:9 size preserved; lazy srcset selects largest candidate; artist banner and MediaSession high resolution art selected; decorative album background excluded. All requests intercepted locally.");
        await ApplyBrowserMode(false);
    }
}
