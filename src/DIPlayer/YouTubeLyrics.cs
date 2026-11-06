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
    YouTubeLyricsLine[] youtubeRenderedLyrics=[];
    readonly List<TextBlock> youtubeLyricBlocks=[];
    int youtubeActiveLyric=-2,youtubeSiteActive=-1;
    // NaN while the lyrics glide is idle; otherwise the offset where the current glide began.
    double youtubeLyricsScrollFrom=double.NaN,youtubeLyricsScrollTarget,youtubeLyricsScrollPosition,youtubeLyricsScrollVelocity;
    bool youtubeLyricsScrollHooked;
    TimeSpan youtubeLyricsScrollLast;
    bool[] youtubeLyricLit=[];
    const double LyricDimOpacity=.36,LyricHoverOpacity=.66;
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
        if (browserMode && clipOriginalLyricsTrack is { } original && (clipChangingMusicVariant || clipMode && state?.VideoId == null ||
            state?.VideoId is { Length: > 0 } id && (id == clipOriginalMedia || id == clipNativeMedia)))
            return original;
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
        if (youtubeState?.IsAd == true) return;
        if(CurrentLyricsTrack()!=youtubeLyricsTrack&&youtubeLyricsTrack.Length>0)
        {
            SyncLyricsMetadata();ResetLyricsChoices();
            youtubeLyricsCancel?.Cancel();
            youtubeTimedLyrics=[];youtubeSiteActive=-1;youtubeActiveLyric=-2;StopLyricsScroll();
            youtubeRenderedLyrics=[];youtubeLyricsSignature="unset";
            clipOriginalLyricsTrack=clipOriginalMedia=clipNativeMedia=null;
            youtubeLyricBlocks.Clear();YouTubeLyricsLines.Children.Clear();youtubeLyricLit=[];
            if(clipMode)_=Guard(UpdateClipLyrics);
            if(!openingClip&&!clipMode)SetClipButtons(ClipButtonLabel);
            YouTubeLyricsSource.Text="";
            YouTubeLyricsMessage.Text="Loading lyrics…";YouTubeLyricsMessage.Visibility=Visibility.Visible;
        }
    }
    void StopYouTubeLyrics()
    {
        CloseTrackClip();
        youtubeLyricsTimer.Stop();youtubeLyricsCancel?.Cancel();StopLyricsScroll();youtubeLyricsTrack="";
        // Navigation needs the destination immediately, so this path never fades.
        youtubeLyricsOpen=false;UI.Motion.Reset(YouTubeLyricsOverlay);YouTubeLyricsOverlay.Visibility=Visibility.Collapsed;YouTubeLyricsButton.Foreground=LyricsButton.Foreground=Brushes.Gray;
        SyncTrackBackdrop();
    }
    async void ToggleYouTubeLyrics(object sender,RoutedEventArgs e)
    {
        youtubeLyricsOpen=!youtubeLyricsOpen;
        EqualizerOverlay.Visibility=Visibility.Collapsed;
        YouTubeLyricsButton.Foreground=LyricsButton.Foreground=youtubeLyricsOpen?Brushes.White:Brushes.Gray;
        if(youtubeLyricsOpen)
        {
            // Reopening during the closing fade turns it back into a fade-in.
            UI.Motion.Restore(YouTubeLyricsOverlay);
            YouTubeLyricsOverlay.Visibility=Visibility.Visible;
            CloseSearchSuggestions();
            if(!openingClip&&!clipMode)SetClipButtons(ClipButtonLabel);
            youtubeLyricBlocks.Clear();YouTubeLyricsLines.Children.Clear();youtubeLyricLit=[];StopLyricsScroll();
            YouTubeLyricsMessage.Text=EffectiveLyricsState()==null?"Play a track to see its lyrics.":"Loading lyrics…";
            YouTubeLyricsMessage.Visibility=Visibility.Visible;
            SyncLyricsMetadata();SyncTrackScreenPlayback();SyncTrackBackdrop();
            // A previous canceled read can still be unwinding. Keep the SC timer
            // alive so its next tick starts this open request with a fresh token.
            if(youtubeLyricsTimerReady)youtubeLyricsTimer.Start();
            await Guard(()=>RefreshYouTubeLyrics(true));
        }
        else
        {
            CloseTrackClip();
            youtubeLyricsTimer.Stop();youtubeLyricsCancel?.Cancel();StopLyricsScroll();youtubeLyricsTrack="";
            // The screen fades out; the cover keeps drifting until it is fully hidden.
            UI.Motion.Conceal(YouTubeLyricsOverlay,SyncTrackBackdrop);
        }
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
        youtubeLyricsFollowing=false;StopLyricsScroll();SyncLyricsFollowButton();
    }
    void YouTubeLyricsManualScroll(object sender,MouseWheelEventArgs e)
    {
        PauseLyricsFollowing();
        if(e.Handled||Keyboard.Modifiers!=ModifierKeys.None||sender is not ScrollViewer scroll||!UI.SmoothScroll.Enabled(scroll))return;
        e.Handled=true;
        UI.SmoothScroll.VerticalBy(scroll,-e.Delta/120.0*UI.SmoothScroll.WheelDistance(scroll));
    }
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
        youtubeLyricsFollowing=true;SyncLyricsFollowButton();UI.SmoothScroll.Stop(LyricsScroll());ScrollToLyric(youtubeActiveLyric);
    }
    void UpdateLyricsPadding()
    {
        if (LyricsScroll() is not { } scroll || scroll.ViewportHeight <= 0) return;
        double padding = scroll.ViewportHeight * .5;
        YouTubeLyricsLines.Margin = new Thickness(0, padding * 1.55, 0, padding);
    }

    bool LyricsMotion=>settings.MotionEnabled&&!clipMode&&WindowState!=WindowState.Minimized;
    void ScrollToLyric(int active,bool instant=false)
    {
        if(!youtubeLyricsFollowing||active<0||active>=youtubeLyricBlocks.Count||youtubeLyricBlocks[active].Text.Length==0)return;
        var line=youtubeLyricBlocks[active];
        Dispatcher.BeginInvoke(()=>
        {
            if(!youtubeLyricsOpen||!youtubeLyricsFollowing||active!=youtubeActiveLyric||!line.IsVisible||LyricsScroll() is not { } scroll)return;
            UI.SmoothScroll.Stop(scroll);
            double center=line.TranslatePoint(new Point(0,line.ActualHeight*.5),scroll).Y+scroll.VerticalOffset;
            double target=Math.Clamp(center-scroll.ViewportHeight*.42,0,Math.Max(0,scroll.ScrollableHeight));
            if(instant||!LyricsMotion){StopLyricsScroll();scroll.ScrollToVerticalOffset(target);return;}
            double start=youtubeLyricsScrollHooked?youtubeLyricsScrollPosition:scroll.VerticalOffset;
            // A seek across the song would otherwise blur dozens of lines past the eye:
            // jump most of the way and glide only the last screen.
            double reach=2.5*Math.Max(240,scroll.ViewportHeight*.6);
            if(Math.Abs(target-start)>reach)
            {
                start=target-Math.Sign(target-start)*reach*.4;
                youtubeLyricsScrollVelocity=0;scroll.ScrollToVerticalOffset(start);
            }
            StartLyricsScroll(start,target);
        },DispatcherPriority.Loaded);
    }
    void StartLyricsScroll(double start,double target)
    {
        if(!youtubeLyricsScrollHooked)
        {
            youtubeLyricsScrollVelocity=0;youtubeLyricsScrollLast=TimeSpan.Zero;youtubeLyricsScrollClock.Restart();
            CompositionTarget.Rendering+=LyricsScrollFrame;youtubeLyricsScrollHooked=true;
        }
        // Retargeting keeps the current velocity, so consecutive lines flow into each other.
        youtubeLyricsScrollPosition=start;youtubeLyricsScrollFrom=start;youtubeLyricsScrollTarget=target;
    }
    void StopLyricsScroll()
    {
        youtubeLyricsScrollFrom=double.NaN;youtubeLyricsScrollVelocity=0;
        if(!youtubeLyricsScrollHooked)return;
        CompositionTarget.Rendering-=LyricsScrollFrame;youtubeLyricsScrollHooked=false;
    }
    // Runs once per rendered frame only while a glide is active (vsync-smooth instead of the 20 Hz lyrics timer).
    void LyricsScrollFrame(object? sender,EventArgs e)
    {
        if(!youtubeLyricsScrollHooked||double.IsNaN(youtubeLyricsScrollFrom))return;
        if(!youtubeLyricsOpen||!youtubeLyricsFollowing||LyricsScroll() is not { IsVisible: true } scroll){StopLyricsScroll();return;}
        TimeSpan now=e is RenderingEventArgs rendering?rendering.RenderingTime:youtubeLyricsScrollClock.Elapsed;
        double dt=youtubeLyricsScrollLast==TimeSpan.Zero?1/60d:(now-youtubeLyricsScrollLast).TotalSeconds;
        if(dt<=0)return; // Rendering can be raised more than once for one frame.
        youtubeLyricsScrollLast=now;dt=Math.Min(dt,.05);
        // Exact critically damped spring: no overshoot, continuous velocity, frame-rate independent.
        const double stiffness=10;
        double target=Math.Clamp(youtubeLyricsScrollTarget,0,Math.Max(0,scroll.ScrollableHeight));
        double x=youtubeLyricsScrollPosition-target,v=youtubeLyricsScrollVelocity,k=v+stiffness*x,decay=Math.Exp(-stiffness*dt);
        x=(x+k*dt)*decay;v=(v-stiffness*k*dt)*decay;
        bool settled=Math.Abs(x)<.4&&Math.Abs(v)<8;
        youtubeLyricsScrollPosition=settled?target:target+x;youtubeLyricsScrollVelocity=settled?0:v;
        scroll.ScrollToVerticalOffset(youtubeLyricsScrollPosition);
        if(settled)StopLyricsScroll();
    }
    bool LyricLit(int index,int active,bool timed)=>!timed||index==active||active>=0&&youtubeTimedLyrics.Length>0&&
        index<youtubeTimedLyrics.Length&&active<youtubeTimedLyrics.Length&&youtubeTimedLyrics[index].Start==youtubeTimedLyrics[active].Start;
    static void StyleLyricLine(TextBlock block,bool lit,bool animate,bool hover=false)
    {
        double opacity=lit?1:hover?LyricHoverOpacity:LyricDimOpacity;
        if(!animate)
        {
            block.BeginAnimation(OpacityProperty,null);block.Opacity=opacity;
            return;
        }
        // Fade the emphasis while keeping the glyphs at their hinted size.
        // No From value: an interrupted change continues from where it is instead of popping.
        var duration=TimeSpan.FromMilliseconds(hover?160:lit?420:560);
        block.BeginAnimation(OpacityProperty,new DoubleAnimation(opacity,duration){EasingFunction=UI.Motion.Gentle});
    }
    void UpdateTrackLyricTypography(double width)
    {
        bool compact = width < 700;
        double size = compact ? 24 : width >= 1388 ? 36 : 32;
        var spacing = new Thickness(0, 0, 0, compact ? 20 : 30);
        foreach (var line in youtubeLyricBlocks)
        {
            line.FontSize = size;
            line.LineHeight = size * 1.3;
            line.Margin = spacing;
        }
    }
    void ApplyLyricsHighlight()
    {
        if(!youtubeLyricsOpen)return;
        if(!IsVisible||WindowState==WindowState.Minimized){youtubeLyricsTimer.Stop();SyncTrackBackdrop();return;}
        SyncTrackScreenPlayback();
        if(!browserMode&&CurrentLyricsTrack()!=youtubeLyricsTrack){ObserveLyricsPlayback();_ = Guard(()=>RefreshYouTubeLyrics());return;}
        var state=EffectiveLyricsState();
        double position=LyricsPlaybackPosition(state)+lyricsOffset;
        int active=youtubeTimedLyrics.Length>0?LyricsTimeline.ActiveIndex(youtubeTimedLyrics,position):youtubeSiteActive;
        if(state?.IsAd==true)active=-1;
        if(active!=youtubeActiveLyric)
        {
            youtubeActiveLyric=active;
            bool timed=youtubeTimedLyrics.Length>0||youtubeSiteActive>=0,animate=LyricsMotion&&YouTubeLyricsOverlay.IsVisible;
            bool restyle=youtubeLyricLit.Length!=youtubeLyricBlocks.Count;
            if(restyle)youtubeLyricLit=new bool[youtubeLyricBlocks.Count];
            // Only lines whose state changed are touched; usually two per lyric change.
            for(int i=0;i<youtubeLyricBlocks.Count;i++)
            {
                bool lit=LyricLit(i,active,timed);
                if(!restyle&&lit==youtubeLyricLit[i])continue;
                youtubeLyricLit[i]=lit;
                var block=youtubeLyricBlocks[i];
                StyleLyricLine(block,lit,animate,!lit&&block.IsMouseOver&&block.Cursor==Cursors.Hand);
            }
            ScrollToLyric(active);SyncLyricsFollowButton();
            if (clipMode) _ = Guard(UpdateClipLyrics);
        }
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
        SeekProgressFrame(start);
        youtubeLyricsFollowing=true;youtubeActiveLyric=-2;ApplyLyricsHighlight();
        await RefreshYouTubeNative();
    }
    void RenderLyrics(YouTubeLyricsLine[] lines,string source,string? message)
    {
        if(!youtubeLyricsScrollReady&&LyricsScroll() is { } scroll)
        {
            // Resizing re-centres instantly; gliding while the window is dragged feels laggy.
            scroll.SizeChanged+=(_,_)=>{UpdateLyricsPadding();ScrollToLyric(youtubeActiveLyric,true);};
            youtubeLyricsScrollReady=true;
        }
        UpdateLyricsPadding();
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
            bool replacing=youtubeLyricBlocks.Count>0;
            youtubeLyricsSignature=signature;youtubeActiveLyric=-2;StopLyricsScroll();
            youtubeRenderedLyrics=lines;
            YouTubeLyricsLines.Children.Clear();youtubeLyricBlocks.Clear();
            youtubeTimedLyrics=timed?lines.Select(l=>new TimedLyric(l.Start!.Value,l.Text)).ToArray():[];
            string track=youtubeLyricsTrack;
            // Lines start in their resting state so the first highlight only animates the active line.
            bool dimmed=timed||youtubeSiteActive>=0;
            youtubeLyricLit=new bool[lines.Length];
            var lineStyle=(Style)FindResource("TrackLyricLineStyle");
            for(int i=0;i<lines.Length;i++)
            {
                var line=lines[i];
                var block=new TextBlock {Text=line.Text,Style=lineStyle};
                youtubeLyricLit[i]=!dimmed;StyleLyricLine(block,!dimmed,false);
                if(line.Start is double start)
                {
                    int index=i;
                    block.Cursor=Cursors.Hand;block.ToolTip="Play from this line";
                    block.MouseLeftButtonUp+=async(_,_)=>await Guard(()=>SeekToLyric(start,track));
                    block.MouseEnter+=(_,_)=>{if(index<youtubeLyricLit.Length&&!youtubeLyricLit[index])StyleLyricLine(block,false,LyricsMotion,true);};
                    block.MouseLeave+=(_,_)=>{if(index<youtubeLyricLit.Length&&!youtubeLyricLit[index])StyleLyricLine(block,false,LyricsMotion);};
                }
                youtubeLyricBlocks.Add(block);YouTubeLyricsLines.Children.Add(block);
            }
            UpdateTrackLyricTypography(LyricsBody.ActualWidth);
            // New lyrics (first load, other version or next track) fade in instead of popping.
            if(lines.Length>0&&LyricsMotion&&YouTubeLyricsLines.IsVisible)
                YouTubeLyricsLines.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(replacing?220:320)){EasingFunction=UI.Motion.Gentle,FillBehavior=FillBehavior.Stop});
        }
        ApplyLyricsHighlight();
        if(clipMode)_=Guard(UpdateClipLyrics);
    }
    async Task RefreshYouTubeLyrics(bool force=false)
    {
        if(!youtubeLyricsOpen||youtubeLyricsReading||clipChangingMusicVariant||browserMode&&(musicBrowser?.CoreWebView2==null||youtubeState?.IsAd==true))return;
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
                youtubeLyricsTrack=track;youtubeLyricsSignature="unset";youtubeExternalLyrics=[];youtubeTimedLyrics=[];youtubeRenderedLyrics=[];
                youtubeLyricsExternalTrack="";youtubeLyricsFollowing=true;StopLyricsScroll();
                ResetLyricsChoices();
                YouTubeLyricsLines.Children.Clear();youtubeLyricBlocks.Clear();youtubeLyricLit=[];SyncLyricsFollowButton();
                if(LyricsScroll() is { } top){UI.SmoothScroll.Stop(top);top.ScrollToTop();}
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
                bool musicVideo=SelectedClipId(current)!=null;
                var candidates=await syncedLyricsProvider.GetCandidates(current.Title,current.Artist,current.Duration,
                    youtubeLyricsCancel?.Token??CancellationToken.None,musicVideo:musicVideo);
                if(!youtubeLyricsOpen||generation!=browserGeneration||track!=CurrentLyricsTrack())return;
                youtubeExternalLyrics=candidates.FirstOrDefault()?.Lines??[];
                lyricsChoices.RemoveAll(choice=>choice.Id!="youtube");
                foreach(var candidate in candidates)
                {
                    double difference=Math.Abs(candidate.Duration-current.Duration);
                    bool timed=!musicVideo||difference<=8;
                    string timing=timed ? (difference>2.1?"Song timing · ":"Synced · ") : "Different video cut · ";
                    lyricsChoices.Add(new(candidate.Id,candidate.Provider+(candidate.Album.Length>0?" · "+candidate.Album:""),
                        candidate.Lines.Select(line=>new YouTubeLyricsLine(line.Text,false,timed?line.Start:null)).ToArray(),
                        timing+candidate.Provider+" · "+candidate.Title+" — "+candidate.Artist));
                }
                ShowSelectedLyrics(state.Message);
            }
        }
        finally{youtubeLyricsReading=false;}
    }
}
