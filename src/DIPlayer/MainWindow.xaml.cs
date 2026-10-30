using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DIPlayer.Audio;
using DIPlayer.Core;
using DIPlayer.Import;
using DIPlayer.Queue;
using DIPlayer.Storage;
using DIPlayer.SystemIntegration;

namespace DIPlayer;

public sealed record DisplayRow(Track Track, long EntryId = 0, int QueueIndex = -1);
public partial class MainWindow : Window
{
    readonly LibraryStore store = new(DiagnosticLibraryDirectory());
    readonly Settings settings = Settings.Load();
    readonly PlaybackQueue queue = new();
    readonly HlsPlayer player = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly ObservableCollection<DisplayRow> rows = [];
    ICatalog? catalog;
    string? serverOverride;
    WindowsIntegration? windows;
    CancellationTokenSource? searchCancel, playCancel, importCancel;
    Task? importing;
    CancellationTokenSource? metadataCancel;
    readonly Dictionary<string, Track> metadata = [];
    readonly System.Collections.Generic.Queue<string> metadataOrder = new();
    readonly List<string?> cursors = [null];
    string view = "home", query = "", latestStatus = "";
    int page, searchGeneration, playGeneration;
    string? next, playlistId;
    Track? current;
    bool quitting, dragging, refreshing, startupInitialized;
    public MainWindow()
    {
        InitializeComponent();
        InitializePreferences();
        InitializeSearchExperience();
        InitializeEqualizer();
        SyncPlaybackModes();
        LoadBrandLogo();
        InitializeDiscord();
        WindowFrame.SizeChanged += (_, _) => UpdateWindowCorners();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(UpdateWindowCorners);
        Tracks.ItemsSource = rows;
        queue.Tracks.AddRange(store.LoadQueue());
        settings.Volume = double.IsFinite(settings.Volume) ? Math.Clamp(settings.Volume, 0, 1) : .6;
        VolumeSlider.Value = YouTubeVolume.Value = settings.Volume;
        player.Volume = (float)settings.Volume;
        volumeSaveTimer.Tick += (_, _) =>
        {
            volumeSaveTimer.Stop();
            if (DiagnosticLibraryDirectory() == null) _ = Guard(() => { settings.Save(); return Task.CompletedTask; });
        };
        timer.Tick += (_, _) => RefreshPosition();
        player.Error += message => Dispatcher.BeginInvoke(() => SetStatus(message));
        player.Ended += () => { int generation = playGeneration; Dispatcher.BeginInvoke(async () => { if (!quitting && !browserMode && generation == playGeneration) await Guard(() => Advance(1, true)); }); };
        Loaded += async (_, _) =>
        {
            // Loaded can fire again after a hidden WPF window is attached to its presentation source.
            if (startupInitialized)
                return;
            startupInitialized = true;
            string[] args = Environment.GetCommandLineArgs();
            int ci = Array.IndexOf(args, "--credentials");
            if (ci >= 0 && ci + 1 < args.Length)
                settings.ImportCredentials(args[ci + 1]);
            if (args.Contains("--use-default-server"))
            {
                settings.Server = Settings.DefaultServer;
                settings.Save();
            }
            int si = Array.IndexOf(args, "--server");
            if (si >= 0 && si + 1 < args.Length)
                serverOverride = args[si + 1];
            Connect();
            RefreshPlaylists();
            if (!args.Contains("--selftest")) LibraryView(this, new RoutedEventArgs());
            if (!args.Contains("--selftest")) _ = CheckForUpdates();
            if (!args.Contains("--selftest") && settings.YouTubeBrowserMode) await ApplyBrowserMode(true);
            int qi = Array.IndexOf(args, "--search");
            if (qi >= 0 && qi + 1 < args.Length)
                SearchBox.Text = args[qi + 1];
            if (args.Contains("--selftest"))
                await RunSelfTest(args);
            await Task.CompletedTask;
        };
    }
    void Connect()
    {
        (catalog as IDisposable)?.Dispose();
        catalog = null;
        try
        {
            catalog = serverOverride != null ? new RemoteCatalog(serverOverride) : settings.CreateCatalog();
            SetStatus(serverOverride != null || settings.Server != null ? "Catalog server connected" : "Private testing • your keys are protected by Windows DPAPI");
        }
        catch (Exception e) { SetStatus(e is CatalogException ? e.Message : "Could not read connection settings."); }
    }
    ICatalog Catalog() => catalog ?? throw new CatalogException("Could not initialize the catalog. Restart the application.");
    void SetStatus(string message)
    {
        if (message == "Catalog server connected") message = "";
        latestStatus = message;
        if (IsVisible && WindowState != WindowState.Minimized)
            Status.Text = message;
    }
    async Task Guard(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException e) when (e.InnerException is TimeoutException) { SetStatus("The server timed out. Check your network and try again."); }
        catch (OperationCanceledException) { }
        catch (Exception e) { SetStatus(e switch { CatalogException => e.Message, HttpRequestException => "No connection. Check your network.", _ => "Could not complete the action. Please try again." }); }
    }
    void SourceReady(object? sender, EventArgs e)
    {
        UpdateWindowCorners();
        SyncTrackBackdrop();
        windows = new WindowsIntegration(this, id => Dispatcher.BeginInvoke(async () => { if (browserMode) { await Guard(() => BrowserMedia(id)); return; } if (id == 0) await Guard(TogglePlayback); else if (id == 1) await Advance(1); else if (id == 2) await Advance(-1); else await player.Stop(); }), () => Dispatcher.BeginInvoke(Exit), () => { playCancel?.Cancel(); player.Pause(); SetStatus("Windows is going to sleep. Press Retry after waking. "); }, () => SetStatus("Windows has resumed. Press Retry to reconnect the stream."), async () => { if (quitting || browserMode || current == null) return; bool paused = player.Paused; double position = player.Position; await Guard(() => Play(current, position)); if (paused) player.Pause(); });
        if (!windows.AllKeysRegistered)
            SetStatus("Some media keys are used by another app. Use the player controls.");
        if (!windows.TrayAvailable)
            SetStatus("The tray icon is unavailable. Closing the window will quit the app.");
    }
    void VisibilityChanged(object? sender, EventArgs e)
    {
        if (timer == null || Status == null)
            return;
        UpdateWindowCorners();
        SyncTrackBackdrop();
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            miniPlayer?.Hide();
            timer.Start();
            Status.Text = latestStatus;
            RefreshCurrent();
            RefreshPosition();
            SyncImportView();
            RefreshImportedLibrary();
            if (!browserMode && view == "library" && libraryOverview) ShowLibraryOverview();
            if (rows.Any(r => r.Track.Access == "unknown"))
                _ = HydrateRows();
        }
        else
        {
            timer.Stop();
            searchCancel?.Cancel();
            metadataCancel?.Cancel();
                searchGeneration++;
        }
    }
    void VisiblePropertyChanged(object sender, DependencyPropertyChangedEventArgs e) { if(startupInitialized) LogWindowTransition("Visibility changed"); VisibilityChanged(sender, EventArgs.Empty); }
    bool requestedTrayClose;
    void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!quitting)
        {
            e.Cancel = true;
            if (!requestedTrayClose) { LogWindowTransition("Ignored unsolicited close"); return; }
            requestedTrayClose = false;
            LogWindowTransition("User close to tray");
            if (!settings.CloseToTray || windows?.TrayAvailable == false)
                Exit();
            else
                Hide();
        }
    }
    async void Exit()
    {
        if (quitting)
            return;
        quitting = true;
        volumeSaveTimer.Stop();
        equalizerApplyTimer.Stop();
        equalizerSaveTimer.Stop();
        discordTimer.Stop();
        discordPresence.Dispose();
        browserGeneration++;
        DisposeMusicBrowser();
        navigationCancel?.Cancel();
        libraryArtworkCancel?.Cancel();
        miniPlayer?.Close();
        timer.Stop();
        searchCancel?.Cancel();
        metadataCancel?.Cancel();
        playCancel?.Cancel();
        importCancel?.Cancel();
        accountCancel?.Cancel();
        windows?.Dispose();
        if (importing != null)
            await importing;
        if (accountTask != null)
            await accountTask;
        await player.Stop();
        player.Dispose();
        settings.Volume = VolumeSlider.Value;
        if (DiagnosticLibraryDirectory() == null) settings.Save();
        store.SaveQueue(queue.Tracks);
        store.Dispose();
        (catalog as IDisposable)?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
    async void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchBox == null||suppressSearchTextChange||browserMode)
            return;
        searchExplicitCommit=false;
        QueueSearchSuggestions();
        searchCancel?.Cancel();
        int generation = ++searchGeneration;
        searchCancel = new();
        var ct = searchCancel.Token;
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
            return;
        await Guard(async () => { await Task.Delay(400, ct); query = SearchBox.Text.Trim(); page = 0; cursors.Clear(); cursors.Add(null); next = null; rows.Clear(); Paging(); SetView("search"); await LoadSearch(ct, generation); });
    }
    async void SearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if(HandleSearchSuggestionKey(e))return;
        if (e.Key == Key.Enter)
        {
            e.Handled=true;
            await Guard(()=>CommitSoundCloudSearch(SearchBox.Text));
        }
    }
    async Task LoadSearch(CancellationToken ct, int generation)
    {
        if (query.Length == 0)
            return;
        SetStatus("Searching…");
        var result = await Catalog().Search(query, cursors[page], ct);
        ct.ThrowIfCancellationRequested();
        if (generation != searchGeneration || view != "search")
            return;
        ShowRows(result.Tracks.Select(t => new DisplayRow(t)));
        next = result.Next;
        Heading.Text = "Search results";
        SetStatus(result.Tracks.Count == 0 ? "No results found" : "");
        Paging();
        RefreshSearchSuggestionsFromSoundCloud(query,result.Tracks);
    }
    void SetView(string value)
    {
        ClosePlaybackPanelsForNavigation();
        CloseActionPanel(false);
        SettingsPanel.Visibility = Visibility.Collapsed;
        if (view == "queue" && value != "queue") queueReturnView = null;
        view = value;
        HomePanel.Visibility = value == "home" ? Visibility.Visible : Visibility.Collapsed;
        ContentPanel.Visibility = value is "search" or "library" or "queue" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        if (value != "library") { libraryArtworkCancel?.Cancel(); PlaylistSummary.Visibility = Visibility.Collapsed; PlaylistSummaryColumn.Width = new GridLength(0); }
        ImportPanel.Visibility = value == "import" ? Visibility.Visible : Visibility.Collapsed;
        HomeNav.Tag = value is "home" or "library" ? "active" : null;
        SearchNav.Tag = value == "search" ? "active" : null;
        QueueNav.Tag = value == "queue" ? "active" : null;
        QueueToggle.Background = value == "queue" ? System.Windows.Media.Brushes.DimGray : System.Windows.Media.Brushes.Transparent;
        QueueToggle.ToolTip = value == "queue" ? "Close queue" : "Queue";
        ClearQueueButton.Visibility = value == "queue" ? Visibility.Visible : Visibility.Collapsed;
        ImportNav.Tag = value == "import" ? "active" : null;
        LibraryBar.Visibility = value == "library" ? Visibility.Visible : Visibility.Collapsed;

        if (value == "import") SyncImportView();
        if (value != "search")
        {
            searchCancel?.Cancel();
            searchGeneration++;
        }
        if (value is "home" or "import") metadataCancel?.Cancel();
        UpdatePlaylistLayout();
    }
    void ShowRows(IEnumerable<DisplayRow> data)
    {
        metadataCancel?.Cancel();
        rows.Clear();
        foreach (var row in data)
        {
            if (row.Track.Access != "unknown")
                Remember(row.Track);
            rows.Add(row);
        }
        if (IsVisible && rows.Any(r => r.Track.Access == "unknown"))
            _ = HydrateRows();
    }
    void Remember(Track track)
    {
        if (!metadata.ContainsKey(track.Id))
            metadataOrder.Enqueue(track.Id);
        metadata[track.Id] = track;
        while (metadata.Count > 128)
            metadata.Remove(metadataOrder.Dequeue());
    }
    async Task HydrateRows()
    {
        metadataCancel?.Cancel();
        metadataCancel = new();
        var ct = metadataCancel.Token;
        var snapshot = rows.ToArray();
        try
        {
            var missing = snapshot.Where(r => r.Track.Access == "unknown" && !metadata.ContainsKey(r.Track.Id)).Select(r => r.Track.Id).Distinct().ToArray();
            if (missing.Length > 0)
            foreach (var t in await Catalog().GetTracks(missing, ct))
                Remember(t);
            ct.ThrowIfCancellationRequested();
            if (rows.Count != snapshot.Length || !rows.SequenceEqual(snapshot))
                return;
            if (view == "library" && !libraryOverview)
            {
                var absent = snapshot.Where(r => r.EntryId > 0 && r.Track.Access == "unknown" &&
                    (!metadata.TryGetValue(r.Track.Id, out var details) || details.Issue == "Track details unavailable")).ToArray();
                if (absent.Length > 0)
                {
                    foreach (var row in absent) store.Hide(row.EntryId);
                    ShowLibrary();
                    return;
                }
            }
            int selected = Tracks.SelectedIndex;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Track.Access != "unknown")
                    continue;
                var row = rows[i];
                var track = metadata.GetValueOrDefault(row.Track.Id) ?? row.Track with
                {
                    Artist = "",
                    Issue = "The API did not return this track. It may have been removed or become unavailable."
                };
                rows[i] = row with
                {
                    Track = track
                };
                if (row.QueueIndex >= 0)
                    queue.Tracks[row.QueueIndex] = track;
            }
            Tracks.SelectedIndex = selected;
            if (view == "library" && !libraryOverview) UpdatePlaylistSummary(store.Playlists().FirstOrDefault(p => p.Id == playlistId));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!quitting) SetStatus(e is CatalogException ? e.Message : "Could not refresh track details. Check your network and reopen the list."); }
    }
    void Paging()
    {
        PageLabel.Text = (page + 1).ToString();
        PrevPage.IsEnabled = page > 0;
        NextPage.IsEnabled = next != null;
    }
    async void SearchView(object s, RoutedEventArgs e)
    {
        SetView("search");
        SearchBox.Focus();
        Heading.Text = "Search tracks";
        page = 0;
        cursors.Clear();
        cursors.Add(null);
        searchCancel = new();
        await Guard(() => LoadSearch(searchCancel.Token, ++searchGeneration));
    }
    void QueueView(object s, RoutedEventArgs e)
    {
        if (view == "queue") return;
        queueReturnView = CaptureView();
        SetView("queue");
        page = 0;
        ShowQueue();
    }
    void ShowQueue()
    {
        ClearQueueButton.IsEnabled = queue.Tracks.Count > 0;
        Heading.Text = $"Queue · {queue.Tracks.Count}";
        ShowRows(queue.Tracks.Skip(page * 25).Take(25).Select((t, i) => new DisplayRow(t, 0, page * 25 + i)));
        next = (page + 1) * 25 < queue.Tracks.Count ? "more" : null;
        Paging();
    }
    async void ClearQueue(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        store.SaveQueue(Array.Empty<Track>());
        queue.Tracks.Clear();
        queue.Index = -1;
        page = 0;
        ShowQueue();
        SetStatus("");
        return Task.CompletedTask;
    });
    void LibraryView(object s, RoutedEventArgs e)
    {
        SetView("library");
        page = 0;
        RefreshPlaylists();
        ShowLibraryOverview();
    }
    void RefreshPlaylists(string? select = null)
    {
        refreshing = true;
        var data = store.Playlists();
        PlaylistPicker.ItemsSource = data;
        playlistId = select ?? playlistId;
        PlaylistPicker.SelectedItem = data.FirstOrDefault(p => p.Id == playlistId) ?? data.FirstOrDefault();
        playlistId = (PlaylistPicker.SelectedItem as PlaylistHeader)?.Id;
        RenamePlaylistButton.IsEnabled = DeletePlaylistButton.IsEnabled = playlistId != null;
        refreshing = false;
    }
    void PlaylistSelected(object s, SelectionChangedEventArgs e)
    {
        if (refreshing)
            return;
        playlistId = (PlaylistPicker.SelectedItem as PlaylistHeader)?.Id;
        RenamePlaylistButton.IsEnabled = DeletePlaylistButton.IsEnabled = playlistId != null;
        page = 0;
        if (view == "library")
            ShowLibrary();
    }
    void ShowLibrary()
    {
        libraryOverview = false;
        LibraryPanel.Visibility = Visibility.Collapsed;
        ContentPanel.Visibility = Visibility.Visible;
        UpdatePlaylistLayout();
        var header = store.Playlists().FirstOrDefault(p => p.Id == playlistId);
        UpdatePlaylistSummary(header);
        Heading.Text = header == null ? "Local playlists" : header.Name + (header.Complete ? "" : " · import incomplete");
        if (header == null)
        {
            rows.Clear();
            next = null;
        }
        else
        {
            page = Math.Min(page, Math.Max(0, (header.Count - 1) / 25));
            ShowRows(store.Page(header.Id, page).Select(r => new DisplayRow(r.Track, r.EntryId)));
            next = (page + 1) * 25 < header.Count ? "more" : null;
        }
        Paging();
    }
    async void FollowingPage(object s, RoutedEventArgs e)
    {
        if (next == null)
            return;
        if (view == "search")
        {
            if (cursors.Count <= page + 1)
                cursors.Add(next);
            await Guard(() => NavigateSearch(page + 1));
        }
        else
        {
            page++;
            if (view == "queue")
                ShowQueue();
            else
                ShowLibrary();
        }
    }
    async void PreviousPage(object s, RoutedEventArgs e)
    {
        if (page == 0)
            return;
        if (view == "search")
            await Guard(() => NavigateSearch(page - 1));
        else
        {
            page--;
            if (view == "queue")
                ShowQueue();
            else
                ShowLibrary();
        }
    }
    async Task NavigateSearch(int target)
    {
        int previous = page;
        page = target;
        searchCancel?.Cancel();
        searchCancel = new();
        int generation = ++searchGeneration;
        try
        {
            await LoadSearch(searchCancel.Token, generation);
        }
        catch { if (generation == searchGeneration) { page = previous; Paging(); } throw; }
    }
    DisplayRow? Selected => Tracks.SelectedItem as DisplayRow;
    async void PlaySelected(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        await Guard(() => PlayRow(row));
    }
    async Task PlayRow(DisplayRow row)
    {
        if (row.QueueIndex >= 0) queue.Index = row.QueueIndex;
        else SeedPlaybackQueue(row);
        if (current?.Id == row.Track.Id && player.Duration > 0 && !player.Faulted)
        {
            if (player.Paused) player.Toggle();
            if (player.Playing) { RefreshPosition(); return; }
        }
        await Play(row.Track);
    }
    void SeedPlaybackQueue(DisplayRow selected)
    {
        var context = rows.ToList();
        if (view == "library" && playlistId != null && selected.EntryId > 0)
        {
            // Read references in pages, retaining at most the existing 1,000-track queue limit.
            int firstPage = page / 40 * 40;
            context.Clear();
            for (int index = firstPage; index < firstPage + 40; index++)
            {
                var entries = store.Page(playlistId, index);
                context.AddRange(entries.Select(r => new DisplayRow(metadata.GetValueOrDefault(r.Track.Id) ?? r.Track, r.EntryId)));
                if (entries.Count < 25) break;
            }
        }
        int target = selected.EntryId > 0 ? context.FindIndex(r => r.EntryId == selected.EntryId) : context.FindIndex(r => ReferenceEquals(r, selected));
        if (target < 0) { context = [selected]; target = 0; }
        store.SaveQueue(context.Select(r => r.Track));
        queue.Tracks.Clear(); queue.Tracks.AddRange(context.Select(r => r.Track)); queue.Index = target;
        SyncMiniPlayer();
    }
    async Task Play(Track track, double position = 0, int direction = 1)
    {
        navigationCancel?.Cancel();
        navigationCancel = new();
        var ct = navigationCancel.Token;
        bool first = true;
        bool played = await queue.PlayAvailable(track, direction, settings.RepeatMode == 1, settings.Shuffle, candidate =>
        {
            double start = first ? position : 0;
            first = false;
            return TryPlayCandidate(candidate, start);
        }, ct);
        if (!played && !ct.IsCancellationRequested) SetStatus("No more playable tracks in the queue.");
    }
    CancellationTokenSource? navigationCancel;
    async Task<bool> TryPlayCandidate(Track track, double position)
    {
        playCancel?.Cancel();
        playCancel = new();
        var ct = playCancel.Token;
        int generation = ++playGeneration;
        current = track;
        RefreshCurrent();
        SetStatus("Checking availability and loading stream…");
        await player.Stop();
        try
        {
            if (track.Access == "unknown") track = metadata.GetValueOrDefault(track.Id) ?? await Catalog().GetTrack(track.Id, ct);
            ct.ThrowIfCancellationRequested();
            if (track.Access is "blocked" or "preview" || track.Issue == "Track details unavailable")
            {
                MarkUnavailable(track);
                return false;
            }
            var url = await Catalog().Stream(track.Id, ct);
            ct.ThrowIfCancellationRequested();
            if (generation != playGeneration) throw new OperationCanceledException(ct);
            current = track; Remember(track); RefreshCurrent();
            await player.Open(url, ct, position);
            windows?.Track(track.Title); SetStatus("");
            return true;
        }
        catch (CatalogException error) when (error.Status is 403 or 404 or 410 || error.Message == "This track is unavailable outside SoundCloud; no reason was provided." || error.Message == "This track has no supported MP3 HLS stream. AAC is not supported yet.")
        {
            ct.ThrowIfCancellationRequested();
            MarkUnavailable(track with { Access = "blocked", Issue = "Unavailable in DIPlayer" });
            return false;
        }
    }
    void MarkUnavailable(Track track)
    {
        track = track with { Issue = track.Access == "preview" ? "Full track unavailable in DIPlayer · preview only" : "Unavailable in DIPlayer" };
        Remember(track);
        for (int i = 0; i < queue.Tracks.Count; i++) if (queue.Tracks[i].Id == track.Id) queue.Tracks[i] = track;
        for (int i = 0; i < rows.Count; i++) if (rows[i].Track.Id == track.Id) rows[i] = rows[i] with { Track = track };
    }
    async Task Advance(int direction, bool automatic = false)
    {
        var track = automatic && settings.RepeatMode == 2 && current != null ? current : queue.Navigate(direction, settings.RepeatMode == 1, settings.Shuffle);
        if (track != null)
            await Guard(() => Play(track, 0, direction));
        else
            SetStatus(direction > 0 ? "End of queue" : "Start of queue");
    }
    async void NextTrack(object s, RoutedEventArgs e) => await Guard(() => Advance(1));
    async void PreviousTrack(object s, RoutedEventArgs e) => await Guard(() => Advance(-1));
    async void ToggleClick(object s, RoutedEventArgs e) => await Guard(TogglePlayback);
    async Task TogglePlayback()
    {
        if (current != null && (player.Duration <= 0 || player.Faulted || (!player.Playing && player.Position >= player.Duration - .5)))
            await Play(current, player.Faulted ? player.Position : 0);
        else
            player.Toggle();
    }
    async void RetryTrack(object s, RoutedEventArgs e)
    {
        if (current != null)
        {
            double position = player.Position;
            await Guard(() => Play(current, position));
        }
    }
    void EnqueueSelected(object s, RoutedEventArgs e)
    {
        if (Selected is { } row)
            _ = Guard(() => { queue.Add(row.Track); store.SaveQueue(queue.Tracks); SetStatus("Added to queue"); return Task.CompletedTask; });
    }
    void RemoveSelected(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        if (view == "queue")
        {
            queue.Remove(row.QueueIndex);
            store.SaveQueue(queue.Tracks);
            ShowQueue();
        }
        else if (view == "library")
        {
            store.Remove(row.EntryId);
            RefreshPlaylists();
            ShowLibrary();
        }
    }
    void Move(int direction)
    {
        if (Selected is not { } row)
            return;
        if (view == "queue")
        {
            queue.Move(row.QueueIndex, direction);
            store.SaveQueue(queue.Tracks);
            ShowQueue();
        }
        else if (view == "library")
        {
            store.Move(row.EntryId, direction);
            ShowLibrary();
        }
    }
    void MoveUp(object s, RoutedEventArgs e) => Move(-1); void MoveDown(object s, RoutedEventArgs e) => Move(1);
    void RefreshPosition()
    {
        PlayPause.Content = player.Playing && !player.Paused ? UI.Symbols.Pause : UI.Symbols.Play;
        if (dragging)
            return;
        PositionSlider.Maximum = Math.Max(1, player.Duration);
        PositionSlider.Value = player.Position;
        TimeLabel.Text = Format(player.Position) + " / " + Format(player.Duration);
    }
    void RefreshCurrent()
    {
        SyncMiniPlayer();
        if (IsVisible && WindowState != WindowState.Minimized && current != null)
        {
            NowTitle.Text = current.Title;
            NowArtist.Text = current.Artist;
            if (NowArtwork.Url != current.Artwork)
                NowArtwork.Url = current.Artwork;
        }
    }
    void HomeView(object s, RoutedEventArgs e)
    {
        if (browserMode) { SettingsPanel.Visibility = Visibility.Collapsed; ShowYouTubeNative(); YouTubeHomeClick(s,e); }
        else LibraryView(s,e);
    }
    void MinimizeClick(object s, RoutedEventArgs e) { LogWindowTransition("User minimize"); WindowState = WindowState.Minimized; if (settings.MiniPlayerEnabled) { if (windows?.TrayAvailable != false) Hide(); ShowMiniPlayer(); } }
    void MaximizeClick(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseClick(object s, RoutedEventArgs e) { requestedTrayClose = true; Close(); }
    bool? compactLayout;
    void LayoutChanged(object s, SizeChangedEventArgs e)
    {
        if (PlayerLayout == null)
            return;
        UpdateWindowCorners();
        MaximizeButton.Content = WindowState == WindowState.Maximized ? UI.Symbols.Restore : UI.Symbols.Maximize;
        bool compact = ActualWidth < 820;
        UpdatePlaylistLayout();
        if (compactLayout == compact)
            return;
        compactLayout = compact;
        PlayerRow.Height = new GridLength(compact ? 112 : 76);
        CompactControlsRow.Height = new GridLength(compact ? 40 : 0);
        TrackColumn.Width = new GridLength(1, GridUnitType.Star);
        ControlsColumn.Width = new GridLength(120);
        ExtrasColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumnSpan(TrackInfo, 1);
        Grid.SetRow(Transport, 0);
        Grid.SetColumn(Transport, 1);
        Grid.SetRow(PlayerExtras, compact ? 1 : 0);
        Grid.SetColumn(PlayerExtras, compact ? 0 : 2);
        Grid.SetColumnSpan(PlayerExtras, compact ? 3 : 1);
        ContentPanel.Margin = compact ? new Thickness(10, 6, 10, 10) : new Thickness(20, 6, 20, 16);
        ContentPanel.Padding = new Thickness(compact ? 10 : 16);
        ImportPanel.Margin = ContentPanel.Margin;
        ImportPanel.Padding = new Thickness(compact ? 14 : 24);
    }
    static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    void SeekStarted(object s, MouseButtonEventArgs e)
    {
        if (player.Duration <= 0) return;
        e.Handled = true;
        dragging = true;
        PositionSlider.Focus();
        PositionSlider.CaptureMouse();
        UpdateSeekPointer(e.GetPosition(PositionSlider).X);
    }
    void UpdateSeekPointer(double x)
    {
        double fraction = Math.Clamp((x - 9) / Math.Max(1, PositionSlider.ActualWidth - 18), 0, 1);
        PositionSlider.Value = fraction * PositionSlider.Maximum;
        TimeLabel.Text = Format(PositionSlider.Value) + " / " + Format(player.Duration);
    }
    void SeekMoved(object s, MouseEventArgs e)
    {
        if (dragging && PositionSlider.IsMouseCaptured) { e.Handled = true; UpdateSeekPointer(e.GetPosition(PositionSlider).X); }
    }
    void SeekCaptureLost(object s, MouseEventArgs e) { dragging = false; }
    async void SeekReleased(object s, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        e.Handled = true;
        UpdateSeekPointer(e.GetPosition(PositionSlider).X);
        double position = PositionSlider.Value;
        dragging = false;
        PositionSlider.ReleaseMouseCapture();
        await Guard(() => player.Seek(position));
    }
    void VolumeChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (player != null)
            player.Volume = (float)e.NewValue;
        if (startupInitialized) RememberVolume(e.NewValue);
    }
    void OpenSoundCloud(object s, RoutedEventArgs e)
    {
        if (current != null && Uri.TryCreate(current.Permalink, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "soundcloud.com")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    sealed class InlineProgress(Action<string> update) : IProgress<string>
    {
        public void Report(string message) => update(message);
    }
}
