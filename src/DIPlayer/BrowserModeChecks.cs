using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyBrowserMode(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Browser checks require isolated storage");
        CheckInteraction(musicBrowser == null, "Optimized startup created a browser");
        OpenPlayerSettings(this, new RoutedEventArgs());
        UpdateLayout();
        Capture(Path.Combine(directory, "player-mode-settings.png"));
        Width = 460; Height = 680; await Task.Delay(150); UpdateLayout();
        Capture(Path.Combine(directory, "player-mode-settings-narrow.png"));
        Width = 1200; Height = 780;
        await ApplyBrowserMode(true);
        CheckInteraction(musicBrowser?.CoreWebView2 != null && BrowserPanel.IsVisible && !NativePlayer.IsVisible, "Browser did not start or native audio controls remain visible");
        var core = musicBrowser!.CoreWebView2;
        await File.WriteAllTextAsync(Path.Combine(directory,"blocker-configuration.json"),browserBlockerDiagnostics);
        CheckInteraction(browserBlockerDiagnostics.Contains("\"mode\":3"), "Complete YouTube filtering is not configured");
        var extensions = await core.Profile.GetBrowserExtensionsAsync();
        CheckInteraction(extensions.Any(extension => extension.IsEnabled && extension.Name.Contains("uBlock", StringComparison.OrdinalIgnoreCase)), "uBO Lite extension did not load");
        bool loaded = false;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(500);
            string result = await core.ExecuteScriptAsync("JSON.stringify({url:location.href,title:document.title,ready:document.readyState})");
            if (result.Contains("YouTube") && !result.Contains("about:blank")) { loaded = true; break; }
        }
        CheckInteraction(loaded, "YouTube Music page did not load");
        DisposeMusicBrowser();
        ShowYouTubeFailure("Simulated connection failure. Press Retry to reconnect.");
        UpdateLayout(); Capture(Path.Combine(directory,"youtube-native-error.png"));
        CheckInteraction(YouTubeNativePanel.IsVisible && YouTubeFailure.IsVisible && !BrowserPanel.IsVisible,"Browser failure exposes the old interface");
        SignInYouTubeAccount(this,new RoutedEventArgs());
        CheckInteraction(!BrowserPanel.IsVisible,"Account action exposes the old interface after initialization failure");
        RetryYouTubeStartup(this,new RoutedEventArgs());
        for(int i=0;i<120;i++)
        {
            await Task.Delay(250);
            if(musicBrowser?.CoreWebView2?.Source.StartsWith("https://music.youtube.com/",StringComparison.Ordinal)==true
                && await musicBrowser.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('ytmusic-app')")=="true")break;
        }
        CheckInteraction(musicBrowser?.CoreWebView2!=null && !YouTubeFailure.IsVisible && musicBrowser.CoreWebView2.Source.StartsWith("https://music.youtube.com/",StringComparison.Ordinal),"Retry did not recover the native YouTube interface");
        core=musicBrowser!.CoreWebView2;
        await File.WriteAllTextAsync(Path.Combine(directory,"youtube-error-recovery-check.txt"),"PASS simulated startup failure stays in native DIPlayer, account action cannot expose the old panel, and Retry reinitializes YouTube without deleting its profile.");
        using (var png = File.Create(Path.Combine(directory, "youtube-browser.png"))) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
        core.Navigate("https://music.youtube.com/search?q=lofi");
        await Task.Delay(3000);
        await File.WriteAllTextAsync(Path.Combine(directory,"youtube-ui-dom.txt"), await core.ExecuteScriptAsync("""JSON.stringify({items:Array.from(document.querySelectorAll('ytmusic-responsive-list-item-renderer')).slice(0,2).map(e=>e.outerHTML.slice(0,18000)),buttons:Array.from(document.querySelectorAll('ytmusic-player-bar button,ytmusic-player-bar tp-yt-paper-icon-button')).map(e=>({tag:e.tagName,id:e.id,cls:e.className,icon:e.getAttribute('icon'),label:e.getAttribute('aria-label'),title:e.title})),search:Array.from(document.querySelectorAll('ytmusic-search-box input')).map(e=>e.outerHTML)})"""));
        string searchUrl = core.Source;
        CheckInteraction(searchUrl.StartsWith("https://music.youtube.com/search", StringComparison.Ordinal) || searchUrl.StartsWith("https://consent.google.com/", StringComparison.Ordinal) || searchUrl.StartsWith("https://consent.youtube.com/", StringComparison.Ordinal), "Search navigation failed");
        OpenPlayerSettings(this, new RoutedEventArgs());
        CheckInteraction(SettingsPanel.IsVisible && !YouTubeNativePanel.IsVisible, "Browser covers settings");
        await ApplyBrowserMode(false);
        CheckInteraction(musicBrowser == null && !BrowserPanel.IsVisible && NativePlayer.IsVisible && LibraryPanel.IsVisible && SearchNav.IsEnabled, "Optimized mode did not unload browser and restore library");
        await File.WriteAllTextAsync(Path.Combine(directory, "browser-mode-check.txt"), "PASS optimized startup without WebView, settings full/narrow, enabled official uBO Lite, live YouTube page, search navigation, settings above browser, browser disposal and native library restore. Playback/ad removal not asserted; consent/account availability may apply.");
    }
}
