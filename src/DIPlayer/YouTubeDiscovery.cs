using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace DIPlayer;
public partial class MainWindow
{
    string? youtubeRequestedArtist;
    bool youtubeLoadingMore;
    DateTime youtubeNextMore;
    string youtubePaginationRoute = "";
    int youtubePaginationGeneration = -1, youtubePaginationRevision, youtubePaginationCount, youtubeEmptyPages;
    bool youtubeMoreExhausted;

    void ResetYouTubePagination(string url, bool force = false)
    {
        string route = new Uri(url).PathAndQuery;
        if (!force && route == youtubePaginationRoute && youtubePaginationGeneration == browserGeneration)
        {
            if (youtubeItems.Count > youtubePaginationCount)
            {
                youtubePaginationCount = youtubeItems.Count;
                youtubeEmptyPages = 0;
                youtubeMoreExhausted = false;
            }
            return;
        }
        youtubePaginationRoute = route;
        youtubePaginationGeneration = browserGeneration;
        youtubePaginationRevision++;
        youtubePaginationCount = youtubeItems.Count;
        youtubeEmptyPages = 0;
        youtubeMoreExhausted = false;
        youtubeNextMore = DateTime.MinValue;
        if (YouTubeMoreIndicator != null) YouTubeMoreIndicator.Visibility = Visibility.Collapsed;
    }

    void RequestYouTubePageIfNeeded()
    {
        if (!browserMode || youtubeWebsiteVisible || youtubeOpeningCollection || youtubeQueueView || youtubeLyricsOpen || SettingsPanel.IsVisible || youtubeItems.Count == 0) return;
        var page = YouTubeShelvesScroll.IsVisible ? YouTubeShelvesScroll : YouTubeCardsScroll;
        if (!page.IsVisible || page.ViewportHeight <= 0 || page.ExtentHeight <= 0) return;
        // ScrollChanged covers the wheel, touchpad, scrollbar thumb, keyboard and
        // resizing. A small lead starts the request before the last row is reached.
        if (page.ExtentHeight - page.VerticalOffset - page.ViewportHeight <= Math.Max(280, page.ViewportHeight * .65))
            _ = Guard(LoadMoreYouTube);
    }

    bool YouTubePaginationStillCurrent(int generation, int revision, string route) =>
        generation == browserGeneration && revision == youtubePaginationRevision && browserMode &&
        !youtubeWebsiteVisible && !youtubeOpeningCollection && !youtubeQueueView &&
        new Uri(youtubeDisplayedUrl).PathAndQuery == route;

