using System.Windows;
using System.Windows.Input;
namespace DIPlayer;
public partial class MainWindow
{
    async Task VerifyLyricsInterface(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory()!=null,"Lyrics interface checks require isolated storage");
        youtubeTimer.Stop();youtubeLyricsTimer.Stop();browserMode=true;youtubeLyricsOpen=true;
        YouTubeNativePanel.Visibility=YouTubeRail.Visibility=YouTubeLyricsOverlay.Visibility=Visibility.Visible;
        SoundCloudRail.Visibility=NativePlayer.Visibility=SearchHeader.Visibility=LibraryPanel.Visibility=Visibility.Collapsed;
        YouTubeNowTitle.Text="Synchronized lyrics";YouTubeNowArtist.Text="Local timing check";
        youtubeState=new("https://music.youtube.com/","Synchronized lyrics","Local timing check",null,0,160,true,.5,false,0,false,[],VideoId:"lyrics-fixture");
        youtubeLyricsTrack=CurrentLyricsTrack();youtubeLyricsFollowing=true;
        RenderLyrics(Enumerable.Range(0,24).Select(i=>new YouTubeLyricsLine("A line of music, a moment in time "+(i+1),false,2+i*5)).ToArray(),"Synced · local timing fixture",null);
        UpdateLayout();await Task.Delay(150);
        foreach(var sample in new[]{(1d,-1),(2d,0),(6.9,0),(7d,1),(57d,11),(12d,2)})
        {
            youtubeState=youtubeState with{Position=sample.Item1};ObserveLyricsPlayback();ApplyLyricsHighlight();
            CheckInteraction(youtubeActiveLyric==sample.Item2,"Lyrics did not follow playback/seek to "+sample.Item1);
        }
        youtubeState=youtubeState with{Position=6.95,Paused=false};ObserveLyricsPlayback();await Task.Delay(120);ApplyLyricsHighlight();
        CheckInteraction(youtubeActiveLyric==1,"Lyrics did not interpolate playing position between snapshots");
        youtubeState=youtubeState with{Position=6.95,Paused=true};ObserveLyricsPlayback();await Task.Delay(120);ApplyLyricsHighlight();
        CheckInteraction(youtubeActiveLyric==0,"Paused lyrics continued advancing");
        YouTubeLyricsManualScroll(this,new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120));
        CheckInteraction(!youtubeLyricsFollowing&&YouTubeLyricsFollow.IsVisible,"Manual reading did not expose Follow lyrics");
        var scroll=LyricsScroll()!;scroll.ScrollToVerticalOffset(300);await Task.Delay(100);UpdateLayout();
        double manualOffset=scroll.VerticalOffset;
        youtubeState=youtubeState with{Position=87};ObserveLyricsPlayback();ApplyLyricsHighlight();await Task.Delay(100);UpdateLayout();
        CheckInteraction(Math.Abs(scroll.VerticalOffset-manualOffset)<1,"Following interrupted manual lyrics reading");
        FollowYouTubeLyrics(this,new RoutedEventArgs());
        for(int i=0;i<9;i++){await Task.Delay(50);ApplyLyricsHighlight();UpdateLayout();}
        CheckInteraction(youtubeLyricsFollowing&&!YouTubeLyricsFollow.IsVisible&&scroll.VerticalOffset>manualOffset+30,"Follow lyrics did not return to the playing line");
        youtubeState=youtubeState with{Position=2};ObserveLyricsPlayback();ApplyLyricsHighlight();
        for(int i=0;i<9;i++){await Task.Delay(50);ApplyLyricsHighlight();UpdateLayout();}
        CheckInteraction(youtubeActiveLyric==0&&scroll.VerticalOffset<5,"Backward seek did not return lyrics to the first line");
        youtubeState=youtubeState with{Position=37};ObserveLyricsPlayback();ApplyLyricsHighlight();
        for(int i=0;i<9;i++){await Task.Delay(50);ApplyLyricsHighlight();UpdateLayout();}
        Capture(Path.Combine(directory,"lyrics-synced.png"));
        Width=460;Height=680;await Task.Delay(150);UpdateLayout();
        for(int i=0;i<9;i++){await Task.Delay(50);ApplyLyricsHighlight();UpdateLayout();}
        var activeBlock=youtubeLyricBlocks[youtubeActiveLyric];
        double activeY=activeBlock.TranslatePoint(new Point(),scroll).Y;
        CheckInteraction(activeY>=0&&activeY+activeBlock.ActualHeight<=scroll.ViewportHeight,"Active lyrics disappeared after narrowing the window");
        Capture(Path.Combine(directory,"lyrics-synced-narrow.png"));
        youtubeState=youtubeState with{VideoId="next-track"};ObserveLyricsPlayback();
        CheckInteraction(youtubeLyricBlocks.Count==0&&youtubeTimedLyrics.Length==0&&YouTubeLyricsMessage.IsVisible,"Previous track lyrics survived a track change");
        browserMode=false;var previousTrack=current;
        current=new("lyrics-sc-fixture","SoundCloud fixture","Fixture artist",160000,null,"https://soundcloud.com/fixture/track","playable");
        var soundCloudState=EffectiveLyricsState();
        CheckInteraction(soundCloudState is {Title:"SoundCloud fixture",Artist:"Fixture artist",VideoId:"sc:lyrics-sc-fixture",Paused:true},"SoundCloud lyrics used stale YouTube metadata/playback");
        youtubeLyricsTrack=CurrentLyricsTrack();SyncLyricsMetadata();
        RenderLyrics([new("SoundCloud line at the beginning",false,0),new("A later line",false,10)],"Synced · local SoundCloud fixture",null);
        CheckInteraction(youtubeActiveLyric==0,"SoundCloud lyrics did not use the native audio position");
        await RefreshYouTubeLyrics(true);
        youtubeLyricsReading=true;
        ToggleYouTubeLyrics(this,new RoutedEventArgs());
        CheckInteraction(!youtubeLyricsOpen&&!youtubeLyricsTimer.IsEnabled&&youtubeLyricsCancel?.IsCancellationRequested==true,"Closing lyrics did not cancel its pending read");
        ToggleYouTubeLyrics(this,new RoutedEventArgs());
        CheckInteraction(youtubeLyricsOpen&&youtubeLyricsTimer.IsEnabled&&youtubeLyricsTrack.Length==0,"Rapid SoundCloud lyrics reopen lost its refresh timer");
        youtubeLyricsReading=false;
        await RefreshYouTubeLyrics(true);
        CheckInteraction(youtubeLyricsCancel?.IsCancellationRequested==false&&youtubeLyricsTrack==CurrentLyricsTrack(),"Reopened SoundCloud lyrics reused a canceled request");
        current=previousTrack;
        await File.WriteAllTextAsync(Path.Combine(directory,"lyrics-interface-check.txt"),"PASS local fixture: timestamp boundaries, playing interpolation, pause, forward/backward seek, manual reading and Follow, active line scrolling, track reset, wide/narrow layout, SoundCloud playback/metadata isolation and rapid reopen recovery. No remote lyrics or playback used.");
        StopYouTubeLyrics();browserMode=false;
    }
}
