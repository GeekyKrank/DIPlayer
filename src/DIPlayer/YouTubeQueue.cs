using System.Text.Json;
using System.Windows;
using DIPlayer.Core;
using DIPlayer.Queue;

namespace DIPlayer;

public partial class MainWindow
{
    readonly PlaybackQueue youtubeQueue = new();
    bool youtubeQueueView, youtubeAdvancing, youtubeQueueCleared;
    void SeedYouTubeQueue(string key)
    {
        if (youtubeQueueView && youtubeQueue.Tracks.Any(t => t.Id == key))
        {
            youtubeQueue.Index = youtubeQueue.Tracks.FindIndex(t => t.Id == key);
            return;
        }
        var tracks = youtubeItems.Where(item => item.Kind == "Track").Select(item => new Track(item.Key, item.Title, item.Subtitle, 0, item.Artwork, "https://music.youtube.com" + item.Key, "playable")).ToList();
        int index = tracks.FindIndex(t => t.Id == key);
        if (index < 0) return;
        youtubeQueueCleared = false;
        youtubeQueue.Tracks.Clear(); youtubeQueue.Tracks.AddRange(tracks); youtubeQueue.Index = index; youtubeQueue.ResetNavigation();
    }
    async Task ConfigureYouTubeQueue()
    {
        if (musicBrowser?.CoreWebView2 == null) return;
        await musicBrowser.CoreWebView2.ExecuteScriptAsync("window.__diplayerNativeQueue=" + (youtubeQueue.Tracks.Count > 0 || youtubeQueueCleared ? "true" : "false") + ";window.__diplayerRepeatOne=" + (settings.RepeatMode == 2 && !youtubeQueueCleared ? "true" : "false") + ";(()=>{const v=document.querySelector('video');if(v)v.loop=" + (settings.RepeatMode == 2 && !youtubeQueueCleared ? "true" : "false") + ";})()");
    }
    async Task ReleaseYouTubeQueueOwnership()
    {
        try
        {
            if (musicBrowser?.CoreWebView2 != null)
                await musicBrowser.CoreWebView2.ExecuteScriptAsync("window.__diplayerNativeQueue=false;window.__diplayerRepeatOne=false;(()=>{const v=document.querySelector('video');if(v)v.loop=false;})()");
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
    async Task AdvanceYouTube(int direction)
    {
        if (youtubeAdvancing || youtubeQueueCleared || musicBrowser?.CoreWebView2 == null) return;
        youtubeAdvancing = true;
        try
        {
            if (youtubeWebsiteVisible || youtubeQueue.Tracks.Count == 0)
            {
                await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Command(direction > 0 ? "next" : "previous"));
                return;
            }
            var nextTrack = youtubeQueue.Navigate(direction, settings.RepeatMode == 1, settings.Shuffle);
            if (nextTrack == null) { YouTubeStatus.Text = direction > 0 ? "End of queue" : "Start of queue"; return; }
            await NavigateYouTube(nextTrack.Id, YouTubeHeading.Text, false);
        }
        finally { youtubeAdvancing = false; }
    }
    async Task RunYouTubeControl(string action)
    {
        if (musicBrowser?.CoreWebView2 == null) return;
        if (action == "next" || action == "previous") { await AdvanceYouTube(action == "next" ? 1 : -1); return; }
        if (action == "shuffle") { settings.Shuffle = !settings.Shuffle; youtubeQueue.ResetNavigation(); SyncPlaybackModes(); await RefreshYouTubeNative(); return; }
        if (action == "repeat") { settings.RepeatMode = (settings.RepeatMode + 1) % 3; SyncPlaybackModes(); await ConfigureYouTubeQueue(); await RefreshYouTubeNative(); return; }
        string result = await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Command(action));
        await RefreshYouTubeNative();
        if (result == "false")
        {
            YouTubeStatus.Text = "Open Website to use this control.";
            if (action == "like") YouTubeStatus.Text = "Sign in to your YouTube account to like this track.";
        }
    }
    void DisplayYouTubeQueue()
    {
        RememberYouTubeView(CaptureYouTubeView());
        youtubeQueueView = true; YouTubeHeading.Text = "YouTube Music queue";
        SyncYouTubeNavigation();
        youtubeCollection = null;
        YouTubeCollectionHeader.Visibility = Visibility.Collapsed;
        YouTubeCardsScroll.Visibility = Visibility.Visible;
        YouTubeShelvesScroll.Visibility = Visibility.Collapsed;
        youtubeItems.Clear();
        foreach (var track in youtubeQueue.Tracks) youtubeItems.Add(new(track.Id, track.Title, track.Artist, track.Artwork, "Track"));
        YouTubeEmpty.Visibility = youtubeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        YouTubeStatus.Text = $"{youtubeItems.Count} tracks · YouTube Music queue";
    }
    async void ClearYouTubeQueue(object sender, RoutedEventArgs e)
    {
        await Guard(async () =>
        {
            youtubeQueue.Tracks.Clear(); youtubeQueue.Index = -1; youtubeQueue.ResetNavigation();
            youtubeQueueCleared = true;
            await ConfigureYouTubeQueue();
            if (youtubeQueueView)
            {
                youtubeItems.Clear(); YouTubeEmpty.Visibility = Visibility.Visible;
                YouTubeStatus.Text = "Queue cleared";
            }
            SyncYouTubeNavigation(); SyncMiniPlayer();
        });
    }
    const string YouTubeEndedBridge = """
        (()=>{
          if(location.hostname!=='music.youtube.com')return;
          document.addEventListener('ended',e=>{
            if(e.target.tagName!=='VIDEO'||!window.__diplayerNativeQueue||document.querySelector('.ad-showing,.ad-interrupting'))return;
            e.stopImmediatePropagation();
            window.chrome.webview.postMessage('diplayer-youtube-ended');
          },true);
        })();
        """;
}
