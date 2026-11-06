using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace DIPlayer;
public partial class MainWindow
{
    string? youtubeCollectionArtistName;
    readonly Dictionary<string,string> youtubeAlbumArtwork = new(StringComparer.Ordinal);

    YouTubeItem WithYouTubeCollectionArtwork(YouTubeItem item)
    {
        if (item.Kind != "Track" || youtubeCollection is not { Kind: "Album", Artwork: { Length: > 0 } artwork }) return item;
        if (VideoIdFromWatchKey(item.Key) is { } id) youtubeAlbumArtwork[id] = artwork;
        return item with { Artwork = artwork };
    }

    YouTubeState WithYouTubeAlbumArtwork(YouTubeState state)
    {
        string? id = state.VideoId ?? VideoIdFromWatchKey(new Uri(state.Url).PathAndQuery);
        return !state.IsAd && id != null && youtubeAlbumArtwork.TryGetValue(id, out var artwork)
            ? state with { Artwork = artwork } : state;
    }
    void ShareYouTubeCollection(object sender,RoutedEventArgs e)
    {
        if(!Uri.TryCreate(youtubeDisplayedUrl,UriKind.Absolute,out var uri)||uri.Host!="music.youtube.com")return;
        Clipboard.SetText(uri.AbsoluteUri);
        if(sender is Button button)button.ToolTip="Link copied";
    }
    async void OpenYouTubeRowMenu(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(sender is Button{Tag:YouTubeItem item})await Guard(()=>OpenYouTubeActions(item));
    }
    async void YouTubeCollectionMenu(object sender,RoutedEventArgs e)
    {
        if(musicBrowser?.CoreWebView2==null||youtubeCollection==null)return;
        await Guard(async()=>
        {
            var core=musicBrowser.CoreWebView2;
            if(!YouTubeRouteMatches(core.Source,new Uri(youtubeDisplayedUrl).PathAndQuery))
            {
                if(await core.ExecuteScriptAsync(YouTubeBridge.Navigate(new Uri(youtubeDisplayedUrl).PathAndQuery))=="false")core.Navigate(youtubeDisplayedUrl);
                for(int i=0;i<40&&!YouTubeRouteMatches(core.Source,new Uri(youtubeDisplayedUrl).PathAndQuery);i++)await Task.Delay(150);
            }
            bool opened=await core.ExecuteScriptAsync("(()=>{const h=Array.from(document.querySelectorAll('ytmusic-detail-header-renderer,ytmusic-responsive-header-renderer')).find(e=>e.getClientRects().length);const m=h?.querySelector('ytmusic-menu-renderer');const buttons=Array.from(m?.querySelectorAll('button:not([aria-pressed]),tp-yt-paper-icon-button,yt-icon-button')||[]);const b=buttons.find(e=>e.getAttribute('aria-label')===m?.getAttribute('aria-label'))||buttons.at(-1);if(!b)return false;b.click();return true;})()") == "true";
            youtubeMenuItem=null;
            if(opened){await Task.Delay(250);await ShowYouTubeMenu();}
        });
    }
}
