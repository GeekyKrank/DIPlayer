using System.Windows;
namespace DIPlayer;
public partial class MainWindow
{
    void ToggleYouTubeProfile(object sender, RoutedEventArgs e)
    {
        if (youtubeState?.SignedIn != true) { SignInYouTubeAccount(sender, e); return; }
        YouTubeProfileName.Text = string.IsNullOrWhiteSpace(youtubeState.AccountName) ? "Your Google account" : youtubeState.AccountName;
        YouTubeProfilePopup.IsOpen = !YouTubeProfilePopup.IsOpen;
    }
    void ManageYouTubeAccount(object sender, RoutedEventArgs e)
    {
        YouTubeProfilePopup.IsOpen = false;
        OpenYouTubeWebsite(sender, e);
    }
}
