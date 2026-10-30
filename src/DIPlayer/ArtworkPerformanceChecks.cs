using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DIPlayer.UI;

namespace DIPlayer;

public partial class MainWindow
{
    sealed class ArtworkFixtureHandler(byte[] image) : HttpMessageHandler
    {
        internal int Requests, Cancelled, Active, Peak;
        internal int Delay = 80;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            int count = Interlocked.Increment(ref Active);
            int previous;
            do { previous = Peak; } while (count > previous && Interlocked.CompareExchange(ref Peak, count, previous) != previous);
            try
            {
                await Task.Delay(Delay, token);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(image) };
            }
            catch (OperationCanceledException) { Interlocked.Increment(ref Cancelled); throw; }
            finally { Interlocked.Decrement(ref Active); }
        }
    }

    async Task VerifyArtworkPerformance(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Artwork performance checks require isolated storage");
        var pixels = new byte[1280 * 720 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 60; pixels[i + 1] = 100; pixels[i + 2] = 160; pixels[i + 3] = 255; }
        var original = BitmapSource.Create(1280, 720, 96, 96, PixelFormats.Bgra32, null, pixels, 1280 * 4);
        original.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(original));
        using var encoded = new MemoryStream(); encoder.Save(encoded);
        using var handler = new ArtworkFixtureHandler(encoded.ToArray());
        using var client = new HttpClient(handler);
        var store = new ArtworkImageStore(client);
        int uiThread = Environment.CurrentManagedThreadId;
        var uri = new Uri("https://lh3.googleusercontent.com/performance-fixture=w384-h216-rj");
        using var firstLifetime = new CancellationTokenSource();
        var first = store.GetAsync(uri, uri, 384, firstLifetime.Token);
        var second = store.GetAsync(uri, uri, 384, CancellationToken.None);
        firstLifetime.Cancel();
        try { await first; throw new InvalidOperationException("Cancelled artwork waiter completed successfully"); } catch (OperationCanceledException) { }
        var image = await second;
        CheckInteraction(handler.Requests == 1 && image is { IsFrozen: true, PixelWidth: 384, PixelHeight: 216 }, "Shared artwork request duplicated a download or changed its 16:9 aspect ratio");
        CheckInteraction(store.DecodeThreadId != uiThread, "Artwork decoding ran on the WPF dispatcher");
        int stride = (image!.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var decoded = new byte[stride * image.PixelHeight]; image.CopyPixels(decoded, stride, 0);
        CheckInteraction(decoded.Any(value => value != 0), "Frozen artwork still depended on its disposed compressed stream");

        handler.Delay = 500;
        var cancelledUri = new Uri("https://lh3.googleusercontent.com/cancelled-fixture=w384-h216-rj");
        using (var lifetime = new CancellationTokenSource())
        {
            var abandoned = store.GetAsync(cancelledUri, cancelledUri, 384, lifetime.Token);
            await Task.Delay(30); lifetime.Cancel();
            try { await abandoned; } catch (OperationCanceledException) { }
        }
        for (int i = 0; i < 20 && handler.Cancelled == 0; i++) await Task.Delay(10);
        CheckInteraction(handler.Cancelled > 0, "Offscreen artwork did not cancel an unneeded download");
        handler.Delay = 10;
        CheckInteraction(await store.GetAsync(cancelledUri, cancelledUri, 384, CancellationToken.None) != null, "A cancelled request poisoned later artwork loading");

        var batch = Enumerable.Range(0, 12).Select(i =>
        {
            var item = new Uri("https://lh3.googleusercontent.com/batch-" + i + "=w384-h216-rj");
            return store.GetAsync(item, item, 384, CancellationToken.None);
        }).ToArray();
        await Task.WhenAll(batch);
        CheckInteraction(handler.Peak <= 3, "Artwork download/decode concurrency exceeded its shared limit");
        for (int i = 0; i < 70; i++)
        {
            var item = new Uri("https://lh3.googleusercontent.com/cache-" + i + "=w512-h288-rj");
            await store.GetAsync(item, item, 512, CancellationToken.None);
        }
        for (int i = 0; i < 5; i++)
        {
            var item = new Uri("https://lh3.googleusercontent.com/large-cache-" + i + "=w1200-h675-rj");
            await store.GetAsync(item, item, 1200, CancellationToken.None);
        }
        CheckInteraction(store.CachedImageCount <= 64 && store.CachedBytes <= 40L * 1024 * 1024, "Artwork cache exceeded its count/byte budget");
        store.Clear();
        CheckInteraction(store.CachedImageCount == 0 && store.CachedBytes == 0, "Clearing the memory cache retained cache entries");
        CheckInteraction(Artwork.EffectiveDecodeWidth(384, 184, 0) == 384 && Artwork.EffectiveDecodeWidth(384, 184, 1) == 576 && Artwork.EffectiveDecodeWidth(384, 184, 2) == 192 && Artwork.EffectiveDecodeWidth(1200, 1000, 2) == 384, "Artwork quality modes did not preserve the default or honor high/saver settings");

        object previousContent = Content;
        try
        {
            var horizontalItems = new StackPanel { Orientation = Orientation.Horizontal };
            var covers = Enumerable.Range(0, 8).Select(_ => new Artwork { Width = 100, Height = 100, Margin = new Thickness(10) }).ToArray();
            foreach (var cover in covers) horizontalItems.Children.Add(cover);
            var horizontal = new ScrollViewer { Width = 350, Height = 170, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = horizontalItems };
            var bottom = new Artwork { Width = 100, Height = 100 };
            var rows = new StackPanel(); rows.Children.Add(horizontal); rows.Children.Add(new Border { Height = 900 }); rows.Children.Add(bottom);
            var vertical = new ScrollViewer { Width = 400, Height = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Content = rows };
            Content = vertical; await Task.Delay(150); UpdateLayout();
            await File.WriteAllTextAsync(Path.Combine(directory, "artwork-viewport-state.txt"), string.Join(Environment.NewLine,
                covers.Append(bottom).Select(cover => $"loaded={cover.IsLoaded} visible={cover.IsVisible} size={cover.RenderSize} near={cover.IsNearViewport(0)} vertical={cover.TransformToAncestor(vertical).TransformBounds(new Rect(new Point(), cover.RenderSize))}")) + $"\nvertical={vertical.RenderSize} viewport={vertical.ViewportWidth}x{vertical.ViewportHeight} horizontal={horizontal.RenderSize} viewport={horizontal.ViewportWidth}x{horizontal.ViewportHeight}");
            CheckInteraction(covers[0].IsNearViewport(0) && !covers[^1].IsNearViewport(0) && !bottom.IsNearViewport(0), "Nested artwork viewports did not defer horizontal/vertical offscreen covers");
            covers[0].Source = image;
            horizontal.ScrollToRightEnd(); await Task.Delay(170); UpdateLayout();
            CheckInteraction(covers[^1].IsNearViewport(0) && !covers[0].IsNearViewport(0) && covers[0].Source == null, "Horizontal scrolling did not activate the next covers and release offscreen pixels");
            covers[^1].Source = image;
            vertical.ScrollToEnd(); await Task.Delay(170); UpdateLayout();
            CheckInteraction(bottom.IsNearViewport(0) && !covers[^1].IsNearViewport(0) && covers[^1].Source == null, "Vertical scrolling retained covers from the old shelf");
        }
        finally { Content = previousContent; await Task.Delay(100); UpdateLayout(); }
        await File.WriteAllTextAsync(Path.Combine(directory, "artwork-performance-check.txt"), "PASS isolated WPF/HTTP fixtures: nested horizontal and vertical viewport deferral, offscreen pixel release, duplicate request coalescing, independent waiter cancellation, abandoned download cancellation/retry, worker-thread frozen bitmap decode with 16:9 preserved, three concurrent requests maximum, bounded LRU cache, clear-cache and quality modes. No remote image requests used.");
    }
}
