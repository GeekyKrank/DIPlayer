using System.Net.Http;
using System.Windows.Media.Imaging;
using DIPlayer.Core;

namespace DIPlayer.UI;

// Shared downloads and worker-thread decoding return frozen images to WPF.
internal sealed class ArtworkImageStore
{
    internal static readonly ArtworkImageStore Shared = new();
    const long MaximumBytes = 40L * 1024 * 1024;
    const int MaximumImages = 64;
    readonly HttpClient http;
    readonly SemaphoreSlim slots = new(3, 3);
    readonly object gate = new();
    readonly Dictionary<string, Entry> cache = [];
    readonly LinkedList<string> lru = [];
    readonly Dictionary<string, Pending> pending = [];
    readonly Dictionary<string, DateTime> failures = [];
    long bytes;
    int largeImages;
    internal int DecodeThreadId { get; private set; }
    internal int CachedImageCount { get { lock (gate) return cache.Count; } }
    internal long CachedBytes { get { lock (gate) return bytes; } }
    internal static string Key(Uri uri, int decode) => uri.AbsoluteUri + "#decode=" + decode;

    sealed record Entry(BitmapSource Image, LinkedListNode<string> Node, long Bytes);
    // BitmapImage retains its StreamSource property after OnLoad. Dropping the
    // inner stream on dispose keeps cached bitmaps from retaining compressed bytes.
    sealed class ImageStream(byte[] data) : Stream
    {
        MemoryStream? inner = new(data, false);
        MemoryStream Current => inner ?? throw new ObjectDisposedException(nameof(ImageStream));
        public override bool CanRead => inner?.CanRead == true;
        public override bool CanSeek => inner?.CanSeek == true;
        public override bool CanWrite => false;
        public override long Length => Current.Length;
        public override long Position { get => Current.Position; set => Current.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Current.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => Current.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner?.Dispose(); inner = null; } base.Dispose(disposing); }
    }
    sealed class Pending(Uri uri, Uri fallback, int decode)
    {
        internal readonly Uri Uri = uri, Fallback = fallback;
        internal readonly int Decode = decode;
        internal readonly CancellationTokenSource Cancel = new();
        internal readonly TaskCompletionSource<BitmapSource?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Consumers;
        internal bool Finished;
    }

    internal ArtworkImageStore(HttpClient? client = null) => http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    internal void Clear()
    {
        lock (gate) { cache.Clear(); lru.Clear(); failures.Clear(); bytes = 0; largeImages = 0; }
    }

    internal async Task<BitmapSource?> GetAsync(Uri uri, Uri fallback, int decode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string key = Key(uri, decode);
        Pending request;
        lock (gate)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                lru.Remove(cached.Node); lru.AddLast(cached.Node); return cached.Image;
            }
            if (failures.TryGetValue(key, out var retry) && retry > DateTime.UtcNow) return null;
            failures.Remove(key);
            if (!pending.TryGetValue(key, out request!))
            {
                request = new(uri, fallback, decode);
                pending.Add(key, request);
                _ = Task.Run(() => RunRequest(key, request));
            }
            request.Consumers++;
        }
        try { return await request.Completion.Task.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            bool cancel = false, dispose = false;
            lock (gate)
            {
                if (--request.Consumers == 0)
                {
                    if (pending.TryGetValue(key, out var value) && ReferenceEquals(value, request)) pending.Remove(key);
                    cancel = !request.Finished; dispose = request.Finished;
                }
            }
            if (cancel) { try { request.Cancel.Cancel(); } catch (ObjectDisposedException) { } }
            if (dispose) request.Cancel.Dispose();
        }
    }

    async Task RunRequest(string key, Pending request)
    {
        try
        {
            var image = await Fetch(request).ConfigureAwait(false);
            lock (gate)
            {
                if (image != null && !request.Cancel.IsCancellationRequested) Remember(key, image);
                else if (!request.Cancel.IsCancellationRequested)
                {
                    if (failures.Count >= MaximumImages) failures.Remove(failures.Keys.First());
                    failures[key] = DateTime.UtcNow.AddSeconds(15);
                }
            }
            request.Completion.TrySetResult(image);
        }
        catch (OperationCanceledException) { request.Completion.TrySetCanceled(); }
        catch (Exception error) when (error is HttpRequestException or IOException or NotSupportedException or CatalogException or ArgumentException or InvalidOperationException or System.IO.FileFormatException or System.Runtime.InteropServices.COMException)
        { request.Completion.TrySetResult(null); }
        finally
        {
            lock (gate)
            {
                request.Finished = true;
                if (pending.TryGetValue(key, out var value) && ReferenceEquals(value, request)) pending.Remove(key);
                if (request.Consumers == 0) request.Cancel.Dispose();
            }
        }
    }

    async Task<BitmapSource?> Fetch(Pending request)
    {
        var token = request.Cancel.Token;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (attempt > 0) await Task.Delay(attempt * 400, token).ConfigureAwait(false);
            await slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var uri = attempt == 1 ? request.Fallback : request.Uri;
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var data = await SoundCloudCatalog.ReadLimited(response.Content, 4 * 1024 * 1024, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                DecodeThreadId = Environment.CurrentManagedThreadId;
                using var input = new ImageStream(data);
                var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                double width = Math.Min(request.Decode, frame.PixelWidth);
                double pixels = width * width * frame.PixelHeight / frame.PixelWidth;
                if (pixels > 2 * 1024 * 1024) width *= Math.Sqrt(2 * 1024 * 1024 / pixels);
                input.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Round(width)); bitmap.StreamSource = input;
                bitmap.EndInit(); bitmap.Freeze();
                token.ThrowIfCancellationRequested(); return bitmap;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception error) when (!token.IsCancellationRequested && error is (HttpRequestException or IOException or NotSupportedException or CatalogException or ArgumentException or InvalidOperationException or System.IO.FileFormatException or System.Runtime.InteropServices.COMException)) { }
            finally { slots.Release(); }
        }
        return null;
    }

    void Remember(string key, BitmapSource image)
    {
        if (cache.TryGetValue(key, out var existing)) Remove(key, existing);
        long size = (long)image.PixelWidth * image.PixelHeight * 4;
        if (size > MaximumBytes) return;
        cache.Add(key, new(image, lru.AddLast(key), size)); bytes += size;
        if (image.PixelWidth > 512) largeImages++;
        while (cache.Count > MaximumImages || bytes > MaximumBytes)
        {
            string oldest = lru.First!.Value;
            Remove(oldest, cache[oldest]);
        }
        while (largeImages > 4)
        {
            string oldest = lru.First(key => cache[key].Image.PixelWidth > 512);
            Remove(oldest, cache[oldest]);
        }
    }
    void Remove(string key, Entry entry)
    {
        cache.Remove(key); lru.Remove(entry.Node); bytes -= entry.Bytes;
        if (entry.Image.PixelWidth > 512) largeImages--;
    }
}
