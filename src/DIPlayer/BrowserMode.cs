using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DIPlayer.Core;
using DIPlayer.Storage;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace DIPlayer;
public partial class MainWindow
{
    WebView2? musicBrowser;
    bool browserMode, changingBrowserMode;
    int browserGeneration;
    void OpenPlayerSettings(object sender, RoutedEventArgs e)
    {
        UpdateMemoryCacheStatus();
        if (browserMode && musicBrowser?.CoreWebView2 != null) ParkYouTubeBrowser();
        else BrowserPanel.Visibility = Visibility.Collapsed;
        YouTubeNativePanel.Visibility = Visibility.Collapsed;
        SetView("settings");
        changingBrowserMode = true;
        BrowserModeSwitch.IsChecked = HeaderModeSwitch.IsChecked = YouTubeModeSwitch.IsChecked = browserMode;
        changingBrowserMode = false;
        SettingsPanel.Visibility = Visibility.Visible;
    }
    async void BrowserModeChanged(object sender, RoutedEventArgs e)
    {
        if (changingBrowserMode || !startupInitialized) return;
        if (sender is System.Windows.Controls.Primitives.ToggleButton toggle)
            await Guard(() => ApplyBrowserMode(toggle.IsChecked == true));
    }
    async Task ApplyBrowserMode(bool enabled)
    {
        int generation = ++browserGeneration;
        youtubeMenuOpen=false; YouTubeActionsOverlay.Visibility=Visibility.Collapsed;
        StopYouTubeLyrics();
        YouTubeFailure.Visibility = Visibility.Collapsed;
        YouTubeTransport.IsEnabled = YouTubeVolume.IsEnabled = true;
        browserMode = enabled;
        settings.YouTubeBrowserMode = enabled;
        if (DiagnosticLibraryDirectory() == null) settings.Save();
        changingBrowserMode = true;
        BrowserModeSwitch.IsChecked = HeaderModeSwitch.IsChecked = YouTubeModeSwitch.IsChecked = enabled;
        SoundCloudServiceSwitch.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        changingBrowserMode = false;
        SoundCloudRail.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        YouTubeRail.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        HomeNav.ToolTip = enabled ? "YouTube Music home" : "SoundCloud library";
        System.Windows.Automation.AutomationProperties.SetName(HomeNav, enabled ? "YouTube Music home" : "SoundCloud library");
        SettingsModeMessage.Text = enabled ? "YouTube Music · browser mode. More memory is used. Ad filtering is experimental; playback depends on YouTube." : "Optimized SoundCloud mode. The embedded browser is unloaded.";
        if (!enabled)
        {
            DisposeMusicBrowser();
            SettingsPanel.Visibility = Visibility.Collapsed;
            BrowserPanel.Visibility = Visibility.Collapsed;
            YouTubeNativePanel.Visibility = Visibility.Collapsed;
            NativePlayer.Visibility = Visibility.Visible;
            SearchHeader.Visibility = Visibility.Visible;
            LibraryView(this, new RoutedEventArgs());
            return;
        }
        playGeneration++;
        navigationCancel?.Cancel(); playCancel?.Cancel(); searchCancel?.Cancel(); metadataCancel?.Cancel(); libraryArtworkCancel?.Cancel();
        miniPlayer?.Hide();
        await player.Stop();
        NativePlayer.Visibility = Visibility.Collapsed;
        SearchHeader.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = ContentPanel.Visibility = ImportPanel.Visibility = PlaylistBack.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        BrowserPanel.Visibility = Visibility.Visible;
        BrowserNotice.Text = "Loading YouTube Music…";
        if (musicBrowser?.CoreWebView2 != null) { ShowYouTubeNative(); return; }
        var browser = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 8, 8, 8) };
        musicBrowser = browser;
        browser.LayoutUpdated += (_, _) => ClipYouTubeHost();
        BrowserHost.Child = browser;
        ShowYouTubeNative();
        string startupStage="browser environment";
        try
        {
            bool checkExisting=DiagnosticLibraryDirectory()!=null&&Environment.GetCommandLineArgs().Contains("--check-existing-youtube-profile");
            string profile = Path.Combine(checkExisting?LibraryStore.DataDirectory:DiagnosticLibraryDirectory() ?? LibraryStore.DataDirectory, "youtube-browser");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile, new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true, AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" });
            if (!browserMode || generation != browserGeneration || quitting) return;
            startupStage="WebView controller";
            await browser.EnsureCoreWebView2Async(environment);
            if (!browserMode || generation != browserGeneration || quitting) return;
            startupStage="browser settings";
            var core = browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, request) =>
            {
                if (BrowserAdFilter.Block(request.Request.Uri))
                    request.Response = environment.CreateWebResourceResponse(new MemoryStream([]), 204, "No Content", "Content-Type: text/plain");
            };
            core.NavigationStarting += (_, args) =>
            {
                if (browserConfiguringBlocker && args.Uri.StartsWith(browserBlockerOrigin + "/", StringComparison.Ordinal)) return;
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https") { args.Cancel = true; return; }
                ObserveYouTubeAccountNavigation(uri);
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated && Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") core.Navigate(args.Uri);
            };
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.WindowCloseRequested += (_, _) => LogWindowTransition("Ignored browser close request");
            core.ProcessFailed += (_, args) =>
            {
                if (generation == browserGeneration && args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) ShowYouTubeFailure("Playback was interrupted. Press Retry to reconnect; your account stays signed in.");
            };
            core.NavigationCompleted += async (_, args) =>
            {
                if (generation != browserGeneration || browserConfiguringBlocker) return;
                if (args.IsSuccess && Uri.TryCreate(core.Source,UriKind.Absolute,out var source))
                {
                    if (source.Host=="music.youtube.com" && (!youtubeWebsiteVisible || youtubeReturningFromSignIn))
                    {
                        await Guard(async () => { await core.ExecuteScriptAsync(YouTubeVolumeBridge.Install(settings.Volume)); });
                        if (generation != browserGeneration || quitting) return;
                        youtubeReturningFromSignIn = false;
                        youtubeItemsSignature = "";
                        ShowYouTubeNative();
                    }
                    else if (source.Host!="music.youtube.com") OpenYouTubeWebsite(this,new RoutedEventArgs());
                }
                if (!args.IsSuccess) ShowYouTubeFailure("Check your connection and press Retry. You can also switch to SC above.");
                BrowserNotice.Text = args.IsSuccess ? "YouTube Music · uBO Lite enabled · ad filtering is not guaranteed" : "Could not load YouTube Music. Check your connection and press Reload.";
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync("""
                (() => {
                  if (location.hostname !== 'music.youtube.com') return;
                  const hidePromos = () => {
                    if (document.getElementById('diplayer-ad-style')) return;
                    const style = document.createElement('style'); style.id = 'diplayer-ad-style';
                    style.textContent = 'ytmusic-mealbar-promo-renderer,ytmusic-statement-banner-renderer,ytd-ad-slot-renderer{display:none!important}';
                    (document.head || document.documentElement)?.appendChild(style);
                  };
                  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', hidePromos, {once:true}); else hidePromos();
                })();
                """);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeEndedBridge);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeVolumeBridge.Install(settings.Volume));
            core.WebMessageReceived += (_, message) =>
            {
                if (!browserMode || quitting || generation != browserGeneration || !Uri.TryCreate(message.Source, UriKind.Absolute, out var source) || source.Host != "music.youtube.com") return;
                try
                {
                    if (message.TryGetWebMessageAsString() == "diplayer-youtube-ended")
                        Dispatcher.BeginInvoke(async () => await Guard(() => AdvanceYouTube(1)));
                }
                catch (ArgumentException) { }
            };
            try
            {
                startupStage="ad filter package";
                string blocker = await Task.Run(() => BrowserBlocker.Prepare(checkExisting?LibraryStore.DataDirectory:DiagnosticLibraryDirectory() ?? LibraryStore.DataDirectory));
                if (!browserMode || generation != browserGeneration || quitting) return;
                startupStage="ad filter registration";
                var extensions=await core.Profile.GetBrowserExtensionsAsync();
                var extension=extensions.FirstOrDefault(e=>e.Name.Contains("uBlock",StringComparison.OrdinalIgnoreCase));
                if(extension==null)
                {
                    try { extension=await core.Profile.AddBrowserExtensionAsync(blocker); }
                    catch(Exception registrationError) when(registrationError is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
                    {
                        LogBrowserStartupFailure(startupStage,registrationError);
                        string repaired=await Task.Run(()=>BrowserBlocker.Prepare(checkExisting?LibraryStore.DataDirectory:DiagnosticLibraryDirectory()??LibraryStore.DataDirectory,true));
                        extension=await core.Profile.AddBrowserExtensionAsync(repaired);
                    }
                }
                if (!extension.IsEnabled) await extension.EnableAsync(true);
                startupStage="ad filter configuration";
                await ConfigureBrowserBlocker(core, extension.Id);
            }
            catch(Exception filterError) when(filterError is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException or JsonException)
            {
                LogBrowserStartupFailure(startupStage,filterError);
                browserBlockerDiagnostics="Filtering extension unavailable; basic request filtering remains active.";
                browserConfiguringBlocker=false;
                BrowserNotice.Text="YouTube Music · extension unavailable; basic filtering only";
            }
            startupStage="YouTube navigation";
            if (!browserMode || generation != browserGeneration || quitting) return;
            core.Navigate("https://music.youtube.com/");
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or TimeoutException)
        {
            LogBrowserStartupFailure(startupStage,error);
            if (generation == browserGeneration && !quitting)
            {
                string message = error is WebView2RuntimeNotFoundException ? "Microsoft Edge WebView2 Runtime is required. Open Settings to install it, or switch to SC above." : "YouTube Music could not start. Press Retry or switch to SC above. Your saved account and libraries are preserved.";
                DisposeMusicBrowser();
                ShowYouTubeFailure(message);
            }
        }
    }
    void DisposeMusicBrowser()
    {
        youtubeClippedHandle = IntPtr.Zero;
        youtubeCatalogReadAt = DateTime.MinValue;
        StopYouTubeLyrics();
        youtubeTimer.Stop();
        youtubeQueue.Tracks.Clear(); youtubeQueue.Index = -1; youtubeQueue.ResetNavigation(); youtubeQueueView = false; youtubeQueueCleared = false;
        youtubeExpectedRoute = null;
        youtubeKeepCards = youtubeOpeningCollection = false; youtubeCollection = null; youtubeHistory.Clear(); youtubeDisplayedUrl = "https://music.youtube.com/";
        YouTubeCollectionHeader.Visibility = Visibility.Collapsed;
        youtubeState = null; youtubeItems.Clear(); youtubeItemsSignature = ""; youtubeWebsiteVisible = false; youtubeReturningFromSignIn = false;
        YouTubeNativePanel.Visibility = Visibility.Collapsed;
        BrowserPanel.Width = BrowserPanel.Height = double.NaN;
        BrowserPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        BrowserPanel.VerticalAlignment = VerticalAlignment.Stretch;
        BrowserToolbar.Visibility = BrowserNotice.Visibility = Visibility.Visible;
        BrowserHost.Child = null;
        musicBrowser?.Dispose();
        musicBrowser = null;
    }
    void BrowserHome(object sender, RoutedEventArgs e) => musicBrowser?.CoreWebView2?.Navigate("https://music.youtube.com/");
    void BrowserBack(object sender, RoutedEventArgs e) { if (musicBrowser?.CanGoBack == true) musicBrowser.GoBack(); }
    void BrowserReload(object sender, RoutedEventArgs e) => musicBrowser?.Reload();
    void SearchYouTube(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || musicBrowser?.CoreWebView2 == null) return;
        e.Handled = true;
        musicBrowser.CoreWebView2.Navigate("https://music.youtube.com/search?q=" + Uri.EscapeDataString(BrowserSearch.Text.Trim()));
    }
    void ClosePlayerSettings(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        if (browserMode) ShowYouTubeNative();
        else LibraryView(sender, e);
    }
    void OpenWebViewRuntime(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
    async Task BrowserMedia(int id)
    {
        if (musicBrowser?.CoreWebView2 == null) return;
        await RunYouTubeControl(id switch { 0 => "play", 1 => "next", 2 => "previous", _ => "stop" });
    }
}
