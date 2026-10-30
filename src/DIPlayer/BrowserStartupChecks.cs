using System.Text.Json;
using System.Windows;
namespace DIPlayer;
public partial class MainWindow
{
    async Task VerifyExistingYouTubeProfile(string directory)
    {
        await ApplyBrowserMode(true);
        CheckInteraction(musicBrowser?.CoreWebView2!=null&&YouTubeFailure.Visibility!=Visibility.Visible,"Existing browser profile failed at startup; see browser-startup.log");
        for(int i=0;i<100;i++)
        {
            await Task.Delay(300);await RefreshYouTubeNative();
            if(youtubeItems.Count>0&&youtubeState?.Url.StartsWith("https://music.youtube.com/")==true)
            {
                await File.WriteAllTextAsync(Path.Combine(directory,"existing-profile-check.json"),JsonSerializer.Serialize(new {Ready=true,SignedIn=youtubeState.SignedIn,Items=youtubeItems.Count,Filtering=browserBlockerDiagnostics}));
                await ApplyBrowserMode(false);return;
            }
        }
        throw new Exception("Existing browser profile did not return YouTube Music content");
    }
}
