using System.Windows;
using System.Windows.Media;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyArtistPresentation(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory()!=null,"Artist presentation checks require isolated storage");
        youtubeTimer.Stop();browserMode=true;
        YouTubeNativePanel.Visibility=YouTubeRail.Visibility=Visibility.Visible;
        SoundCloudRail.Visibility=NativePlayer.Visibility=SearchHeader.Visibility=LibraryPanel.Visibility=Visibility.Collapsed;
        youtubeQueueView=false;youtubeKeepCards=false;
        youtubeCollection=new("/channel/artist-visual-fixture","LONOWN","",null,"Artist");
        youtubeArtistPage=new("LONOWN","",null,"A local biography fixture. Artist information is shown only when the music service provides it.",[new("Subscribe"),new("Start radio")]);
        SetYouTubeItems(Enumerable.Range(0,8).Select(i=>new YouTubeItem("/watch?v=artist-fixture-"+i,
            new[]{"worry (ultra slowed)","AVANGARD (Slowed + Reverb)","starly (Slowed)","addiction (Slowed)"}[i%4],"LONOWN",null,"Track","Popular",Duration:"3:54")));
        UpdateYouTubeShelves("https://music.youtube.com/channel/artist-visual-fixture");
        Width=1200;Height=780;await Task.Delay(150);UpdateLayout();

        var image=new DrawingGroup();
        using(var draw=image.Open())
        {
            draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(32,36,43)),null,new Rect(0,0,1400,500));
            draw.DrawEllipse(new SolidColorBrush(Color.FromRgb(71,80,92)),null,new Point(970,180),310,310);
            draw.DrawEllipse(new SolidColorBrush(Color.FromRgb(42,48,57)),null,new Point(850,110),225,225);
        }
        image.Freeze();YouTubeArtistBanner.Source=new DrawingImage(image);
        await Task.Delay(300);UpdateLayout();
        CheckInteraction(YouTubeArtistBanner.Stretch==Stretch.UniformToFill,"Landscape artist banner no longer fills the hero");
        CheckInteraction(YouTubeArtistHero.IsVisible&&YouTubeArtistHero.ActualHeight==360&&YouTubeArtistName.FontSize==46,"Wide artist hero layout failed");
        CheckInteraction(YouTubeArtistListeners.Text.Length==0,"Artist page invented listening statistics");
        CheckInteraction(YouTubeArtistTopTracks.Items.Count==5&&YouTubeArtistTopMore.IsVisible,"Artist popular tracks are not initially compact");
        CheckInteraction(YouTubeArtistActions.Children.Count==2&&YouTubeArtistPlay.IsEnabled,"Artist play/follow/overflow actions are missing");
        Capture(Path.Combine(directory,"artist-design.png"));

        ShowAllYouTubeArtistTracks(this,new RoutedEventArgs());
        CheckInteraction(YouTubeArtistTopTracks.Items.Count==8,"Artist Show all did not reveal the loaded tracks");
        ShowAllYouTubeArtistTracks(this,new RoutedEventArgs());
        CheckInteraction(YouTubeArtistTopTracks.Items.Count==5,"Artist Show less did not restore the compact list");
        ReadMoreYouTubeArtist(this,new RoutedEventArgs());
        CheckInteraction(YouTubeArtistDescription.IsVisible,"Artist biography disclosure failed");
        ReadMoreYouTubeArtist(this,new RoutedEventArgs());

        Width=460;Height=680;await Task.Delay(150);UpdateLayout();
        CheckInteraction(YouTubeArtistHero.ActualHeight==320&&YouTubeArtistName.FontSize==32,"Narrow artist hero did not adapt");
        var square=new DrawingGroup();using(var draw=square.Open())draw.DrawRectangle(Brushes.DimGray,null,new Rect(0,0,400,400));
        square.Freeze();YouTubeArtistBanner.Source=new DrawingImage(square);
        await Task.Delay(300);UpdateLayout();
        CheckInteraction(YouTubeArtistBanner.Stretch==Stretch.Uniform,"Square artist artwork is cropped in the hero");
        Capture(Path.Combine(directory,"artist-design-narrow.png"));
        await File.WriteAllTextAsync(Path.Combine(directory,"artist-design-check.txt"),"PASS local artist fixture: wide/narrow banner, source aspect preservation, no invented listening statistics, actual follow/overflow actions, compact popular tracks, Show all/less and biography disclosure.");
        youtubeCollection=null;youtubeArtistPage=null;SyncYouTubeArtistPresentation();browserMode=false;
        Width=1200;Height=780;
    }
}
