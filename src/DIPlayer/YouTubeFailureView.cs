using System.Windows;
namespace DIPlayer;

public partial class MainWindow
{
    bool youtubeRetrying;
    void ShowYouTubeFailure(string message)
    {
        if (!browserMode || quitting) return;
        youtubeTimer.Stop();
        YouTubeTransport.IsEnabled = YouTubeVolume.IsEnabled = YouTubePosition.IsEnabled = false;
        YouTubePlay.Content = "\uE768";
        youtubeWebsiteVisible = false;
        BrowserPanel.Visibility = Visibility.Collapsed;
        BrowserToolbar.Visibility = BrowserNotice.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        YouTubeNativePanel.Visibility = Visibility.Visible;
        YouTubeCards.ItemsSource = youtubeItems;
        YouTubeFailureMessage.Text = message;
        YouTubeFailure.Visibility = Visibility.Visible;
        YouTubeEmpty.Visibility = Visibility.Collapsed;
        SyncYouTubeHeader(); SyncYouTubeNavigation();
    }

    async void RetryYouTubeStartup(object sender, RoutedEventArgs e)
    {
        if (youtubeRetrying || !browserMode) return;
        youtubeRetrying = true;
        try
        {
            await Guard(async () =>
            {
                DisposeMusicBrowser();
                await ApplyBrowserMode(true);
            });
        }
        finally { youtubeRetrying = false; }
    }
}
