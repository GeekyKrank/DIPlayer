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
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(string), typeof(Artwork), new PropertyMetadata(null, (o, _) => ((Artwork)o).Load()));
    public string? Url
    {
        get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value);
    }
    public Artwork()
    {
        Width = 48;
        Height = 48;
        Stretch = System.Windows.Media.Stretch.UniformToFill;
        Loaded += (_, _) => Load();
        IsVisibleChanged += (_, _) => { if (IsVisible) Load(); else cancel?.Cancel(); };
        Unloaded += (_, _) => { cancel?.Cancel(); cancel?.Dispose(); cancel = null; Source = null; };
    }
    async void Load()
    {
        cancel?.Cancel();
        cancel?.Dispose();
        cancel = null;
        Source = null;
        if (!IsLoaded || !IsVisible || string.IsNullOrEmpty(Url))
            return;
        string url = Url.Replace("-large.", "-t67x67.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.EndsWith(".sndcdn.com"))
            return;
        if (cache.TryGetValue(url, out var image))
        {
            lru.Remove(url);
            lru.AddLast(url);
            Source = image;
            return;
        }
        cancel = new();
        var ct = cancel.Token;
        try
        {
            await slots.WaitAsync(ct);
            byte[] bytes;
            try
            {
                using var r = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                r.EnsureSuccessStatusCode();
                bytes = await SoundCloudCatalog.ReadLimited(r.Content, 256 * 1024, ct);
            }
            finally { slots.Release(); }
            ct.ThrowIfCancellationRequested();
            using var input = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 64;
            bitmap.StreamSource = input;
            bitmap.EndInit();
            bitmap.Freeze();
            cache[url] = bitmap;
            lru.Remove(url);
            lru.AddLast(url);
            while (cache.Count > 64)
            {
                cache.Remove(lru.First!.Value);
                lru.RemoveFirst();
            }
            Source = bitmap;
        }
        catch (Exception e) when (e is OperationCanceledException or HttpRequestException or IOException or NotSupportedException or CatalogException or System.IO.FileFormatException or ArgumentException or InvalidOperationException) { }
    }
}
