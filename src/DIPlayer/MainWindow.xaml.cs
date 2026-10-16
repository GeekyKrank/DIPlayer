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
    readonly LibraryStore store = new();
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
        Tracks.ItemsSource = rows;
        queue.Tracks.AddRange(store.LoadQueue());
        VolumeSlider.Value = settings.Volume;
        player.Volume = (float)settings.Volume;
        timer.Tick += (_, _) => RefreshPosition();
        player.Error += message => Dispatcher.BeginInvoke(() => SetStatus(message));
        player.Ended += () => { int generation = playGeneration; Dispatcher.BeginInvoke(async () => { if (!quitting && generation == playGeneration) await Guard(() => Advance(1)); }); };
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
            SetStatus(serverOverride != null || settings.Server != null ? "Подключён сервер каталога" : "Личное тестирование • собственные ключи защищены Windows DPAPI");
        }
        catch (Exception e) { SetStatus(e is CatalogException ? e.Message : "Не удалось прочитать настройки подключения."); }
    }
    ICatalog Catalog() => catalog ?? throw new CatalogException("Откройте ⚙ и настройте сервер или импортируйте собственные ключи для теста.");
    void SetStatus(string message)
    {
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
        catch (OperationCanceledException e) when (e.InnerException is TimeoutException) { SetStatus("Сервер не ответил вовремя. Проверьте сеть и повторите."); }
        catch (OperationCanceledException) { }
        catch (Exception e) { SetStatus(e switch { CatalogException => e.Message, HttpRequestException => "Нет связи. Проверьте подключение к сети.", _ => "Не удалось выполнить действие. Попробуйте ещё раз." }); }
    }
    void SourceReady(object? sender, EventArgs e)
    {
        windows = new WindowsIntegration(this, id => Dispatcher.BeginInvoke(async () => { if (id == 0) await Guard(TogglePlayback); else if (id == 1) await Advance(1); else if (id == 2) await Advance(-1); else await player.Stop(); }), () => Dispatcher.BeginInvoke(Exit), () => { playCancel?.Cancel(); player.Pause(); SetStatus("Windows переходит в сон. После пробуждения нажмите «Повторить». "); }, () => SetStatus("Windows возобновила работу. Нажмите «Повторить» для обновления потока."), async () => { if (quitting || current == null) return; bool paused = player.Paused; double position = player.Position; await Guard(() => Play(current, position)); if (paused) player.Pause(); });
        if (!windows.AllKeysRegistered)
            SetStatus("Часть медиаклавиш занята другой программой; управление в окне доступно.");
        if (!windows.TrayAvailable)
            SetStatus("Windows не добавила значок в трей; закрытие окна завершит приложение.");
    }
    void VisibilityChanged(object? sender, EventArgs e)
    {
        if (timer == null || Status == null)
            return;
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            timer.Start();
            Status.Text = latestStatus;
            RefreshCurrent();
            RefreshPosition();
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
        if (WindowState == WindowState.Minimized && windows?.TrayAvailable != false)
            Hide();
    }
    void VisiblePropertyChanged(object sender, DependencyPropertyChangedEventArgs e) => VisibilityChanged(sender, EventArgs.Empty);
    void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!quitting)
        {
            e.Cancel = true;
            if (windows?.TrayAvailable == false)
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
        timer.Stop();
        searchCancel?.Cancel();
        metadataCancel?.Cancel();
        playCancel?.Cancel();
        importCancel?.Cancel();
        windows?.Dispose();
        if (importing != null)
            await importing;
        await player.Stop();
        player.Dispose();
        settings.Volume = VolumeSlider.Value;
        settings.Save();
        store.SaveQueue(queue.Tracks);
        store.Dispose();
        (catalog as IDisposable)?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
    async void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchBox == null)
            return;
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
        if (e.Key == Key.Enter)
        {
            searchCancel?.Cancel();
            searchCancel = new();
            query = SearchBox.Text.Trim();
            page = 0;
            cursors.Clear();
            cursors.Add(null);
            SetView("search");
            await Guard(() => LoadSearch(searchCancel.Token, ++searchGeneration));
        }
    }
    async Task LoadSearch(CancellationToken ct, int generation)
    {
        if (query.Length == 0)
            return;
        SetStatus("Поиск…");
        var result = await Catalog().Search(query, cursors[page], ct);
        ct.ThrowIfCancellationRequested();
        if (generation != searchGeneration || view != "search")
            return;
        ShowRows(result.Tracks.Select(t => new DisplayRow(t)));
        next = result.Next;
        Heading.Text = "Результаты поиска";
        SetStatus(result.Tracks.Count == 0 ? "Ничего не найдено" : "Выберите трек • доступ проверяется перед воспроизведением");
        Paging();
    }
    void SetView(string value)
    {
        view = value;
        HomePanel.Visibility = value == "home" ? Visibility.Visible : Visibility.Collapsed;
        ContentPanel.Visibility = value == "home" ? Visibility.Collapsed : Visibility.Visible;
        HomeNav.Tag = value == "home" ? "active" : null;
        SearchNav.Tag = value == "search" ? "active" : null;
        LibraryNav.Tag = value == "library" ? "active" : null;
        QueueNav.Tag = value == "queue" ? "active" : null;
        LibraryBar.Visibility = value == "library" ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Visibility = UpButton.Visibility = DownButton.Visibility = value != "search" ? Visibility.Visible : Visibility.Collapsed;
        if (value != "search")
        {
            searchCancel?.Cancel();
            searchGeneration++;
        }
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
            int selected = Tracks.SelectedIndex;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Track.Access != "unknown")
                    continue;
                var row = rows[i];
                var track = metadata.GetValueOrDefault(row.Track.Id) ?? row.Track with
                {
                    Artist = "",
                    Issue = "API не вернул трек. Возможно, он удалён или недоступен."
                };
                rows[i] = row with
                {
                    Track = track
                };
                if (row.QueueIndex >= 0)
                    queue.Tracks[row.QueueIndex] = track;
            }
            Tracks.SelectedIndex = selected;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!quitting) SetStatus(e is CatalogException ? e.Message : "Не удалось обновить метаданные. Проверьте сеть и откройте список ещё раз."); }
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
        Heading.Text = "Поиск треков";
        page = 0;
        cursors.Clear();
        cursors.Add(null);
        searchCancel = new();
        await Guard(() => LoadSearch(searchCancel.Token, ++searchGeneration));
    }
    void QueueView(object s, RoutedEventArgs e)
    {
        SetView("queue");
        page = 0;
        ShowQueue();
    }
    void ShowQueue()
    {
        Heading.Text = $"Очередь · {queue.Tracks.Count}";
        ShowRows(queue.Tracks.Skip(page * 25).Take(25).Select((t, i) => new DisplayRow(t, 0, page * 25 + i)));
        next = (page + 1) * 25 < queue.Tracks.Count ? "more" : null;
        Paging();
    }
    void LibraryView(object s, RoutedEventArgs e)
    {
        SetView("library");
        page = 0;
        RefreshPlaylists();
        ShowLibrary();
    }
    void RefreshPlaylists(string? select = null)
    {
        refreshing = true;
        var data = store.Playlists();
        PlaylistPicker.ItemsSource = data;
        playlistId = select ?? playlistId;
        PlaylistPicker.SelectedItem = data.FirstOrDefault(p => p.Id == playlistId) ?? data.FirstOrDefault();
        playlistId = (PlaylistPicker.SelectedItem as PlaylistHeader)?.Id;
        refreshing = false;
    }
    void PlaylistSelected(object s, SelectionChangedEventArgs e)
    {
        if (refreshing)
            return;
        playlistId = (PlaylistPicker.SelectedItem as PlaylistHeader)?.Id;
        page = 0;
        if (view == "library")
            ShowLibrary();
    }
    void ShowLibrary()
    {
        var header = store.Playlists().FirstOrDefault(p => p.Id == playlistId);
        Heading.Text = header == null ? "Локальные плейлисты" : header.Name + (header.Complete ? "" : " · импорт не завершён");
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
        await Guard(async () => { if (row.QueueIndex >= 0) queue.Index = row.QueueIndex; else { queue.Add(row.Track); queue.Index = queue.Tracks.Count - 1; store.SaveQueue(queue.Tracks); } await Play(row.Track); });
    }
    async Task Play(Track track, double position = 0)
    {
        playCancel?.Cancel();
        playCancel = new();
        var ct = playCancel.Token;
        int generation = ++playGeneration;
        current = track;
        RefreshCurrent();
        SetStatus("Проверка доступности и загрузка потока…");
        await player.Stop();
        await Guard(async () => { if (track.Access == "unknown") { track = await Catalog().GetTrack(track.Id, ct); ct.ThrowIfCancellationRequested(); current = track; Remember(track); RefreshCurrent(); } var url = await Catalog().Stream(track.Id, ct); ct.ThrowIfCancellationRequested(); if (generation != playGeneration) return; await player.Open(url, ct, position); windows?.Track(track.Title); SetStatus("Воспроизведение • SoundCloud"); });
    }
    async Task Advance(int direction)
    {
        var track = queue.Next(direction);
        if (track != null)
            await Play(track);
        else
            SetStatus(direction > 0 ? "Конец очереди" : "Начало очереди");
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
            _ = Guard(() => { queue.Add(row.Track); store.SaveQueue(queue.Tracks); SetStatus("Добавлено в очередь"); return Task.CompletedTask; });
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
        PlayPause.Content = player.Playing && !player.Paused ? "\uE769" : "\uE768";
        if (dragging)
            return;
        PositionSlider.Maximum = Math.Max(1, player.Duration);
        PositionSlider.Value = player.Position;
        TimeLabel.Text = Format(player.Position) + " / " + Format(player.Duration);
    }
    void RefreshCurrent()
    {
        if (IsVisible && WindowState != WindowState.Minimized && current != null)
        {
            NowTitle.Text = current.Title;
            NowArtist.Text = current.Artist;
            if (NowArtwork.Url != current.Artwork)
                NowArtwork.Url = current.Artwork;
        }
    }
    void HomeView(object s, RoutedEventArgs e) => SetView("home");
    void MinimizeClick(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void MaximizeClick(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseClick(object s, RoutedEventArgs e) => Close();
    bool? compactLayout;
    void LayoutChanged(object s, SizeChangedEventArgs e)
    {
        if (PlayerLayout == null)
            return;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        bool compact = ActualWidth < 820;
        if (compactLayout == compact)
            return;
        compactLayout = compact;
        PlayerRow.Height = new GridLength(compact ? 140 : 100);
        CompactControlsRow.Height = new GridLength(compact ? 48 : 0);
        TrackColumn.Width = compact ? new GridLength(142) : new GridLength(1, GridUnitType.Star);
        ControlsColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(160);
        ExtrasColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumnSpan(TrackInfo, compact ? 2 : 1);
        Grid.SetRow(Transport, compact ? 1 : 0);
        Grid.SetColumn(Transport, compact ? 0 : 1);
        Grid.SetRow(PlayerExtras, compact ? 1 : 0);
        Grid.SetColumn(PlayerExtras, compact ? 1 : 2);
        SourceButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ContentPanel.Margin = compact ? new Thickness(10, 6, 10, 10) : new Thickness(20, 6, 20, 16);
        ContentPanel.Padding = new Thickness(compact ? 10 : 16);
    }
    static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    void SeekStarted(object s, MouseButtonEventArgs e) => dragging = true;
    async void SeekReleased(object s, MouseButtonEventArgs e)
    {
        dragging = false;
        await Guard(() => player.Seek(PositionSlider.Value));
    }
    void VolumeChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (player != null)
            player.Volume = (float)e.NewValue;
    }
    void OpenSoundCloud(object s, RoutedEventArgs e)
    {
        if (current != null && Uri.TryCreate(current.Permalink, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "soundcloud.com")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    void OpenSource(object s, RoutedEventArgs e)
    {
        if (current != null)
            OpenSoundCloud(s, e);
        else
            Process.Start(new ProcessStartInfo("https://soundcloud.com") { UseShellExecute = true });
    }
    void CreatePlaylist(object s, RoutedEventArgs e)
    {
        string? name = Dialogs.Text(this, "Создать плейлист", "Название");
        if (string.IsNullOrWhiteSpace(name))
            return;
        playlistId = store.Create(name.Trim());
        RefreshPlaylists(playlistId);
        SetView("library");
        page = 0;
        ShowLibrary();
    }
    void RenamePlaylist(object s, RoutedEventArgs e)
    {
        if (playlistId == null)
            return;
        string? name = Dialogs.Text(this, "Переименовать", "Название", (PlaylistPicker.SelectedItem as PlaylistHeader)?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name))
            return;
        store.Rename(playlistId, name.Trim());
        RefreshPlaylists();
        ShowLibrary();
    }
    void DeletePlaylist(object s, RoutedEventArgs e)
    {
        if (playlistId == null)
            return;
        if (System.Windows.MessageBox.Show(this, "Удалить этот локальный плейлист?", "DIPlayer", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        store.Delete(playlistId);
        playlistId = null;
        RefreshPlaylists();
        ShowLibrary();
    }
    void AddToPlaylist(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        var list = store.Playlists();
        if (list.Count == 0)
        {
            SetStatus("Сначала создайте локальный плейлист.");
            return;
        }
        var selected = Dialogs.Pick(this, list);
        if (selected == null)
            return;
        store.Add(selected.Id, [row.Track]);
        SetStatus("Добавлено в «" + selected.Name + "»");
        if (view == "library")
        {
            RefreshPlaylists();
            ShowLibrary();
        }
    }
    async void ImportClick(object s, RoutedEventArgs e)
    {
        if (importCancel != null)
        {
            SetStatus("Импорт уже выполняется.");
            return;
        }
        string? url = Dialogs.Text(this, "Импорт публичного плейлиста", "https://soundcloud.com/автор/sets/плейлист");
        if (string.IsNullOrWhiteSpace(url))
            return;
        importCancel = new();
        CancelImport.Visibility = Visibility.Visible;
        importing = Guard(async () => { var progress = new InlineProgress(SetStatus); string id = await new PlaylistImporter(Catalog(), store).Import(url, progress, importCancel.Token); RefreshPlaylists(id); SetView("library"); page = 0; ShowLibrary(); SetStatus(store.Playlists().Single(p => p.Id == id).Complete ? "Импорт завершён. Копия списка сохранена на этом ПК; аудио не скачивается." : "Сохранён неполный список: SoundCloud вернул не все заявленные записи."); });
        await importing;
        importCancel.Dispose();
        importCancel = null;
        importing = null;
        CancelImport.Visibility = Visibility.Collapsed;
        RefreshPlaylists();
        if (view == "library")
            ShowLibrary();
    }
    void CancelImportClick(object s, RoutedEventArgs e)
    {
        importCancel?.Cancel();
        SetStatus("Импорт отменён. Полученная часть списка сохранена.");
    }
    void SettingsClick(object s, RoutedEventArgs e)
    {
        if (importCancel != null)
        {
            SetStatus("Сначала завершите или отмените импорт.");
            return;
        }
        if (Dialogs.Configure(this, settings))
        {
            searchCancel?.Cancel();
            Connect();
        }
    }
    sealed class InlineProgress(Action<string> update) : IProgress<string>
    {
        public void Report(string message) => update(message);
    }
}
