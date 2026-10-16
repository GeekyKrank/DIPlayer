using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DIPlayer.Core;
using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace DIPlayer;

public partial class MainWindow
{
    static string? DiagnosticLibraryDirectory()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--test-library");
        return args.Contains("--selftest") && index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
    }
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region, int x, int y);
    // Explicit diagnostic mode; no polling/screenshot capture runs in normal use.
    async Task RunSelfTest(string[] args)
    {
        int outputIndex = Array.IndexOf(args, "--evidence");
        string directory = outputIndex >= 0 ? Path.GetFullPath(args[outputIndex + 1]) : Path.Combine(Environment.CurrentDirectory, "evidence");
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        var samples = new List<object>();
        int index = Array.IndexOf(args, "--seconds");
        int seconds = index >= 0 ? int.Parse(args[index + 1]) : 90;
        try
        {
            if (!args.Contains("--no-capture"))
            {
                SetView("home");
                UpdateLayout();
                Capture(Path.Combine(directory, "home.png"));
                if (args.Contains("--check-corners")) VerifyRoundedRegion();
                Width = 460;
                Height = 680;
                await Task.Delay(200);
                UpdateLayout();
                Capture(Path.Combine(directory, "home-narrow.png"));
                if (args.Contains("--check-corners")) VerifyRoundedRegion();
                Width = 1200;
                Height = 780;
            }
            if (args.Contains("--visual-only"))
            {
                if (args.Contains("--check-volume"))
                {
                    double previous = VolumeSlider.Value;
                    VolumeSlider.ApplyTemplate();
                    UpdateLayout();
                    var track = (System.Windows.Controls.Primitives.Track)VolumeSlider.Template.FindName("PART_Track", VolumeSlider);
                    foreach (double fraction in new[] { 0.0, .12, .25, .43, .5, .72, .88, 1.0, .31 })
                    {
                        UpdateVolumePointer(7 + fraction * (VolumeSlider.ActualWidth - 14));
                        CheckInteraction(Math.Abs(VolumeSlider.Value - fraction) < .001 && Math.Abs(player.Volume - fraction) < .001 && Math.Abs(track.Value - fraction) < .001, "Volume pointer, audio output and thumb must match");
                    }
                    UpdateVolumePointer(-100); CheckInteraction(VolumeSlider.Value == 0, "Volume lower bound failed");
                    UpdateVolumePointer(10000); CheckInteraction(VolumeSlider.Value == 1, "Volume upper bound failed");
                    VolumeSlider.Value = previous;
                    await File.WriteAllTextAsync(Path.Combine(directory, "volume-check.txt"), "PASS nine arbitrary pointer positions match slider, audio-engine volume and visual track; both out-of-bounds positions clamp correctly.");
                }
                if (args.Contains("--check-mini-player")) await VerifyMiniPlayer(directory);
                if (args.Contains("--check-track-menu"))
                {
                    var menu = BuildTrackMenu(new System.Windows.Controls.ListBoxItem());
                    if (menu.Items.Count != 2) throw new Exception("Track menu must contain only two add actions");
                    menu.PlacementTarget = SearchBox;
                    menu.IsOpen = true;
                    await Task.Delay(200);
                    menu.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(menu);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(directory, "track-menu.png"))) encoder.Save(file);
                    menu.IsOpen = false;
                    await File.WriteAllTextAsync(Path.Combine(directory, "track-menu-check.txt"), "PASS two add actions only; custom dark popup and item templates render without standard icon gutter.");
                }
                if (args.Contains("--check-inline-actions")) await VerifyInlineActions(directory);
                if (args.Contains("--check-playlist-cover")) await VerifyPlaylistCover(directory);
                if (args.Contains("--check-library-cards")) await VerifyLibraryCards(directory);
                if (args.Contains("--check-single-click")) await VerifySingleClick(directory);
                if (args.Contains("--check-import-view"))
                    await VerifyInlineImport(directory);
                if (args.Contains("--check-icon"))
                {
                    if (windows?.TrayUsesAppIcon != true || windows.TrayAvailable != true || Icon == null)
                        throw new Exception("Custom window or tray icon was not loaded");
                    await File.WriteAllTextAsync(Path.Combine(directory, "icon-check.txt"), "PASS embedded application icon extracted; tray registered with application icon; window icon loaded");
                }
                if (args.Contains("--check-activation"))
                    await VerifyTrayActivation(directory);
                if (args.Contains("--check-brand-font"))
                {
                    var typeface = new Typeface(BrandTitle.FontFamily, BrandTitle.FontStyle, BrandTitle.FontWeight, BrandTitle.FontStretch);
                    if (!typeface.TryGetGlyphTypeface(out var glyphs) || !glyphs.FamilyNames.Values.Contains("Dirty Stains"))
                        throw new Exception("Dirty Stains was not resolved; a fallback font would be displayed");
                    await File.WriteAllTextAsync(Path.Combine(directory, "brand-font-check.txt"), "PASS Dirty Stains resolves to the actual local font; title size=" + BrandTitle.FontSize);
                }
                if (args.Contains("--check-corners"))
                {
                    WindowState = WindowState.Maximized;
                    await Task.Delay(250);
                    UpdateLayout();
                    if (WindowFrame.CornerRadius.TopLeft != 0 || WindowFrame.Clip != null)
                        throw new Exception("Maximized frame must fill the screen without rounded clipping");
                    Capture(Path.Combine(directory, "maximized.png"));
                    WindowState = WindowState.Normal;
                    await Task.Delay(250);
                    UpdateLayout();
                    VerifyRoundedRegion();
                    Capture(Path.Combine(directory, "restored.png"));
                    await File.WriteAllTextAsync(Path.Combine(directory, "window-shape-checks.txt"), "PASS native rounded region: normal, narrow, restored; PASS maximized frame has no rounded clipping");
                }
                Exit();
                return;
            }
            int queryIndex = Array.IndexOf(args, "--query");
            string testQuery = queryIndex >= 0 ? args[queryIndex + 1] : "ambient";
            var found = await Catalog().Search(testQuery, null, default);
            checks.Add("Official search returned " + found.Tracks.Count + " tracks");
            var playable = seconds >= 3600 ? found.Tracks.Where(t => t.Access == "playable").OrderByDescending(t => t.DurationMs).First() : found.Tracks.First(t => t.Access == "playable");
            checks.Add("Track: " + playable.Title + "; duration ms=" + playable.DurationMs);
            query = "ambient";
            SetView("search");
            ShowRows(found.Tracks.Select(t => new DisplayRow(t)));
            Heading.Text = "Search results";
            next = found.Next;
            Paging();
            queue.Tracks.Clear();
            queue.Index = 0;
            queue.Add(playable);
            await Play(playable);
            await Task.Delay(3500);
            if (!player.Playing || player.Position < .2)
                throw new Exception("Playback did not advance: " + latestStatus);
            checks.Add("Real MP3 HLS playback advances");
            checks.Add("Tray registered=" + windows?.TrayAvailable);
            if (!args.Contains("--no-capture"))
            {
                UpdateLayout();
                Capture(Path.Combine(directory, "desktop.png"));
                Width = 460;
                Height = 680;
                await Task.Delay(300);
                UpdateLayout();
                Capture(Path.Combine(directory, "narrow.png"));
                if (Tracks.ItemContainerGenerator.ContainerFromIndex(0) is System.Windows.FrameworkElement item)
                {
                    checks.Add($"Narrow list width={Tracks.ActualWidth}; item width={item.ActualWidth}");
                    void Inspect(System.Windows.DependencyObject node)
                    {
                        if (node is System.Windows.Controls.TextBlock text && text.Text == found.Tracks[0].Duration)
                            checks.Add($"Duration width={text.ActualWidth}; x={text.TranslatePoint(new Point(0, 0), Tracks).X}");
                        for (int child = 0; child < VisualTreeHelper.GetChildrenCount(node); child++)
                            Inspect(VisualTreeHelper.GetChild(node, child));
                    }
                    Inspect(item);
                }
                Width = 1200;
                Height = 780;
            }
            if (args.Contains("--library-test"))
            {
                string testId = store.Create("Диагностика · 1001 трек");
                try
                {
                    store.Add(testId, Enumerable.Range(0, 1001).Select(i => playable with { Title = "Проверка строки " + i }));
                    RefreshPlaylists(testId);
                    SetView("library");
                    page = 0;
                    ShowLibrary();
                    await Task.Delay(300);
                    UpdateLayout();
                    if (rows.Count != 25)
                        throw new Exception("Library page size incorrect");
                    Tracks.ScrollIntoView(rows[^1]);
                    await Task.Delay(300);
                    UpdateLayout();
                    Capture(Path.Combine(directory, "library.png"));
                    page = 40;
                    ShowLibrary();
                    if (rows.Count != 1)
                        throw new Exception("Last library page incorrect");
                    checks.Add("1001-track library scrolling and last page checked; virtualized=" + System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(Tracks));
                }
                finally { store.Delete(testId); playlistId = null; RefreshPlaylists(); SetView("search"); page = 0; ShowRows(found.Tracks.Select(t => new DisplayRow(t))); }
            }
            var proc = Process.GetCurrentProcess();
            var cpu = proc.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            double beforePause = 0, afterPause = 0;
            int hiddenTicks = 0;
            bool hidden = false;
            for (int i = 0; i < seconds; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "selftest-progress.txt"), $"Second {i}; position={player.Position:F1}; hidden={hidden}; timer={timer.IsEnabled}");
                if (i == 5)
                {
                    await player.Seek(Math.Min(60, player.Duration / 2));
                    checks.Add("Seek requested");
                }
                if (i == 10)
                {
                    beforePause = player.Position;
                    player.Pause();
                }
                if (i == 12)
                {
                    afterPause = player.Position;
                    if (Math.Abs(afterPause - beforePause) > .3)
                        throw new Exception("Pause position drift");
                    player.Toggle();
                    checks.Add("Pause/resume OK");
                }
                if (i == 15)
                {
                    Hide();
                    hidden = true;
                    checks.Add("Hidden window; position timer enabled=" + timer.IsEnabled);
                }
                if (i == 20 && args.Contains("--tray-test"))
                {
                    // Exercise restoration from the minimized state, including IsVisibleChanged.
                    WindowState = WindowState.Minimized;
                    PostMessage(new WindowInteropHelper(this).Handle, 0x802B, (IntPtr)1, (IntPtr)0x203);
                    for (int wait = 0; wait < 20 && !IsVisible; wait++)
                        await Task.Delay(100);
                    await Task.Delay(300);
                    if (!IsVisible || WindowState != WindowState.Normal || !timer.IsEnabled)
                        throw new Exception("Native tray callback did not restore window");
                    hidden = false;
                    checks.Add("Native tray double-click callback restored window");
                }
                if (i == seconds - 5)
                {
                    Show();
                    hidden = false;
                    checks.Add("Restored window");
                }
                if (hidden && timer.IsEnabled)
                    hiddenTicks++;
                // Repeat track after the end to sustain a long test without preloading.
                if (!player.Playing && player.Position > player.Duration - 1 && i > 15)
                    await Play(playable);
                await Task.Delay(1000);
                proc.Refresh();
                samples.Add(new
                {
                    second = i + 1,
                    hidden,
                    workingSetMiB = proc.WorkingSet64 / 1048576d,
                    privateMiB = proc.PrivateMemorySize64 / 1048576d,
                    position = player.Position,
                    pcmBufferedSeconds = player.BufferedSeconds
                });
            }
            double cpuSeconds = (proc.TotalProcessorTime - cpu).TotalSeconds;
            checks.Add("Hidden UI timer violations=" + hiddenTicks);
            var report = new
            {
                utc = DateTimeOffset.UtcNow,
                seconds = clock.Elapsed.TotalSeconds,
                cpuSeconds,
                cpuOneCorePercent = cpuSeconds / clock.Elapsed.TotalSeconds * 100,
                cpuMachinePercent = cpuSeconds / clock.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
                logicalProcessors = Environment.ProcessorCount,
                checks,
                samples
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "app-selftest.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { await File.WriteAllTextAsync(Path.Combine(directory, "app-selftest-error.txt"), e.GetType().Name + ": " + e.Message); }
        Exit();
    }
    void Capture(string file)
    {
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file);
        encoder.Save(output);
    }
    void VerifyRoundedRegion()
    {
        var region = CreateRoundRectRgn(0, 0, 1, 1, 1, 1);
        try
        {
            if (GetWindowRgn(new WindowInteropHelper(this).Handle, region) == 0 || PtInRegion(region, 0, 0) || !PtInRegion(region, 100, 100))
                throw new Exception("Native rounded window region was not applied");
        }
        finally { DeleteObject(region); }
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetExtendedStyle(IntPtr window, int index);
    async Task VerifyInlineImport(string directory)
    {
        int windowsBefore = System.Windows.Application.Current.Windows.Count;
        int playlistsBefore = store.Playlists().Count;
        string? temporaryPlaylist = null;
        try
        {
            ImportClick(this, new RoutedEventArgs());
            UpdateLayout();
            if (view != "import" || !ImportPanel.IsVisible || ContentPanel.IsVisible || System.Windows.Application.Current.Windows.Count != windowsBefore)
                throw new Exception("Import must be a page inside the original window");
            Capture(Path.Combine(directory, "import.png"));
            Width = 460;
            Height = 680;
            await Task.Delay(200);
            UpdateLayout();
            Capture(Path.Combine(directory, "import-narrow.png"));
            ImportUrl.Text = "https://example.com/private";
            await StartPlaylistImport();
            if (!importError || importCancel != null || store.Playlists().Count != playlistsBefore)
                throw new Exception("Invalid link must display an inline error without creating a playlist");
            Capture(Path.Combine(directory, "import-invalid.png"));
            ImportUrl.Text = "https://soundcloud.com/laugebabagnohm/sets/ambient-soundscapes-selections";
            var cancelled = StartPlaylistImport();
            CancelImportClick(this, new RoutedEventArgs());
            await cancelled;
            if (importCancel != null || !ImportStart.IsEnabled || ImportCancel.IsVisible)
                throw new Exception("Cancelled import did not restore the inline form");
            if (importedPlaylistId != null) store.Delete(importedPlaylistId);
            Width = 1200;
            Height = 780;
            await StartPlaylistImport();
            temporaryPlaylist = importedPlaylistId;
            if (temporaryPlaylist == null || importError || importProgress is not { Imported: > 0 } || !ImportOpenPlaylist.IsVisible || !ImportStart.IsEnabled)
                throw new Exception("Real inline import failed: " + importMessage);
            if (System.Windows.Application.Current.Windows.Count != windowsBefore)
                throw new Exception("Import opened another application window");
            UpdateLayout();
            Capture(Path.Combine(directory, "import-complete.png"));
            Width = 460;
            Height = 680;
            await Task.Delay(200);
            UpdateLayout();
            Capture(Path.Combine(directory, "import-complete-narrow.png"));
            OpenImportedPlaylist(this, new RoutedEventArgs());
            if (view != "library" || playlistId != temporaryPlaylist || rows.Count == 0)
                throw new Exception("Imported playlist did not open in the same window");
            await File.WriteAllTextAsync(Path.Combine(directory, "inline-import-check.txt"), $"PASS inline navigation, invalid link, cancellation, real public import ({importProgress.Imported}/{importProgress.Total}, unavailable={importProgress.Unavailable}), open local playlist; application window count stayed {windowsBefore}.");
        }
        finally
        {
            if (temporaryPlaylist != null) store.Delete(temporaryPlaylist);
            importedPlaylistId = null;
            playlistId = null;
            RefreshPlaylists();
        }
    }
    async Task VerifyTrayActivation(string directory)
    {
        var other = new Window { Title = "DIPlayer · проверка фокуса", Width = 640, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = "Проверка возврата DIPlayer из трея" };
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            // Real input is needed, not just programmatic activation: the OS checks
            // which process received the last user input before allowing foreground changes.
            var input = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            System.Windows.Input.MouseButtonEventHandler receive = (_, _) => input.TrySetResult();
            PreviewMouseDown += receive;
            try
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "activation-ready.txt"), "Waiting for a real click in DIPlayer before the diagnostic");
                await input.Task.WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(100);
            }
            finally { PreviewMouseDown -= receive; }
            for (int cycle = 0; cycle < 3; cycle++)
            {
                WindowState = WindowState.Minimized;
                DIPlayer.SystemIntegration.WindowActivation.Restore(other);
                await Task.Delay(150);
                if (DIPlayer.SystemIntegration.WindowActivation.GetForegroundWindow() != new WindowInteropHelper(other).Handle)
                    throw new Exception("Activation check could not establish the competing foreground window");
                PostMessage(handle, 0x802B, (IntPtr)1, (IntPtr)0x203);
                await Task.Delay(350);
                if (!IsVisible || WindowState != WindowState.Normal || DIPlayer.SystemIntegration.WindowActivation.GetForegroundWindow() != handle || !IsActive || !timer.IsEnabled)
                    throw new Exception("Tray restore did not activate DIPlayer in the foreground");
                if (Topmost || (GetExtendedStyle(handle, -20) & 0x8) != 0)
                    throw new Exception("Tray restore left the player permanently topmost");
                other.Activate();
                await Task.Delay(150);
                if (DIPlayer.SystemIntegration.WindowActivation.GetForegroundWindow() != new WindowInteropHelper(other).Handle)
                    throw new Exception("Another window could not take focus after tray restore");
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "tray-activation-check.txt"), "PASS 3 simulated tray restores over a competing window: visible, foreground, active, UI timer enabled, no persistent topmost; competing window can take focus again. Physical tray click is not simulated by this check.");
        }
        finally { other.Close(); }
    }
}
