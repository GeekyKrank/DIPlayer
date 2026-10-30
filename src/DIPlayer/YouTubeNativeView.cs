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
    DateTime youtubeCatalogReadAt;
    IntPtr youtubeClippedHandle;
    bool? youtubeCompactHeader;
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
        if (youtubeClippedHandle == musicBrowser.Handle) return;
        var region = CreateRectRgn(0, 0, 1, 1);
        if (SetWindowRgn(musicBrowser.Handle, region, true) == 0) DeleteObject(region);
        else youtubeClippedHandle = musicBrowser.Handle;
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
            bool readCatalog = IsVisible && WindowState != WindowState.Minimized && !youtubeLyricsOpen && !SettingsPanel.IsVisible && !youtubeQueueView && !youtubeKeepCards && !youtubeOpeningCollection && !youtubeLoadingMore
                && (youtubeItems.Count == 0 || youtubeExpectedRoute != null || DateTime.UtcNow - youtubeCatalogReadAt >= TimeSpan.FromSeconds(5));
            string json = await musicBrowser.CoreWebView2.ExecuteScriptAsync(readCatalog ? YouTubeBridge.Snapshot : YouTubeBridge.PlaybackSnapshot);
            if (generation != browserGeneration || !browserMode || json.Length > 1024 * 1024) return;
            var state = JsonSerializer.Deserialize<YouTubeState>(json);
            if (state == null) return;
            if (readCatalog) youtubeCatalogReadAt = DateTime.UtcNow;
            youtubeState = state;
            ObserveLyricsPlayback();
            ObserveYouTubeEqualizer();
            SyncYouTubeHeader();
            if (youtubeRequestedArtist != null && state.Page?.Title.Equals(youtubeRequestedArtist,StringComparison.OrdinalIgnoreCase) == true && new Uri(state.Url).AbsolutePath.StartsWith("/@")) youtubeExpectedRoute=null;
            UpdateArtistHeader(state);
            if (youtubeWebsiteVisible && youtubeReturningFromSignIn && state.SignedIn && !state.RequiresWebsite)
            {
                youtubeItemsSignature = "";
                ShowYouTubeNative();
            }
            ClipYouTubeHost();
            if (state.RequiresWebsite && !youtubeWebsiteVisible) YouTubeStatus.Text = "Use Sign in / account to complete the account prompt.";
            string signature = readCatalog ? string.Join('\n', state.Items.Select(i => i.Key + i.Title + i.Subtitle + i.Artwork + i.Section + i.Kind + i.ArtistKey + i.Duration + i.Album + i.ArtistName + i.IsVideo)) : youtubeItemsSignature;
            bool currentRoute = youtubeExpectedRoute == null || YouTubeRouteMatches(state.Url, youtubeExpectedRoute);
            if (currentRoute) youtubeExpectedRoute = null;
            if (readCatalog && currentRoute && !youtubeQueueView && !youtubeKeepCards && !youtubeOpeningCollection && !youtubeLoadingMore && signature != youtubeItemsSignature)
            {
                youtubeItemsSignature = signature;
                SetYouTubeItems(state.Items.Where(item => !IsYouTubeLikesDuplicate(item, state.Url)).Take(600));
                UpdateYouTubeShelves(state.Url);
                RefreshSearchSuggestionsFromYouTube();
            }
            YouTubeEmpty.Visibility = youtubeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!youtubeQueueView && !youtubeOpeningCollection) YouTubeStatus.Text = youtubeLoadingMore ? "Loading more…" : youtubeItems.Count > 0 ? $"{youtubeItems.Count} items · YouTube Music" : "Loading… If sign-in or consent is required, open Website.";
            if (state.IsAd) YouTubeStatus.Text = "YouTube advertisement · uBO Lite may not block every ad.";
            YouTubePosition.IsEnabled = !state.IsAd && state.Duration > 0;
            YouTubeNowTitle.Text = state.Title.Length > 0 ? state.Title : "YouTube Music";
            YouTubeNowArtist.Text = state.Artist;
            YouTubeViewCount.Text = state.Views ?? "";
            YouTubeLikeCount.Text = state.LikeCount ?? "";
            YouTubeViewStat.Visibility = !settings.ShowTrackStats || string.IsNullOrWhiteSpace(state.Views) ? Visibility.Collapsed : Visibility.Visible;
            YouTubeLikeStat.Visibility = !settings.ShowTrackStats || string.IsNullOrWhiteSpace(state.LikeCount) ? Visibility.Collapsed : Visibility.Visible;
            if (YouTubeNowArtwork.Url != state.Artwork) YouTubeNowArtwork.Url = state.Artwork;
            YouTubePlay.Content = state.Paused ? UI.Symbols.Play : UI.Symbols.Pause;
            YouTubeRepeat.Content = settings.RepeatMode == 2 ? UI.Symbols.RepeatOne : UI.Symbols.Repeat;
            YouTubeRepeat.Foreground = settings.RepeatMode == 0 ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.White;
            YouTubeShuffle.Foreground = settings.Shuffle ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Gray;
            YouTubeLike.Foreground = state.Liked ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Gray;
            if (!youtubeSeeking)
            {
                YouTubePosition.Maximum = Math.Max(1, state.Duration);
                YouTubePosition.Value = Math.Clamp(state.Position, 0, YouTubePosition.Maximum);
                YouTubeTime.Text = Format(state.Position) + " / " + Format(state.Duration);
            }
            if (!youtubeVolumeDragging) YouTubeVolume.Value = settings.Volume;
            SyncMiniPlayer();
            if(youtubeLyricsOpen) _ = Guard(()=>RefreshYouTubeLyrics());
            RequestYouTubePageIfNeeded();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { youtubeReading = false; }
    }
    string youtubeDisplayedUrl = "https://music.youtube.com/";
    sealed record YouTubeShelf(string Title, YouTubeItem[] Items);
    readonly ObservableCollection<YouTubeShelf> youtubeShelves = [];
    void SetYouTubeItems(IEnumerable<YouTubeItem> source)
    {
        var next = source.ToArray();
        for (int i = 0; i < next.Length; i++)
        {
            if (i >= youtubeItems.Count) youtubeItems.Add(next[i]);
            else if (youtubeItems[i] != next[i]) youtubeItems[i] = next[i];
        }
        while (youtubeItems.Count > next.Length) youtubeItems.RemoveAt(youtubeItems.Count - 1);
    }
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
        ResetYouTubePagination(url);
        youtubeDisplayedUrl = url;
        SyncYouTubeNavigation();
        bool artist = youtubeCollection?.Kind == "Artist";
        bool cards = !youtubeQueueView && (youtubeCollection == null || artist) && !url.Contains("/search", StringComparison.OrdinalIgnoreCase)
            && !url.Contains("/playlist", StringComparison.OrdinalIgnoreCase);
        YouTubeCardsScroll.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
        YouTubeShelvesScroll.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;
        YouTubeCollectionHeader.Visibility = youtubeCollection == null || artist ? Visibility.Collapsed : Visibility.Visible;
        YouTubeArtistShelves.Visibility = artist ? Visibility.Visible : Visibility.Collapsed;
        bool collection = youtubeCollection != null && !artist && !youtubeQueueView;
        YouTubeCards.Visibility = artist || collection ? Visibility.Collapsed : Visibility.Visible;
        YouTubeCollectionTable.Visibility=collection?Visibility.Visible:Visibility.Collapsed;
        YouTubeCollectionTracks.ItemsSource=collection?youtubeItems.Where(i=>i.Kind=="Track").Select(i=>i with{Album=i.Album??(youtubeCollection!.Kind=="Album"?youtubeCollection.Title:null),ArtistName=i.ArtistName??youtubeCollectionArtistName}).ToArray():null;
        if (artist) { YouTubeCardsScroll.Visibility = Visibility.Visible; YouTubeShelvesScroll.Visibility = Visibility.Collapsed; }
        var shelves = youtubeItems.GroupBy(item => item.Section)
            .Select(group => new YouTubeShelf(group.Key, group.ToArray())).ToArray();
        // Keep existing shelf containers (and their horizontal positions) when a
        // continuation only appends sections further down the page.
        for (int i = 0; i < shelves.Length; i++)
        {
            var next = shelves[i];
            int previous = youtubeShelves.ToList().FindIndex(shelf => shelf.Title == next.Title);
            if (previous >= 0)
            {
                if (previous != i) youtubeShelves.Move(previous, i);
                if (!youtubeShelves[i].Items.SequenceEqual(next.Items)) youtubeShelves[i] = next;
            }
            else youtubeShelves.Insert(i, next);
        }
        while (youtubeShelves.Count > shelves.Length) youtubeShelves.RemoveAt(youtubeShelves.Count - 1);
        if (!ReferenceEquals(YouTubeShelves.ItemsSource, youtubeShelves)) YouTubeShelves.ItemsSource = youtubeShelves;
        SyncYouTubeArtistPresentation();
        SyncYouTubeHomePresentation();
    }
    void YouTubeHeaderSizeChanged(object sender, SizeChangedEventArgs e) => SyncYouTubeHeader();
    void SyncYouTubeHeader()
    {
        if (YouTubeAccountButton == null || YouTubeSearchRow == null) return;
        bool compact = YouTubeSearchRow.ActualWidth < 700;
        if (youtubeCompactHeader != compact)
        {
            youtubeCompactHeader = compact;
            Resources["ArtistStatWidth"] = new GridLength(compact ? 0 : 120);
            Resources["ArtistAlbumWidth"] = new GridLength(compact ? 0 : 160);
            Resources["CollectionArtistWidth"] = compact ? new GridLength(0) : new GridLength(1.3, GridUnitType.Star);
            Resources["CollectionAlbumWidth"] = compact ? new GridLength(0) : new GridLength(1.5, GridUnitType.Star);
        }
        bool connected = youtubeState?.SignedIn == true;
        YouTubeAccountLabel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        YouTubeAccountAvatar.Url = connected ? youtubeState?.AccountAvatar : null;
        YouTubeAccountButton.ToolTip = connected ? string.IsNullOrWhiteSpace(youtubeState?.AccountName) ? "Your Google account" : youtubeState.AccountName : "Sign in to YouTube Music";
    }
    void YouTubeFooterSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (YouTubeNowInfo == null || YouTubeTransport == null || YouTubeFooterDetails == null) return;
        bool compact = e.NewSize.Width < 820;
        Grid.SetRow(YouTubeNowInfo, 0); Grid.SetColumn(YouTubeNowInfo, 0); Grid.SetColumnSpan(YouTubeNowInfo, 1);
        Grid.SetRow(YouTubeTransport, 0); Grid.SetColumn(YouTubeTransport, 1); Grid.SetColumnSpan(YouTubeTransport, 1);
        Grid.SetRow(YouTubeFooterDetails, compact ? 1 : 0); Grid.SetColumn(YouTubeFooterDetails, compact ? 0 : 2); Grid.SetColumnSpan(YouTubeFooterDetails, compact ? 3 : 1);
        YouTubeFooter.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        YouTubeNowInfo.ColumnDefinitions[1].Width = compact ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        YouTubeFooterDetails.Margin = compact ? new Thickness(0, 7, 0, 0) : new Thickness(0);
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
    void YouTubePageScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Ignore bubbled changes from the horizontal carousels.
        if (sender is not ScrollViewer page || !ReferenceEquals(e.OriginalSource, page)) return;
        if (e.VerticalChange > 0 || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
            RequestYouTubePageIfNeeded();
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
        youtubeClippedHandle = IntPtr.Zero;
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
        ClosePlaybackPanelsForNavigation();
        youtubeRequestedArtist = key.StartsWith("/channel/") || key.StartsWith("/browse/UC") || key.StartsWith("/@") ? heading : null;
        ResetYouTubePagination(uri.AbsoluteUri, true);
        RememberYouTubeView(CaptureYouTubeView());
        youtubeKeepCards = false;
        youtubeCollection = null;
        youtubeArtistPage = null;
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
        if(await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeBridge.Navigate(key))=="false") musicBrowser.CoreWebView2.Navigate(uri.AbsoluteUri);
        ShowYouTubeNative();
    }
    async void SearchYouTubeNative(object sender, KeyEventArgs e)
    {
        if (HandleSearchSuggestionKey(e)) return;
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Guard(() => CommitYouTubeSearch(YouTubeSearch.Text));
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
        value = Math.Clamp(value, 0, 1);
        RememberVolume(value);
        VolumeSlider.Value = YouTubeVolume.Value = value;
        if (musicBrowser?.CoreWebView2 != null)
            await musicBrowser.CoreWebView2.ExecuteScriptAsync(YouTubeVolumeBridge.Install(value));
    }
    async void YouTubeVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (startupInitialized && browserMode && Math.Abs(e.NewValue - settings.Volume) > .0001)
            await Guard(() => SetYouTubeVolume(e.NewValue));
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
