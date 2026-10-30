using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace DIPlayer;
public partial class MainWindow
{
    bool youtubeLyricsOpen,youtubeLyricsReading,youtubeLyricsTimerReady,youtubeLyricsFollowing=true,youtubeLyricsScrollReady;
    string youtubeLyricsSignature="",youtubeLyricsTrack="",youtubeLyricsExternalTrack="";
    DateTime youtubeLyricsReadAt,youtubeLyricsExternalReadAt;
    readonly SyncedLyricsProvider syncedLyricsProvider=new();
    readonly DispatcherTimer youtubeLyricsTimer=new(){Interval=TimeSpan.FromMilliseconds(50)};
    readonly Stopwatch youtubeLyricsClock=Stopwatch.StartNew(),youtubeLyricsScrollClock=new();
    CancellationTokenSource? youtubeLyricsCancel;
    TimedLyric[] youtubeTimedLyrics=[],youtubeExternalLyrics=[];
    readonly List<TextBlock> youtubeLyricBlocks=[];
    int youtubeActiveLyric=-2,youtubeSiteActive=-1;
    double youtubeLyricsScrollFrom=double.NaN,youtubeLyricsScrollTarget;
    YouTubeState? EffectiveLyricsState()
    {
        if(browserMode)return youtubeState;
        if(current is not { } track)return null;
        return new(track.Permalink,track.Title,track.Artist,track.Artwork,player.Position,
            player.Duration>0?player.Duration:track.DurationMs/1000d,!player.Playing||player.Paused,player.Volume,
            settings.Shuffle,settings.RepeatMode,false,[],VideoId:"sc:"+track.Id);
    }
    string CurrentLyricsTrack()
    {
        var state=EffectiveLyricsState();
        return (state?.VideoId??"")+"|"+(state?.Title??"")+"|"+(state?.Artist??"")+"|"+Math.Round(state?.Duration??0);
    }
    void SyncLyricsMetadata()
    {
        var state=EffectiveLyricsState();
        if(FindName("LyricsBackdrop") is UI.Artwork backdrop&&backdrop.Url!=state?.Artwork)backdrop.Url=state?.Artwork;
        if(FindName("LyricsArtwork") is UI.Artwork artwork&&artwork.Url!=state?.Artwork)artwork.Url=state?.Artwork;
        if(FindName("LyricsTitle") is TextBlock title)title.Text=state?.Title??"Lyrics";
        if(FindName("LyricsArtist") is TextBlock artist)artist.Text=state?.Artist??"";
    }
    void ObserveLyricsPlayback()
    {
        youtubeLyricsClock.Restart();
        if(CurrentLyricsTrack()!=youtubeLyricsTrack&&youtubeLyricsTrack.Length>0)
        {
            SyncLyricsMetadata();ResetLyricsChoices();
            youtubeLyricsCancel?.Cancel();
            youtubeTimedLyrics=[];youtubeSiteActive=-1;youtubeActiveLyric=-2;
            youtubeLyricBlocks.Clear();YouTubeLyricsLines.Children.Clear();
            YouTubeLyricsSource.Text="";
            YouTubeLyricsMessage.Text="Loading lyrics…";YouTubeLyricsMessage.Visibility=Visibility.Visible;
        }
    }
    void StopYouTubeLyrics()
    {
        youtubeLyricsTimer.Stop();youtubeLyricsCancel?.Cancel();youtubeLyricsScrollFrom=double.NaN;youtubeLyricsTrack="";
        youtubeLyricsOpen=false;YouTubeLyricsOverlay.Visibility=Visibility.Collapsed;YouTubeLyricsButton.Foreground=LyricsButton.Foreground=Brushes.Gray;
        SyncTrackBackdrop();
    }
    async void ToggleYouTubeLyrics(object sender,RoutedEventArgs e)
    {
        youtubeLyricsOpen=!youtubeLyricsOpen;
        EqualizerOverlay.Visibility=Visibility.Collapsed;
        YouTubeLyricsOverlay.Visibility=youtubeLyricsOpen?Visibility.Visible:Visibility.Collapsed;
        YouTubeLyricsButton.Foreground=LyricsButton.Foreground=youtubeLyricsOpen?Brushes.White:Brushes.Gray;
        if(youtubeLyricsOpen)
        {
            CloseSearchSuggestions();
            youtubeLyricBlocks.Clear();YouTubeLyricsLines.Children.Clear();
            YouTubeLyricsMessage.Text=EffectiveLyricsState()==null?"Play a track to see its lyrics.":"Loading lyrics…";
            YouTubeLyricsMessage.Visibility=Visibility.Visible;
            SyncLyricsMetadata();SyncTrackScreenPlayback();SyncTrackBackdrop();
            // A previous canceled read can still be unwinding. Keep the SC timer
            // alive so its next tick starts this open request with a fresh token.
            if(youtubeLyricsTimerReady)youtubeLyricsTimer.Start();
            await Guard(()=>RefreshYouTubeLyrics(true));
        }
        else {youtubeLyricsTimer.Stop();youtubeLyricsCancel?.Cancel();youtubeLyricsScrollFrom=double.NaN;youtubeLyricsTrack="";SyncTrackBackdrop();}
    }
    ScrollViewer? LyricsScroll()
    {
        if(FindName("YouTubeLyricsScroll") is ScrollViewer named)return named;
        for(DependencyObject? node=YouTubeLyricsLines;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node is ScrollViewer scroll)return scroll;
        return null;
    }
    void SyncLyricsFollowButton()
    {
        if(FindName("YouTubeLyricsFollow") is Button button)
            button.Visibility=!youtubeLyricsFollowing&&(youtubeTimedLyrics.Length>0||youtubeSiteActive>=0)?Visibility.Visible:Visibility.Collapsed;
    }
    void PauseLyricsFollowing()
    {
        youtubeLyricsFollowing=false;youtubeLyricsScrollFrom=double.NaN;SyncLyricsFollowButton();
    }
    void YouTubeLyricsManualScroll(object sender,MouseWheelEventArgs e)=>PauseLyricsFollowing();
    void YouTubeLyricsPointerDown(object sender,MouseButtonEventArgs e)
    {
        for(DependencyObject? node=e.OriginalSource as DependencyObject;node!=null&&node!=sender;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is ScrollBar or Thumb){PauseLyricsFollowing();break;}
    }
    void YouTubeLyricsScrollKey(object sender,KeyEventArgs e)
    {
        if(e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space)PauseLyricsFollowing();
    }
    void FollowYouTubeLyrics(object sender,RoutedEventArgs e)
    {
        youtubeLyricsFollowing=true;SyncLyricsFollowButton();ScrollToLyric(youtubeActiveLyric);
    }
    void ScrollToLyric(int active)
    {
        if(!youtubeLyricsFollowing||active<0||active>=youtubeLyricBlocks.Count||youtubeLyricBlocks[active].Text.Length==0)return;
        var line=youtubeLyricBlocks[active];
        Dispatcher.BeginInvoke(()=>
        {
            if(!youtubeLyricsOpen||!youtubeLyricsFollowing||active!=youtubeActiveLyric||!line.IsVisible||LyricsScroll() is not { } scroll)return;
            double y=line.TranslatePoint(new Point(),scroll).Y+scroll.VerticalOffset;
            youtubeLyricsScrollTarget=Math.Clamp(y-(scroll.ViewportHeight-line.ActualHeight)*.4,0,scroll.ScrollableHeight);
            if (!settings.MotionEnabled) { scroll.ScrollToVerticalOffset(youtubeLyricsScrollTarget); youtubeLyricsScrollFrom=double.NaN; return; }
            youtubeLyricsScrollFrom=scroll.VerticalOffset;youtubeLyricsScrollClock.Restart();
        },DispatcherPriority.Loaded);
    }
    void AnimateLyricsScroll()
    {
        if(double.IsNaN(youtubeLyricsScrollFrom)||LyricsScroll() is not { } scroll)return;
        double progress=Math.Min(1,youtubeLyricsScrollClock.Elapsed.TotalSeconds/.32);
        scroll.ScrollToVerticalOffset(youtubeLyricsScrollFrom+(youtubeLyricsScrollTarget-youtubeLyricsScrollFrom)*(1-Math.Pow(1-progress,3)));
        if(progress>=1)youtubeLyricsScrollFrom=double.NaN;
    }
    void ApplyLyricsHighlight()
    {
        if(!youtubeLyricsOpen)return;
        if(!IsVisible||WindowState==WindowState.Minimized){youtubeLyricsTimer.Stop();SyncTrackBackdrop();return;}
        SyncTrackScreenPlayback();
        if(!browserMode&&CurrentLyricsTrack()!=youtubeLyricsTrack){ObserveLyricsPlayback();_ = Guard(()=>RefreshYouTubeLyrics());return;}
        var state=EffectiveLyricsState();
        double position=(state?.Position??0)+(browserMode&&state?.Paused==false?youtubeLyricsClock.Elapsed.TotalSeconds:0)+lyricsOffset;
        int active=youtubeTimedLyrics.Length>0?LyricsTimeline.ActiveIndex(youtubeTimedLyrics,position):youtubeSiteActive;
        if(state?.IsAd==true)active=-1;
        if(active!=youtubeActiveLyric)
        {
            youtubeActiveLyric=active;
            bool timed=youtubeTimedLyrics.Length>0||youtubeSiteActive>=0;
            for(int i=0;i<youtubeLyricBlocks.Count;i++)
            {
                bool current=i==active||active>=0&&youtubeTimedLyrics.Length>0&&youtubeTimedLyrics[i].Start==youtubeTimedLyrics[active].Start;
                youtubeLyricBlocks[i].Foreground=Brushes.White;
                youtubeLyricBlocks[i].BeginAnimation(OpacityProperty,new DoubleAnimation(!timed||current?1:.32,TimeSpan.FromMilliseconds(settings.MotionEnabled ? 180 : 0)));
            }
            ScrollToLyric(active);SyncLyricsFollowButton();
        }
        AnimateLyricsScroll();
    }
    async Task SeekToLyric(double start,string track)
    {
        if(track!=CurrentLyricsTrack())return;
        start=Math.Max(0,start-lyricsOffset);
        if(!browserMode)
        {
            await player.Seek(start);
            if(track!=CurrentLyricsTrack())return;
            youtubeLyricsFollowing=true;youtubeActiveLyric=-2;ApplyLyricsHighlight();return;
        }
        if(musicBrowser?.CoreWebView2==null||youtubeState?.IsAd==true)return;
        int generation=browserGeneration;
        bool sought=await musicBrowser.CoreWebView2.ExecuteScriptAsync("(()=>{const v=document.querySelector('video');if(!v||!Number.isFinite(v.duration))return false;v.currentTime=Math.min(v.duration,"+JsonSerializer.Serialize(start)+");return true;})()") == "true";
        if(!sought||generation!=browserGeneration||track!=CurrentLyricsTrack())return;
        youtubeState=youtubeState! with{Position=start};youtubeLyricsClock.Restart();
        youtubeLyricsFollowing=true;youtubeActiveLyric=-2;ApplyLyricsHighlight();
        await RefreshYouTubeNative();
    }
    void RenderLyrics(YouTubeLyricsLine[] lines,string source,string? message)
    {
        if(!youtubeLyricsScrollReady&&LyricsScroll() is { } scroll)
        {
            scroll.SizeChanged+=(_,_)=>ScrollToLyric(youtubeActiveLyric);
            youtubeLyricsScrollReady=true;
        }
        lines=lines.Where(l=>l.Text.Length<=2000).Take(500).Select(l=>l with{Start=l.Start is double start&&double.IsFinite(start)&&start>=0?start:null}).ToArray();
        bool timed=lines.Length>0&&lines.All(l=>l.Start.HasValue);
        if(timed)lines=lines.OrderBy(l=>l.Start).ToArray();
        youtubeSiteActive=Array.FindIndex(lines,l=>l.Active);
        string signature=string.Join('\n',lines.Select(l=>l.Start+"|"+l.Text));
        YouTubeLyricsSource.Text=source;
        YouTubeLyricsMessage.Visibility=lines.Length==0?Visibility.Visible:Visibility.Collapsed;
        YouTubeLyricsMessage.Text=message??"Lyrics are not available for this track.";
        if(signature!=youtubeLyricsSignature)
        {
            youtubeLyricsSignature=signature;youtubeActiveLyric=-2;youtubeLyricsScrollFrom=double.NaN;
            YouTubeLyricsLines.Children.Clear();youtubeLyricBlocks.Clear();
            youtubeTimedLyrics=timed?lines.Select(l=>new TimedLyric(l.Start!.Value,l.Text)).ToArray():[];
            string track=youtubeLyricsTrack;
            foreach(var line in lines)
            {
                var block=new TextBlock {Text=line.Text,FontSize=LyricsBody.ActualWidth<700?26:32,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,32),Foreground=Brushes.White};
                if(line.Start is double start)
                {
                    block.Cursor=Cursors.Hand;block.ToolTip="Play from this line";
                    block.MouseLeftButtonUp+=async(_,_)=>await Guard(()=>SeekToLyric(start,track));
                }
                youtubeLyricBlocks.Add(block);YouTubeLyricsLines.Children.Add(block);
            }
        }
        ApplyLyricsHighlight();
    }
    async Task RefreshYouTubeLyrics(bool force=false)
    {
        if(!youtubeLyricsOpen||youtubeLyricsReading||browserMode&&musicBrowser?.CoreWebView2==null)return;
        if(!force&&DateTime.UtcNow-youtubeLyricsReadAt<TimeSpan.FromSeconds(1))return;
        if(!youtubeLyricsTimerReady)
        {
            youtubeLyricsTimer.Tick+=(_,_)=>{ApplyLyricsHighlight();if(!browserMode)_ = Guard(()=>RefreshYouTubeLyrics());};
            IsVisibleChanged+=(_,_)=>{SyncTrackBackdrop();if(!IsVisible)youtubeLyricsTimer.Stop();else if(youtubeLyricsOpen){youtubeLyricsTimer.Start();_ = Guard(()=>RefreshYouTubeLyrics());}};
            youtubeLyricsTimerReady=true;
        }
        if(IsVisible)youtubeLyricsTimer.Start();youtubeLyricsReading=true;youtubeLyricsReadAt=DateTime.UtcNow;
        int generation=browserGeneration;
        try
        {
            var core=musicBrowser?.CoreWebView2;
            string track=CurrentLyricsTrack();
            SyncLyricsMetadata();
            if(force||track!=youtubeLyricsTrack)
            {
                youtubeLyricsCancel?.Cancel();youtubeLyricsCancel?.Dispose();youtubeLyricsCancel=new();
                youtubeLyricsTrack=track;youtubeLyricsSignature="unset";youtubeExternalLyrics=[];youtubeTimedLyrics=[];
                youtubeLyricsExternalTrack="";youtubeLyricsFollowing=true;youtubeLyricsScrollFrom=double.NaN;
                ResetLyricsChoices();
                YouTubeLyricsLines.Children.Clear();youtubeLyricBlocks.Clear();SyncLyricsFollowButton();LyricsScroll()?.ScrollToTop();
                YouTubeLyricsSource.Text="";YouTubeLyricsMessage.Text="Loading lyrics…";YouTubeLyricsMessage.Visibility=Visibility.Visible;
                if(browserMode&&core!=null)await core.ExecuteScriptAsync(YouTubeLyricsBridge.Open);
            }
            string json=browserMode&&core!=null?await core.ExecuteScriptAsync(YouTubeLyricsBridge.Read):"{\"Lines\":[]}";
            if(!youtubeLyricsOpen||generation!=browserGeneration||track!=CurrentLyricsTrack()||json.Length>128*1024)return;
            var state=JsonSerializer.Deserialize<YouTubeLyricsState>(json);
            if(state==null)return;
            bool nativeTiming=state.Lines.Length>0&&(state.Lines.All(l=>l.Start.HasValue)||state.Lines.Any(l=>l.Active));
            int nativeIndex=lyricsChoices.FindIndex(choice=>choice.Id=="youtube");
            if(state.Lines.Length>0)
            {
                var native=new LyricsChoice("youtube","YouTube Music",state.Lines,nativeTiming?"Synced · YouTube Music":"YouTube Music · no timing available");
                if(nativeIndex>=0)lyricsChoices[nativeIndex]=native;else lyricsChoices.Insert(0,native);
            }
            ShowSelectedLyrics(EffectiveLyricsState()==null?"Play a track to see its lyrics.":state.Message);
            if(DiagnosticLibraryDirectory()==null&&(youtubeLyricsExternalTrack!=track||youtubeExternalLyrics.Length==0&&DateTime.UtcNow-youtubeLyricsExternalReadAt>TimeSpan.FromMinutes(1))&&EffectiveLyricsState() is {Duration:>0,IsAd:false} current)
            {
                youtubeLyricsExternalTrack=track;youtubeLyricsExternalReadAt=DateTime.UtcNow;
                var candidates=await syncedLyricsProvider.GetCandidates(current.Title,current.Artist,current.Duration,youtubeLyricsCancel?.Token??CancellationToken.None);
                if(!youtubeLyricsOpen||generation!=browserGeneration||track!=CurrentLyricsTrack())return;
                youtubeExternalLyrics=candidates.FirstOrDefault()?.Lines??[];
                lyricsChoices.RemoveAll(choice=>choice.Id!="youtube");
                foreach(var candidate in candidates)
                    lyricsChoices.Add(new(candidate.Id,candidate.Provider+(candidate.Album.Length>0?" · "+candidate.Album:""),candidate.Lines.Select(line=>new YouTubeLyricsLine(line.Text,false,line.Start)).ToArray(),"Synced · "+candidate.Provider+" · "+candidate.Title+" — "+candidate.Artist));
                ShowSelectedLyrics(state.Message);
            }
        }
        finally{youtubeLyricsReading=false;}
    }
}
