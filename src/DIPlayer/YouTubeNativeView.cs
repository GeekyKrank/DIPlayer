using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DIPlayer.Core;
namespace DIPlayer;
public partial class MainWindow
{
    readonly ObservableCollection<YouTubeItem> youtubeItems = [];
    readonly DispatcherTimer youtubeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    YouTubeState? youtubeState;
    bool youtubeReading, youtubeSeeking, youtubeVolumeDragging, youtubeWebsiteVisible;
    bool youtubeReturningFromSignIn;
    void ObserveYouTubeAccountNavigation(Uri uri)
    {
        if (uri.Host is "accounts.google.com" or "consent.google.com" or "consent.youtube.com")
            youtubeReturningFromSignIn = true;
    }
    string youtubeItemsSignature = "";
    string? youtubeExpectedRoute;
    static bool YouTubeRouteMatches(string actual, string expected)
    {
        if (!Uri.TryCreate(actual, UriKind.Absolute, out var source)) return false;
        var target = new Uri("https://music.youtube.com" + expected);
        if (source.AbsolutePath != target.AbsolutePath) return false;
        static Dictionary<string,string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)
            .Select(pair=>pair.Split('=',2)).GroupBy(pair=>Uri.UnescapeDataString(pair[0].Replace('+',' ')))
            .ToDictionary(group=>group.Key,group=>Uri.UnescapeDataString((group.Last().Length>1?group.Last()[1]:"").Replace('+',' ')));
        var values = Query(source);
        return Query(target).All(pair=>values.TryGetValue(pair.Key,out var value)&&value==pair.Value);
    }
    void ShowYouTubeNative()
    {
        if (!browserMode) return;
        youtubeWebsiteVisible = false;
        youtubeReturningFromSignIn = false;
        ParkYouTubeBrowser();
        SettingsPanel.Visibility = Visibility.Collapsed;
        YouTubeNativePanel.Visibility = Visibility.Visible;
        YouTubeCards.ItemsSource = youtubeItems;
        SyncYouTubeNavigation();
        if (!youtubeTimer.IsEnabled)
        {
            youtubeTimer.Tick -= YouTubeTick;
            youtubeTimer.Tick += YouTubeTick;
            youtubeTimer.Start();
        }
        _ = RefreshYouTubeNative();
        _ = RestoreYouTubeQueueOwnership();
    }
    async Task RestoreYouTubeQueueOwnership()
    {
        try { if (!youtubeWebsiteVisible && browserMode) await ConfigureYouTubeQueue(); }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
    void ParkYouTubeBrowser()
    {
        if (musicBrowser == null) return;
        youtubeWebsiteVisible = false;
        BrowserPanel.Visibility = Visibility.Visible;
        BrowserPanel.ClipToBounds = true;
        BrowserPanel.Width = BrowserPanel.Height = double.NaN;
        BrowserPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        BrowserPanel.VerticalAlignment = VerticalAlignment.Stretch;
        BrowserToolbar.Visibility = BrowserNotice.Visibility = Visibility.Collapsed;
        musicBrowser.Width = musicBrowser.Height = double.NaN;
        Dispatcher.BeginInvoke(ClipYouTubeHost, DispatcherPriority.Loaded);
    }
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    void ClipYouTubeHost()
    {
        // HwndHost ignores WPF z-order/clipping. Keep its viewport active for the
        // site's media loader, while exposing only a one-pixel host region.
        if (musicBrowser == null || youtubeWebsiteVisible || !browserMode || musicBrowser.Handle == IntPtr.Zero) return;
        var region = CreateRectRgn(0, 0, 1, 1);
        if (SetWindowRgn(musicBrowser.Handle, region, true) == 0) DeleteObject(region);
    }
    void YouTubeTick(object? sender, EventArgs e)
    {
        if (browserMode && (IsVisible || miniPlayer?.IsVisible == true)) _ = RefreshYouTubeNative();
    }
    async Task RefreshYouTubeNative()
    {
        if (youtubeReading || !browserMode || musicBrowser?.CoreWebView2 == null) return;
        youtubeReading = true;
        int generation = browserGeneration;
        try
        {
            string json = await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Snapshot);
            if (generation != browserGeneration || !browserMode || json.Length > 256 * 1024) return;
            var state = JsonSerializer.Deserialize<YouTubeState>(json);
            if (state == null) return;
            youtubeState = state;
            SyncYouTubeHeader();
            if (youtubeWebsiteVisible && youtubeReturningFromSignIn && state.SignedIn && !state.RequiresWebsite)
            {
                youtubeItemsSignature = "";
                ShowYouTubeNative();
            }
            ClipYouTubeHost();
            if (state.RequiresWebsite && !youtubeWebsiteVisible) YouTubeStatus.Text = "Use Sign in / account to complete the account prompt.";
            string signature = string.Join('\n', state.Items.Select(i => i.Key + i.Title + i.Subtitle + i.Artwork + i.Section));
            bool currentRoute = youtubeExpectedRoute == null || YouTubeRouteMatches(state.Url, youtubeExpectedRoute);
            if (currentRoute) youtubeExpectedRoute = null;
            if (currentRoute && !youtubeQueueView && !youtubeKeepCards && !youtubeOpeningCollection && signature != youtubeItemsSignature)
            {
                youtubeItemsSignature = signature;
                youtubeItems.Clear();
                foreach (var item in state.Items.Where(item => !IsYouTubeLikesDuplicate(item, state.Url)).Take(100)) youtubeItems.Add(item);
                UpdateYouTubeShelves(state.Url);
            }
            YouTubeEmpty.Visibility = youtubeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!youtubeQueueView && !youtubeOpeningCollection) YouTubeStatus.Text = youtubeItems.Count > 0 ? $"{youtubeItems.Count} items · YouTube Music" : "Loading… If sign-in or consent is required, open Website.";
            if (state.IsAd) YouTubeStatus.Text = "YouTube advertisement · uBO Lite may not block every ad.";
            YouTubePosition.IsEnabled = !state.IsAd && state.Duration > 0;
            YouTubeNowTitle.Text = state.Title.Length > 0 ? state.Title : "YouTube Music";
            YouTubeNowArtist.Text = state.Artist;
            YouTubeViewCount.Text = state.Views ?? "";
            YouTubeLikeCount.Text = state.LikeCount ?? "";
            YouTubeViewStat.Visibility = string.IsNullOrWhiteSpace(state.Views) ? Visibility.Collapsed : Visibility.Visible;
            YouTubeLikeStat.Visibility = string.IsNullOrWhiteSpace(state.LikeCount) ? Visibility.Collapsed : Visibility.Visible;
            if (YouTubeNowArtwork.Url != state.Artwork) YouTubeNowArtwork.Url = state.Artwork;
            YouTubePlay.Content = state.Paused ? "\uE768" : "\uE769";
            YouTubeRepeat.Content = settings.RepeatMode == 2 ? "\uE8ED" : "\uE8EE";
            YouTubeRepeat.Foreground = settings.RepeatMode == 0 ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.White;
            YouTubeShuffle.Foreground = settings.Shuffle ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Gray;
            YouTubeLike.Foreground = state.Liked ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Gray;
            if (!youtubeSeeking)
            {
                YouTubePosition.Maximum = Math.Max(1, state.Duration);
                YouTubePosition.Value = Math.Clamp(state.Position, 0, YouTubePosition.Maximum);
                YouTubeTime.Text = Format(state.Position) + " / " + Format(state.Duration);
            }
            if (!youtubeVolumeDragging) YouTubeVolume.Value = state.Volume;
            SyncMiniPlayer();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { youtubeReading = false; }
    }
    string youtubeDisplayedUrl = "https://music.youtube.com/";
    sealed record YouTubeShelf(string Title, YouTubeItem[] Items);
    static bool IsYouTubeLikesDuplicate(YouTubeItem item, string url)
    {
        string path = new Uri(url).AbsolutePath;
        if (!path.StartsWith("/library", StringComparison.Ordinal) && !path.StartsWith("/browse/FEmusic_library", StringComparison.Ordinal)) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(item.Key, @"(?:[?&]list=LM(?:&|$)|^/browse/VLLM(?:\?|$))")) return true;
        return new[] { "Liked Music", "Liked songs", "Your Likes", "Понравившаяся музыка", "Музика, яка сподобалася", "Музика, що сподобалася" }
            .Any(name => item.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    void UpdateYouTubeShelves(string url)
    {
        youtubeDisplayedUrl = url;
        SyncYouTubeNavigation();
        bool cards = !youtubeQueueView && youtubeCollection == null && !url.Contains("/search", StringComparison.OrdinalIgnoreCase)
            && !url.Contains("/playlist", StringComparison.OrdinalIgnoreCase);
        YouTubeCardsScroll.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
        YouTubeShelvesScroll.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;
        YouTubeCollectionHeader.Visibility = youtubeCollection == null ? Visibility.Collapsed : Visibility.Visible;
        YouTubeShelves.ItemsSource = youtubeItems.GroupBy(item => item.Section)
            .Select(group => new YouTubeShelf(group.Key, group.ToArray())).ToArray();
    }
    void YouTubeHeaderSizeChanged(object sender, SizeChangedEventArgs e) => SyncYouTubeHeader();
    void SyncYouTubeHeader()
    {
        if (YouTubeAccountButton == null || YouTubeSearchRow == null) return;
        YouTubeAccountButton.Content = YouTubeSearchRow.ActualWidth < 700 ? "Account"
            : youtubeState?.SignedIn == true ? "Account connected" : "Sign in / account";
        YouTubeAccountButton.ToolTip = youtubeState?.SignedIn == true ? "YouTube account connected" : "Sign in to YouTube Music";
    }
    void YouTubeFooterSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (YouTubeNowInfo == null || YouTubeTransport == null || YouTubeFooterDetails == null) return;
        bool compact = e.NewSize.Width < 820;
        Grid.SetRow(YouTubeNowInfo, 0); Grid.SetColumn(YouTubeNowInfo, 0); Grid.SetColumnSpan(YouTubeNowInfo, compact ? 3 : 1);
        Grid.SetRow(YouTubeTransport, compact ? 1 : 0); Grid.SetColumn(YouTubeTransport, compact ? 0 : 1); Grid.SetColumnSpan(YouTubeTransport, compact ? 3 : 1);
        Grid.SetRow(YouTubeFooterDetails, compact ? 2 : 0); Grid.SetColumn(YouTubeFooterDetails, compact ? 0 : 2); Grid.SetColumnSpan(YouTubeFooterDetails, compact ? 3 : 1);
        YouTubeFooterDetails.HorizontalAlignment = HorizontalAlignment.Right;
    }
    void ScrollYouTubePage(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer page) return;
        // Handle the tunnel event before a nested horizontal shelf consumes it.
        double distance = SystemParameters.WheelScrollLines < 0
            ? page.ViewportHeight : Math.Max(1, SystemParameters.WheelScrollLines) * 32;
        page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta / 120.0 * distance);
        e.Handled = true;
    }
    async void OpenYouTubeCard(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: YouTubeItem item })
            await Guard(() => ActivateYouTubeItem(item));
    }
    async void SignInYouTubeAccount(object sender, RoutedEventArgs e)
    {
        if (YouTubeFailure.Visibility == Visibility.Visible || musicBrowser?.CoreWebView2 == null) { ShowYouTubeFailure("Press Retry to load YouTube Music before signing in."); return; }
        if (youtubeState?.SignedIn == true)
        {
            YouTubeStatus.Text = "Your YouTube account is connected. Open Library or Likes to see your music.";
            return;
        }
        youtubeReturningFromSignIn = true;
        OpenYouTubeWebsite(sender, e);
        if (musicBrowser?.CoreWebView2 != null)
            await Guard(async () => { await musicBrowser.CoreWebView2.ExecuteScriptAsync("document.querySelector('ytmusic-nav-bar a[href*=\"accounts.google.com\"],ytmusic-nav-bar a[href*=\"ServiceLogin\"]')?.click()"); });
    }
    void OpenYouTubeWebsite(object sender, RoutedEventArgs e)
    {
        if (YouTubeFailure.Visibility == Visibility.Visible || musicBrowser?.CoreWebView2 == null) { ShowYouTubeFailure("Press Retry to reconnect to YouTube Music."); return; }
        youtubeWebsiteVisible = true;
        _ = ReleaseYouTubeQueueOwnership();
        if (musicBrowser != null && musicBrowser.Handle != IntPtr.Zero) SetWindowRgn(musicBrowser.Handle, IntPtr.Zero, true);
        BrowserPanel.Width = BrowserPanel.Height = double.NaN;
        BrowserPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        BrowserPanel.VerticalAlignment = VerticalAlignment.Stretch;
        BrowserToolbar.Visibility = BrowserNotice.Visibility = Visibility.Visible;
        if (musicBrowser != null) { musicBrowser.Width = musicBrowser.Height = double.NaN; }
        YouTubeNativePanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        BrowserPanel.Visibility = Visibility.Visible;
    }
    void ReturnYouTubeNative(object sender, RoutedEventArgs e) => ShowYouTubeNative();
    async Task NavigateYouTube(string key, string heading, bool seedQueue = true)
    {
        if (musicBrowser?.CoreWebView2 == null) return;
        if (!Uri.TryCreate("https://music.youtube.com" + key, UriKind.Absolute, out var uri) || uri.Host != "music.youtube.com" || key.Length > 2048) return;
        if (key.StartsWith("/watch?", StringComparison.Ordinal))
        {
            await PlayYouTubeKey(key, seedQueue);
            return;
        }
        RememberYouTubeView(CaptureYouTubeView());
        youtubeKeepCards = false;
        youtubeCollection = null;
        YouTubeCollectionHeader.Visibility = Visibility.Collapsed;
        youtubeQueueView = false;
        youtubeItemsSignature = "";
        youtubeExpectedRoute = uri.PathAndQuery;
        youtubeItems.Clear();
        UpdateYouTubeShelves(uri.AbsoluteUri);
        YouTubeShelvesScroll.ScrollToTop();
        await ConfigureYouTubeQueue();
        YouTubeHeading.Text = heading;
        YouTubeStatus.Text = "Loading…";
        await musicBrowser.CoreWebView2.ExecuteScriptAsync("delete document.documentElement.dataset.diplayerView");
        await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Navigate(key));
        ShowYouTubeNative();
    }
    async void SearchYouTubeNative(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Guard(() => NavigateYouTube("/search?q=" + Uri.EscapeDataString(YouTubeSearch.Text.Trim()), "Search results"));
    }
    async void YouTubeHomeClick(object sender, RoutedEventArgs e) => await Guard(() => NavigateYouTube("/", "YouTube Music"));
    async void YouTubeLibraryClick(object sender, RoutedEventArgs e) => await Guard(() => NavigateYouTube("/library", "YouTube Music library"));
    async void YouTubeLikesClick(object sender, RoutedEventArgs e) => await Guard(() => NavigateYouTube("/playlist?list=LM", "Likes"));
    async void YouTubeControl(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action } && musicBrowser?.CoreWebView2 != null)
            await Guard(() => RunYouTubeControl(action));
    }
    async void YouTubeQueueClick(object sender, RoutedEventArgs e)
    {
        DisplayYouTubeQueue();
        ShowYouTubeNative();
        await Task.CompletedTask;
    }
    async void YouTubeLoadMore(object sender, RoutedEventArgs e)
    {
        if (musicBrowser?.CoreWebView2 != null)
            await Guard(async () => { await musicBrowser.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0,document.documentElement.scrollHeight);document.querySelector('ytmusic-browse-response,ytmusic-search-page')?.scrollIntoView({block:'end'})"); await RefreshYouTubeNative(); });
    }
    void YouTubeSeekDown(object sender, MouseButtonEventArgs e)
    {
        if (youtubeState?.Duration is not > 0 || youtubeState.IsAd) return;
        e.Handled = true; youtubeSeeking = true; YouTubePosition.CaptureMouse(); YouTubeSeekPointer(e.GetPosition(YouTubePosition).X);
    }
    void YouTubeSeekPointer(double x)
    {
        YouTubePosition.Value = Math.Clamp((x - 9) / Math.Max(1, YouTubePosition.ActualWidth - 18), 0, 1) * YouTubePosition.Maximum;
        YouTubeTime.Text = Format(YouTubePosition.Value) + " / " + Format(youtubeState?.Duration ?? 0);
    }
    void YouTubeSeekMove(object sender, MouseEventArgs e) { if (youtubeSeeking) { e.Handled = true; YouTubeSeekPointer(e.GetPosition(YouTubePosition).X); } }
    async void YouTubeSeekUp(object sender, MouseButtonEventArgs e)
    {
        if (!youtubeSeeking) return;
        e.Handled = true; YouTubeSeekPointer(e.GetPosition(YouTubePosition).X);
        youtubeSeeking = false; YouTubePosition.ReleaseMouseCapture();
        if (musicBrowser?.CoreWebView2 != null)
            await Guard(async () => { await musicBrowser.CoreWebView2.ExecuteScriptAsync("(()=>{const v=document.querySelector('video');if(v)v.currentTime=" + JsonSerializer.Serialize(YouTubePosition.Value) + ";})()"); });
    }
    void YouTubeSeekLost(object sender, MouseEventArgs e) => youtubeSeeking = false;
    async Task SetYouTubeVolume(double value)
    {
        value = Math.Clamp(value, 0, 1); YouTubeVolume.Value = value;
        if (musicBrowser?.CoreWebView2 != null)
            await musicBrowser.CoreWebView2.ExecuteScriptAsync("(()=>{const v=document.querySelector('video');if(v){v.volume=" + JsonSerializer.Serialize(value) + ";v.muted=false;}})()");
    }
    async void YouTubeVolumeDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; youtubeVolumeDragging = true; YouTubeVolume.CaptureMouse();
        await Guard(() => SetYouTubeVolume((e.GetPosition(YouTubeVolume).X - 7) / Math.Max(1, YouTubeVolume.ActualWidth - 14)));
    }
    async void YouTubeVolumeMove(object sender, MouseEventArgs e)
    {
        if (youtubeVolumeDragging) { e.Handled = true; await Guard(() => SetYouTubeVolume((e.GetPosition(YouTubeVolume).X - 7) / Math.Max(1, YouTubeVolume.ActualWidth - 14))); }
    }
    void YouTubeVolumeUp(object sender, MouseButtonEventArgs e) { youtubeVolumeDragging = false; YouTubeVolume.ReleaseMouseCapture(); e.Handled = true; }
    void YouTubeVolumeLost(object sender, MouseEventArgs e) => youtubeVolumeDragging = false;
    Track? YouTubeCurrentTrack() => youtubeState == null ? null : new Track("youtube-current", youtubeState.Title, youtubeState.Artist, (long)(youtubeState.Duration * 1000), youtubeState.Artwork, youtubeState.Url, "playable");
}
