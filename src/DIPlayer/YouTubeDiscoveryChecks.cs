using System.Text.Json;
using System.Windows;
namespace DIPlayer;
public partial class MainWindow
{
    async Task VerifyYouTubeDiscovery(string directory)
    {
        await ApplyBrowserMode(true);
        CheckInteraction(musicBrowser?.CoreWebView2!=null,"WebView could not start");
        var core=musicBrowser!.CoreWebView2;
        for(int i=0;i<80;i++){await Task.Delay(300);await RefreshYouTubeNative();if(youtubeItems.Count>0)break;}
        await NavigateYouTube("/search?q=Daft%20Punk","Search");
        for(int i=0;i<80;i++){await Task.Delay(300);await RefreshYouTubeNative();if(youtubeItems.Any(i=>i.Kind=="Artist"))break;}
        await File.WriteAllTextAsync(Path.Combine(directory,"artist-search.json"),JsonSerializer.Serialize(youtubeItems));
        var artist=youtubeItems.FirstOrDefault(i=>i.Kind=="Artist");
        CheckInteraction(artist!=null,"Artist not recognized in live search");
        youtubeState=new("https://music.youtube.com/","Fixture track","Lil Uzi Vert",null,0,100,true,.5,false,0,false,[],ArtistKey:"/@LILUZIVERT");
        await OpenCurrentYouTubeArtistPage();
        for(int i=0;i<90;i++){await Task.Delay(300);await RefreshYouTubeNative();if(youtubeCollection?.Kind=="Artist"&&youtubeItems.Any(i=>i.Kind=="Album"))break;}
        await File.WriteAllTextAsync(Path.Combine(directory,"artist-page.json"),await core.ExecuteScriptAsync(YouTubeBridge.Snapshot));
        await File.WriteAllTextAsync(Path.Combine(directory,"artist-header-dom.json"),await core.ExecuteScriptAsync("JSON.stringify(Array.from(document.querySelectorAll('ytmusic-immersive-header-renderer,ytmusic-visual-header-renderer')).map(e=>({html:e.outerHTML.slice(0,24000),data:e.data})))"));
        CheckInteraction(youtubeCollection?.Kind=="Artist"&&youtubeItems.Any(i=>i.Kind=="Track")&&youtubeItems.Any(i=>i.Kind=="Album"),"Artist top tracks or albums missing");
        await Task.Delay(200);UpdateLayout();
        CheckInteraction(YouTubeArtistShelves.IsVisible&&YouTubeArtistHero.IsVisible,"Artist native header and sections are hidden");
        await LoadMoreYouTube();
        await Task.Delay(1200);UpdateLayout();Capture(Path.Combine(directory,"artist.png"));
        var beforeRows=YouTubeArtistTopTracks.ItemsSource;
        await RefreshYouTubeNative();
        CheckInteraction(ReferenceEquals(beforeRows,YouTubeArtistTopTracks.ItemsSource),"Idle artist refresh rebuilds rows");
        var album=youtubeItems.First(i=>i.Kind=="Album");
        await ActivateYouTubeItem(album);
        await Task.Delay(700);UpdateLayout();
        CheckInteraction(YouTubeCollectionTable.IsVisible&&YouTubeCollectionArtwork.ActualWidth==270,"Collection header/table layout failed");
        await File.WriteAllTextAsync(Path.Combine(directory,"collection-state.json"),await core.ExecuteScriptAsync(YouTubeBridge.Snapshot));
        Capture(Path.Combine(directory,"collection-table.png"));
        YouTubeCollectionBack(this,new RoutedEventArgs());
        CheckInteraction(YouTubeArtistHero.IsVisible,"Back did not restore artist hero");
        var track=youtubeItems.First(i=>i.Kind=="Track");
        await OpenYouTubeActions(track);
        await File.WriteAllTextAsync(Path.Combine(directory,"live-menu.json"),await core.ExecuteScriptAsync(YouTubeMenuBridge.Read));
        if(!youtubeMenuOpen) await File.WriteAllTextAsync(Path.Combine(directory,"menu-dom.json"),await core.ExecuteScriptAsync("JSON.stringify({rows:Array.from(document.querySelectorAll('ytmusic-responsive-list-item-renderer')).slice(0,2).map(e=>e.outerHTML.slice(-12000)),menus:Array.from(document.querySelectorAll('ytmusic-popup-container,ytmusic-menu-popup-renderer,tp-yt-iron-dropdown')).map(e=>e.outerHTML.slice(0,18000)),status:document.activeElement?.outerHTML})"));
        CheckInteraction(youtubeMenuOpen&&YouTubeActionChoices.Children.Count>0,"Live track menu did not open");
        Capture(Path.Combine(directory,"track-actions.png"));
        CloseYouTubeActions(this,new RoutedEventArgs());
        await VerifyYouTubeMenuFixture(directory);
        requestedTrayClose=false;Close();
        CheckInteraction(IsVisible&&!quitting,"Unsolicited close hid main window");
        await File.WriteAllTextAsync(Path.Combine(directory,"discovery-check.txt"),"PASS live artist search, native artist header, top tracks, albums, continuation load, live native menu and unsolicited-close protection");
        await ApplyBrowserMode(false);
    }
}
