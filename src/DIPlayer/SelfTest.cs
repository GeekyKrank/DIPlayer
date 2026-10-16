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
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
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
            int queryIndex = Array.IndexOf(args, "--query");
            string testQuery = queryIndex >= 0 ? args[queryIndex + 1] : "ambient";
            var found = await Catalog().Search(testQuery, null, default);
            checks.Add("Official search returned " + found.Tracks.Count + " tracks");
            var playable = seconds >= 3600 ? found.Tracks.Where(t => t.Access == "playable").OrderByDescending(t => t.DurationMs).First() : found.Tracks.First(t => t.Access == "playable");
            checks.Add("Track: " + playable.Title + "; duration ms=" + playable.DurationMs);
            query = "ambient";
            SetView("search");
            ShowRows(found.Tracks.Select(t => new DisplayRow(t)));
            Heading.Text = "Результаты поиска";
            next = found.Next;
            Paging();
            queue.Tracks.Clear();
            queue.Index = 0;
            queue.Add(playable);
            await Play(playable);
            await Task.Delay(3500);
            if (!player.Playing || player.Position < .2)
                throw new Exception("Playback did not advance");
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
                Width = 940;
                Height = 710;
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
                    PostMessage(new WindowInteropHelper(this).Handle, 0x802B, (IntPtr)1, (IntPtr)0x203);
                    await Task.Delay(100);
                    if (!IsVisible)
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
}