    const string YouTubeContinuePage = """
        (()=>{
          const visible=e=>!!e.getClientRects().length&&!e.closest('[hidden]');
          const root=document.scrollingElement;root?.scrollTo(0,root.scrollHeight);
          for(const el of document.querySelectorAll('ytmusic-app,ytmusic-browse-response,ytmusic-search-page,#content,#contents,ytmusic-section-list-renderer'))
            if(visible(el)&&el.scrollHeight>el.clientHeight+20){el.scrollTop=el.scrollHeight;el.dispatchEvent(new Event('scroll'));}
          const last=Array.from(document.querySelectorAll('ytmusic-continuation-item-renderer')).filter(visible).pop();
          last?.scrollIntoView({block:'end'});
          window.dispatchEvent(new Event('scroll'));
          return !!last;
        })()
        """;
    void UpdateArtistHeader(YouTubeState state)
    {
        if (youtubeKeepCards || youtubeQueueView || youtubeExpectedRoute != null && !YouTubeRouteMatches(state.Url, youtubeExpectedRoute)) return;
        var uri=new Uri(state.Url);
        if (!uri.AbsolutePath.StartsWith("/channel/") && !uri.AbsolutePath.StartsWith("/browse/UC") && !uri.AbsolutePath.StartsWith("/@")) return;
        if (state.Page is not { Title.Length: > 0 } page) return;
        bool changed=youtubeCollection?.Key!=uri.PathAndQuery || youtubeCollection?.Kind!="Artist" || YouTubeArtistHero.Visibility!=Visibility.Visible;
        bool metadataChanged=youtubeArtistPage?.Title!=page.Title||youtubeArtistPage?.Subtitle!=page.Subtitle||youtubeArtistPage?.Artwork!=page.Artwork||youtubeArtistPage?.Description!=page.Description||string.Join('|',youtubeArtistPage?.Actions?.Select(a=>a.Label)??[])!=string.Join('|',page.Actions?.Select(a=>a.Label)??[]);
        if(youtubeCollection?.Key!=uri.PathAndQuery){YouTubeArtistDescription.MaxHeight=54;YouTubeArtistHero.Height=430;YouTubeArtistReadMore.Content="More";}
        youtubeCollection=new(uri.PathAndQuery,page.Title,page.Subtitle,page.Artwork,"Artist");
        YouTubeCollectionTitle.Text=page.Title; YouTubeCollectionSubtitle.Text=page.Subtitle;
        YouTubeCollectionArtwork.Url=page.Artwork;
        youtubeArtistPage=page; if(metadataChanged&&!changed)SyncYouTubeArtistPresentation();
        if(changed)UpdateYouTubeShelves(state.Url);
    }
    async void OpenCurrentYouTubeArtist(object sender, MouseButtonEventArgs e)=>await Guard(OpenCurrentYouTubeArtistPage);
    async Task OpenCurrentYouTubeArtistPage()
    {
        if(musicBrowser?.CoreWebView2==null)return;
        string? key=youtubeState?.ArtistKey;
        string artist=youtubeState?.Artist??"Artist";
        if(key!=null){await NavigateYouTube(key,artist);return;}
        await NavigateYouTube("/search?q="+Uri.EscapeDataString(artist),artist);
        for(int i=0;i<40;i++)
        {
            await Task.Delay(200);await RefreshYouTubeNative();
            var match=youtubeItems.FirstOrDefault(item=>item.Kind=="Artist"&&item.Title.Equals(artist,StringComparison.OrdinalIgnoreCase));
            if(match!=null){await ActivateYouTubeItem(match);return;}
        }
    }
    async Task LoadMoreYouTube()
    {
        if (youtubeLoadingMore || youtubeMoreExhausted || youtubeItems.Count>=600 || musicBrowser?.CoreWebView2==null ||
            !browserMode || youtubeWebsiteVisible || youtubeQueueView || youtubeOpeningCollection || youtubeMenuOpen ||
            youtubeExpectedRoute!=null || DateTime.UtcNow<youtubeNextMore) return;
        ResetYouTubePagination(youtubeDisplayedUrl);
        youtubeLoadingMore=true;
        int generation=browserGeneration, revision=youtubePaginationRevision;
        string route=new Uri(youtubeDisplayedUrl).PathAndQuery;
        var core=musicBrowser.CoreWebView2;
        int before=youtubeItems.Count;
        YouTubeStatus.Text="Loading more…";
        YouTubeMoreIndicator.Visibility=Visibility.Visible;
        try
        {
            // Playing a track or returning with Back preserves the native gallery,
            // while the browser may be on a different route. Restore that browse
            // route before scrolling it; SPA navigation leaves the audio playing.
            if (!YouTubeRouteMatches(core.Source,route))
            {
                if (await core.ExecuteScriptAsync(YouTubeBridge.Navigate(route))=="false") return;
                for(int i=0;i<30&&!YouTubeRouteMatches(core.Source,route);i++)
                {
                    await Task.Delay(150);
                    if(!YouTubePaginationStillCurrent(generation,revision,route))return;
                }
                if(!YouTubeRouteMatches(core.Source,route))return;
                await Task.Delay(350);
            }
            if(!YouTubePaginationStillCurrent(generation,revision,route))return;
            bool continuation=await core.ExecuteScriptAsync(YouTubeContinuePage)=="true";
            for(int attempt=0;attempt<5;attempt++)
            {
                await Task.Delay(650);
                if(!YouTubePaginationStillCurrent(generation,revision,route)||!YouTubeRouteMatches(core.Source,route))return;
                string json=await core.ExecuteScriptAsync(YouTubeBridge.Snapshot);
                if(!YouTubePaginationStillCurrent(generation,revision,route)||json.Length>1024*1024)return;
                var state=JsonSerializer.Deserialize<YouTubeState>(json);
                if(state==null||!YouTubeRouteMatches(state.Url,route))return;
                var incoming=state.Items.Where(item=>!IsYouTubeLikesDuplicate(item,state.Url));
                if(youtubeCollection is {Kind:not "Artist"})incoming=incoming.Where(item=>item.Kind=="Track");
                // Restored galleries can contain more cached pages than the site's
                // initial route render; keep those rows while catching up.
                var fresh=incoming.ToDictionary(item=>(item.Section,item.Key),item=>item);
                var merged=youtubeItems.Select(item=>fresh.TryGetValue((item.Section,item.Key),out var updated)?updated:item)
                    .Concat(incoming).DistinctBy(item=>(item.Section,item.Key)).Take(600).ToArray();
                if(!merged.SequenceEqual(youtubeItems))
                {
                    SetYouTubeItems(merged);
                    UpdateYouTubeShelves(state.Url);
                }
                if(youtubeItems.Count>before)
                {
                    youtubeEmptyPages=0;youtubeMoreExhausted=false;
                    youtubeNextMore=DateTime.UtcNow.AddMilliseconds(750);
                    return;
                }
                // A restored long gallery may need several remote continuation
                // batches to reach the rows it already has cached.
                if(attempt==2)continuation=await core.ExecuteScriptAsync(YouTubeContinuePage)=="true"||continuation;
            }
            youtubeEmptyPages++;
            youtubeMoreExhausted=!continuation&&youtubeEmptyPages>=2;
            youtubeNextMore=DateTime.UtcNow.AddSeconds(Math.Min(30,3*Math.Pow(2,youtubeEmptyPages-1)));
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if(YouTubePaginationStillCurrent(generation,revision,route))youtubeNextMore=DateTime.UtcNow.AddSeconds(5);
        }
        finally
        {
            youtubeLoadingMore=false;
            YouTubeMoreIndicator.Visibility=Visibility.Collapsed;
            if(YouTubePaginationStillCurrent(generation,revision,route))
                YouTubeStatus.Text=$"{youtubeItems.Count} items · YouTube Music";
        }
    }
    async Task RefreshYouTubeCollectionItems()
    {
        if(musicBrowser?.CoreWebView2==null)return;
        int generation=browserGeneration, revision=youtubePaginationRevision;
        string route=new Uri(youtubeDisplayedUrl).PathAndQuery;
        var json=await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Snapshot);
        if(json.Length>1024*1024||!YouTubePaginationStillCurrent(generation,revision,route))return;
        var state=JsonSerializer.Deserialize<YouTubeState>(json);
        if(state==null||!YouTubeRouteMatches(state.Url,route))return;
        SetYouTubeItems(state.Items.Where(item=>item.Kind=="Track").Take(600));
        UpdateYouTubeShelves(state.Url);
    }
    async void YouTubeCardMenu(object sender, MouseButtonEventArgs e)
    {
        if(sender is not Button {Tag:YouTubeItem item} button)return;
        e.Handled=true;
        var menu=new ContextMenu {Style=(Style)FindResource("TrackContextMenu")};
        void Add(string label,Func<Task> action){var entry=new MenuItem {Header=label,Style=(Style)FindResource("TrackContextMenuItem")};entry.Click+=async(_,_)=>await Guard(action);menu.Items.Add(entry);}
        if(item.Kind=="Track") Add("Add to queue",()=>AddYouTubeQueueItem(item));
        if(item.ArtistKey!=null) Add("Open artist",()=>NavigateYouTube(item.ArtistKey,"Artist"));
        Add("YouTube Music actions…",()=>OpenYouTubeActions(item));
        menu.PlacementTarget=button;menu.IsOpen=true;
    }
    YouTubeItem? youtubeMenuItem;
    async Task AddYouTubeQueueItem(YouTubeItem item,bool next=false)
    {
        var track=new DIPlayer.Core.Track(item.Key,item.Title,item.Subtitle,0,item.Artwork,"https://music.youtube.com"+item.Key,"playable");
        if(next)youtubeQueue.Tracks.Insert(Math.Clamp(youtubeQueue.Index+1,0,youtubeQueue.Tracks.Count),track);
        else youtubeQueue.Add(track);
        youtubeQueue.ResetNavigation();youtubeQueueCleared=false;
        await ConfigureYouTubeQueue();SyncYouTubeNavigation();
    }
    bool youtubeMenuOpen;
    async Task OpenYouTubeActions(YouTubeItem item)
    {
        if(musicBrowser?.CoreWebView2==null)return;
        youtubeMenuItem=item;
        bool opened=await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeMenuBridge.Open(item.Key))=="true";
        if(!opened && youtubeCollection!=null)
        {
            if(await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Navigate(new Uri(youtubeDisplayedUrl).PathAndQuery))=="false")musicBrowser.CoreWebView2.Navigate(youtubeDisplayedUrl);
            for(int i=0;i<40&&!opened;i++){await Task.Delay(200);if(!browserMode||musicBrowser?.CoreWebView2==null)return;opened=await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeMenuBridge.Open(item.Key))=="true";}
        }
        if(!opened){YouTubeStatus.Visibility=Visibility.Visible;YouTubeStatus.Text="Track actions are not available on this page. Open its playlist first.";return;}
        await Task.Delay(200);await ShowYouTubeMenu();
    }
    async Task ShowYouTubeMenu()
    {
        if(musicBrowser?.CoreWebView2==null)return;
        string json=await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeMenuBridge.Read);
        if(json.Length>128*1024)return;
        var state=JsonSerializer.Deserialize<YouTubeMenuState>(json);
        if(state==null||state.Choices.Length==0){youtubeMenuOpen=false;YouTubeActionsOverlay.Visibility=Visibility.Collapsed;return;}
        youtubeMenuOpen=true;YouTubeActionsTitle.Text=state.Title;
        YouTubeActionsInput.Visibility=state.TextInput?Visibility.Visible:Visibility.Collapsed;
        YouTubeActionsInput.Text=state.InputValue;
        YouTubeActionChoices.Children.Clear();
        foreach(var choice in state.Choices)
        {
            var button=new Button {Content=choice.Label,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,9,12,9)};
            button.Click+=async(_,_)=>await Guard(async()=>
            {
                if(musicBrowser?.CoreWebView2==null)return;
                button.IsEnabled=false;
                if(choice.Action is "queue" or "play-next" && youtubeMenuItem is { Kind:"Track" } queued)
                {
                    await AddYouTubeQueueItem(queued,choice.Action=="play-next");
                    CloseYouTubeActions(this,new RoutedEventArgs());return;
                }
                bool selected=await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeMenuBridge.Choose(choice.Token,YouTubeActionsInput.Text))=="true";
                if(!selected){await ShowYouTubeMenu();return;}
                await Task.Delay(450);await ShowYouTubeMenu();
                if(!youtubeMenuOpen)
                {
                    var actual=new Uri(musicBrowser.CoreWebView2.Source);
                    if(actual.AbsolutePath!="/watch" && !YouTubeRouteMatches(actual.AbsoluteUri,new Uri(youtubeDisplayedUrl).PathAndQuery))
                    {youtubeCollection=null;youtubeKeepCards=false;youtubeExpectedRoute=null;youtubeItemsSignature="";await RefreshYouTubeNative();}
                    else if(youtubeCollection?.Kind=="Playlist")await RefreshYouTubeCollectionItems();
                    else if(youtubeCollection==null) {youtubeKeepCards=false;youtubeItemsSignature="";await RefreshYouTubeNative();}
                }
            });
            YouTubeActionChoices.Children.Add(button);
        }
        YouTubeActionsOverlay.Visibility=Visibility.Visible;
    }
    async void CloseYouTubeActions(object sender,RoutedEventArgs e)
    {
        youtubeMenuOpen=false;YouTubeActionsOverlay.Visibility=Visibility.Collapsed;
        if(musicBrowser?.CoreWebView2!=null)await Guard(async()=>{await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeMenuBridge.Close);});
    }
}
