using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace DIPlayer;
public partial class MainWindow
{
    YouTubePage? youtubeArtistPage;
    string youtubeArtistActionsSignature="";
    string youtubeArtistPresentationKey="";
    bool youtubeArtistBannerObserved,youtubeArtistExpanded;
    void SyncYouTubeArtistPresentation()
    {
        bool artist=youtubeCollection?.Kind=="Artist";
        YouTubeArtistHero.Visibility=artist?Visibility.Visible:Visibility.Collapsed;
        YouTubeArtistTopSection.Visibility=artist?Visibility.Visible:Visibility.Collapsed;
        if(!artist){YouTubeArtistDescription.Visibility=Visibility.Collapsed;return;}
        string key=youtubeCollection!.Key;
        if(youtubeArtistPresentationKey!=key)
        {
            youtubeArtistPresentationKey=key;youtubeArtistExpanded=false;
            YouTubeArtistDescription.Visibility=Visibility.Collapsed;
            YouTubeArtistDescription.MaxHeight=double.PositiveInfinity;
        }
        var page=youtubeArtistPage;
        YouTubeArtistName.Text=page?.Title??youtubeCollection!.Title;
        YouTubeArtistListeners.Text=page?.Subtitle??youtubeCollection!.Subtitle;
        YouTubeArtistBanner.Url=page?.Artwork??youtubeCollection!.Artwork;
        YouTubeArtistBannerBackdrop.Url=settings.SoftBackdrop?YouTubeArtistBanner.Url:null;
        YouTubeArtistBannerBackdrop.Visibility=settings.SoftBackdrop?Visibility.Visible:Visibility.Collapsed;
        if(!youtubeArtistBannerObserved)
        {
            DependencyPropertyDescriptor.FromProperty(Image.SourceProperty,typeof(UI.Artwork))
                .AddValueChanged(YouTubeArtistBanner,YouTubeArtistBannerImageChanged);
            youtubeArtistBannerObserved=true;
        }
        YouTubeArtistBannerImageChanged(this,EventArgs.Empty);
        UpdateYouTubeArtistHeroLayout();
        YouTubeArtistDescription.Text=page?.Description??"";
        YouTubeArtistReadMore.Content=YouTubeArtistDescription.Visibility==Visibility.Visible?"Less":"About";
        YouTubeArtistReadMore.Visibility=string.IsNullOrWhiteSpace(page?.Description)?Visibility.Collapsed:Visibility.Visible;
        var groups=youtubeItems.GroupBy(i=>i.Section).ToArray();
        var top=groups.FirstOrDefault(g=>g.Any(i=>i.Kind=="Track"&&!i.IsVideo));
        YouTubeArtistTopTitle.Text=top?.Key is {Length:>0} title&&title!="YouTube Music"?title:"Popular";
        var tracks=top?.Where(i=>i.Kind=="Track").ToArray()??[];
        YouTubeArtistTopTracks.ItemsSource=youtubeArtistExpanded?tracks:tracks.Take(5).ToArray();
        YouTubeArtistTopMore.Tag=top?.FirstOrDefault(i=>i.Kind=="Browse");
        YouTubeArtistTopMore.Content=youtubeArtistExpanded?"Show less":"Show all";
        YouTubeArtistTopMore.Visibility=YouTubeArtistTopMore.Tag!=null||tracks.Length>5?Visibility.Visible:Visibility.Collapsed;
        YouTubeArtistTopSection.Visibility=top!=null?Visibility.Visible:Visibility.Collapsed;
        YouTubeArtistPlay.IsEnabled=tracks.Length>0;
        YouTubeArtistShelves.ItemsSource=groups.Where(g=>g!=top).Select(g=>new YouTubeShelf(g.Key,g.ToArray())).ToArray();
        string signature=string.Join('|',page?.Actions?.Select(a=>a.Label)??[]);
        if(signature==youtubeArtistActionsSignature)return;
        youtubeArtistActionsSignature=signature;YouTubeArtistActions.Children.Clear();
        var actions=(page?.Actions??[]).Where(action=>!System.Text.RegularExpressions.Regex.IsMatch(action.Label,"shuffle|переміш|перемеш|^play$|відтворити|воспроизвести",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray();
        var follow=actions.FirstOrDefault(action=>System.Text.RegularExpressions.Regex.IsMatch(action.Label,"subscrib|follow|library|подпис|підпис|бібліот|библиот",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if(follow!=null)
        {
            bool active=System.Text.RegularExpressions.Regex.IsMatch(follow.Label,"subscribed|following|unsubscribe|подписан|підписан|отпис|відпис",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var button=ArtistIconButton(active?UI.Symbols.Check:UI.Symbols.Plus,follow.Label);
            button.Tag=follow.Label;
            button.Click+=YouTubeArtistActionClick;YouTubeArtistActions.Children.Add(button);
        }
        var remaining=actions.Where(action=>action!=follow).ToArray();
        if(remaining.Length>0)
        {
            var menu=new ContextMenu {Style=(Style)FindResource("TrackContextMenu")};
            foreach(var action in remaining)
            {
                var item=new MenuItem {Header=action.Label,Tag=action.Label,Style=(Style)FindResource("TrackContextMenuItem")};
                item.Click+=YouTubeArtistActionClick;menu.Items.Add(item);
            }
            var more=ArtistIconButton(UI.Symbols.More,"More artist actions");more.ContextMenu=menu;
            more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};
            YouTubeArtistActions.Children.Add(more);
        }
    }
    static Button ArtistIconButton(string glyph,string label)
    {
        var button=new Button
        {
            Content=glyph,FontFamily=UI.Symbols.Font,FontWeight=FontWeights.Normal,FontSize=15,Width=38,Height=38,
            Padding=new Thickness(0),Margin=new Thickness(0,0,6,0),ToolTip=label,
            Background=new SolidColorBrush(Color.FromArgb(84,20,20,20)),
        };
        AutomationProperties.SetName(button,label);return button;
    }
    void YouTubeArtistBannerImageChanged(object? sender,EventArgs e)
    {
        var image=YouTubeArtistBanner.Source;
        // A real landscape banner fills the hero. A square artist photo remains
        // intact over its softer background instead of losing the face to a crop.
        YouTubeArtistBanner.Stretch=image is {Width:>0,Height:>0}&&image.Width/image.Height<1.65?Stretch.Uniform:Stretch.UniformToFill;
        if(image!=null&&ReferenceEquals(sender,YouTubeArtistBanner))YouTubeArtistBanner.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(settings.MotionEnabled?240:0)));
    }
    void YouTubeArtistHeroSizeChanged(object sender,SizeChangedEventArgs e)=>UpdateYouTubeArtistHeroLayout();
    void UpdateYouTubeArtistHeroLayout()
    {
        if(YouTubeArtistName==null||YouTubeArtistIdentity==null)return;
        bool narrow=YouTubeArtistHero.ActualWidth<620;
        YouTubeArtistHero.Height=narrow?320:360;
        YouTubeArtistName.FontSize=narrow?32:46;
        YouTubeArtistIdentity.Margin=new Thickness(narrow?18:28,24,narrow?18:28,26);
    }
    void ReadMoreYouTubeArtist(object sender,RoutedEventArgs e)
    {
        bool expanded=YouTubeArtistDescription.Visibility==Visibility.Visible;
        YouTubeArtistDescription.MaxHeight=double.PositiveInfinity;
        YouTubeArtistDescription.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;
        YouTubeArtistReadMore.Content=expanded?"About":"Less";
    }
    async void ShowAllYouTubeArtistTracks(object sender,RoutedEventArgs e)
    {
        if(YouTubeArtistTopMore.Tag is YouTubeItem browse){await Guard(()=>ActivateYouTubeItem(browse));return;}
        youtubeArtistExpanded=!youtubeArtistExpanded;SyncYouTubeArtistPresentation();
    }
    async void PlayYouTubeArtist(object sender,RoutedEventArgs e)
    {
        var first=youtubeItems.FirstOrDefault(i=>i.Kind=="Track"&&!i.IsVideo);
        if(first==null)return;
        settings.Shuffle=false;SyncPlaybackModes();
        await Guard(()=>PlayYouTubeKey(first.Key));
    }
    void ShareYouTubeArtist(object sender,RoutedEventArgs e)
    {
        if(youtubeCollection?.Kind!="Artist")return;
        try{Clipboard.SetText("https://music.youtube.com"+youtubeCollection.Key);YouTubeStatus.Text="Artist link copied";YouTubeStatus.Visibility=Visibility.Visible;}
        catch(System.Runtime.InteropServices.ExternalException){YouTubeStatus.Text="The clipboard is busy. Try again.";YouTubeStatus.Visibility=Visibility.Visible;}
    }
    async void ShuffleYouTubeArtist(object sender,RoutedEventArgs e)
    {
        var first=youtubeItems.FirstOrDefault(i=>i.Kind=="Track"&&!i.IsVideo);
        if(first==null)return;
        settings.Shuffle=true;SyncPlaybackModes();
        await Guard(()=>PlayYouTubeKey(first.Key));
    }
    async void YouTubeArtistActionClick(object sender,RoutedEventArgs e)
    {
        string? label=sender is FrameworkElement {Tag:string actionLabel}?actionLabel:null;
        if(label==null||musicBrowser?.CoreWebView2==null)return;
        e.Handled=true;
        await Guard(async()=>
        {
            var core=musicBrowser.CoreWebView2;
            if(!YouTubeRouteMatches(core.Source,new Uri(youtubeDisplayedUrl).PathAndQuery))
            {
                if(await core.ExecuteScriptAsync(YouTubeBridge.Navigate(new Uri(youtubeDisplayedUrl).PathAndQuery))=="false")core.Navigate(youtubeDisplayedUrl);
                for(int i=0;i<40&&!YouTubeRouteMatches(core.Source,new Uri(youtubeDisplayedUrl).PathAndQuery);i++)await Task.Delay(150);
            }
            string script="(()=>{const label="+JsonSerializer.Serialize(label)+";const h=Array.from(document.querySelectorAll('ytmusic-immersive-header-renderer,ytmusic-visual-header-renderer,ytmusic-responsive-header-renderer')).find(e=>e.getClientRects().length);const b=Array.from(h?.querySelectorAll('button,yt-button-renderer,tp-yt-paper-button')||[]).find(e=>(e.textContent||e.getAttribute('aria-label')||'').replace(/\\s+/g,' ').trim()===label);if(!b)return false;b.click();return true;})()";
            bool done=await core.ExecuteScriptAsync(script)=="true";
            if(!done){YouTubeStatus.Visibility=Visibility.Visible;YouTubeStatus.Text="This artist action is currently unavailable.";return;}
            await Task.Delay(400);
            var state=JsonSerializer.Deserialize<YouTubeState>(await core.ExecuteScriptAsync(YouTubeBridge.Snapshot));
            if(state?.Page!=null){youtubeArtistPage=state.Page;SyncYouTubeArtistPresentation();}
            await ShowYouTubeMenu();
        });
    }
}
