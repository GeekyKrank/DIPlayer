using System.Windows;
using System.Windows.Media.Imaging;
using DIPlayer.Core;
using DIPlayer.Storage;
using System.Security.Cryptography;
using System.Text;
namespace DIPlayer.UI;

public sealed class Artwork : System.Windows.Controls.Image
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly SemaphoreSlim slots = new(3, 3);
    static readonly Dictionary<string, BitmapSource> cache = [];
    static readonly LinkedList<string> lru = [];
    CancellationTokenSource? cancel;
    string? displayedUrl;
    public int DecodeWidth { get; set; } = 64;
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(string), typeof(Artwork), new PropertyMetadata(null, (o, _) => ((Artwork)o).Load()));
    public string? Url
    {
        get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value);
    }
    public Artwork()
    {
        Stretch = System.Windows.Media.Stretch.UniformToFill;
        Loaded += (_, _) => Load();
        IsVisibleChanged += (_, _) => { if (IsVisible) Load(); else cancel?.Cancel(); };
        Unloaded += (_, _) => { cancel?.Cancel(); cancel?.Dispose(); cancel = null; Source = null; displayedUrl = null; };
    }
    async void Load()
    {
        cancel?.Cancel();
        cancel?.Dispose();
        cancel = null;
        string? requestedUrl = Url;
        if (displayedUrl != requestedUrl) Source = null;
        if (!IsLoaded || !IsVisible || string.IsNullOrEmpty(Url))
            return;
        int decode = Math.Clamp(DecodeWidth, 32, 256);
        string url = Url.Replace("-large.", decode > 64 ? "-t200x200." : "-t67x67.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase) || uri.Host == "i.ytimg.com" || System.Text.RegularExpressions.Regex.IsMatch(uri.Host, @"^(?:lh\d+|yt\d+)\.googleusercontent\.com$") || uri.Host == "yt3.ggpht.com"))
            return;
        if (uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            url = System.Text.RegularExpressions.Regex.Replace(url, @"=w\d+-h\d+", "=w" + decode + "-h" + decode);
            url = url.Replace("-rw", "-rj", StringComparison.Ordinal);
            uri = new Uri(url);
        }
        string cacheKey = url + "#decode=" + decode;
        if (cache.TryGetValue(cacheKey, out var image))
        {
            lru.Remove(cacheKey);
            lru.AddLast(cacheKey);
            Source = image;
            displayedUrl = requestedUrl;
            return;
        }
        cancel = new();
        var ct = cancel.Token;
        for (int attempt = 0; attempt < 3 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                if (attempt > 0) await Task.Delay(400 * attempt, ct);
                await slots.WaitAsync(ct);
                try
                {
                    // Another visible copy of this cover may have filled the cache while waiting.
                    if (!cache.TryGetValue(cacheKey, out image))
                    {
                        var candidate = attempt == 1 ? new Uri(requestedUrl!) : uri;
                        using var r = await http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, ct);
                        r.EnsureSuccessStatusCode();
                        var bytes = await SoundCloudCatalog.ReadLimited(r.Content, 2 * 1024 * 1024, ct);
                        ct.ThrowIfCancellationRequested();
                        using var input = new MemoryStream(bytes);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = decode;
                        bitmap.StreamSource = input;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        image = bitmap;
                        cache[cacheKey] = image;
                    }
                    lru.Remove(cacheKey); lru.AddLast(cacheKey);
                    while (cache.Count > 64)
                    {
                        cache.Remove(lru.First!.Value); lru.RemoveFirst();
                    }
                    if (!ct.IsCancellationRequested && Url == requestedUrl && IsLoaded && IsVisible)
                    {
                        Source = image; displayedUrl = requestedUrl;
                    }
                    return;
                }
                finally { slots.Release(); }
            }
            catch (Exception e) when (e is OperationCanceledException or HttpRequestException or IOException or NotSupportedException or CatalogException or System.IO.FileFormatException or ArgumentException or InvalidOperationException) { }
        }
    }
}
