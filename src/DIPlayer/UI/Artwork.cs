using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DIPlayer.UI;

public sealed class Artwork : Image
{
    static int qualityMode;
    CancellationTokenSource? cancel;
    string? displayedKey, loadingKey;
    string? resolvedSource, resolvedKey;
    Uri? resolvedUri;
    int resolvedDecode, resolvedQuality;
    int generation, registration;
    FrameworkElement[] viewportClips = [];

    public static int QualityMode
    {
        get => Volatile.Read(ref qualityMode);
        set
        {
            int mode = Math.Clamp(value, 0, 2);
            if (Interlocked.Exchange(ref qualityMode, mode) != mode) ArtworkViewportScheduler.RefreshAll();
        }
    }
    public static int CachedImageCount => ArtworkImageStore.Shared.CachedImageCount;
    public static long CachedBytes => ArtworkImageStore.Shared.CachedBytes;
    public static void ClearMemoryCache() => ArtworkImageStore.Shared.Clear();

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(
        nameof(DecodeWidth), typeof(int), typeof(Artwork), new PropertyMetadata(64, Changed));
    public int DecodeWidth { get => (int)GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    public bool PreserveAspectRatio { get; set; } = true;
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
        nameof(Url), typeof(string), typeof(Artwork), new PropertyMetadata(null, Changed));
    public string? Url { get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }

    public Artwork()
    {
        Stretch = Stretch.Uniform;
        Loaded += (_, _) =>
        {
            if (registration != 0) ArtworkViewportScheduler.Unregister(Dispatcher, registration);
            viewportClips = FindViewportClips();
            registration = ArtworkViewportScheduler.Register(this);
        };
        SizeChanged += (_, _) => ArtworkViewportScheduler.Request(Dispatcher);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) Release();
            else ArtworkViewportScheduler.Request(Dispatcher);
        };
        Unloaded += (_, _) =>
        {
            ArtworkViewportScheduler.Unregister(Dispatcher, registration);
            registration = 0; viewportClips = []; Release();
        };
    }

    static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var image = (Artwork)target;
        image.CancelLoad();
        image.Source = null; image.displayedKey = null;
        image.resolvedSource = null;
        if (image.IsLoaded) ArtworkViewportScheduler.Request(image.Dispatcher);
    }

    public static Uri? ResolveRequestUri(string? source, int decodeWidth, bool allowDownsample = false)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        int decode = Math.Clamp(decodeWidth, 32, 1200);
        string url = source.Replace("-large.", decode > 256 ? "-t500x500." : decode > 96 ? "-t200x200." : "-t67x67.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase) || uri.Host == "i.ytimg.com" || System.Text.RegularExpressions.Regex.IsMatch(uri.Host, @"^(?:lh\d+|yt\d+)\.googleusercontent\.com$") || uri.Host == "yt3.ggpht.com")) return null;
        if (!uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)) return uri;
        url = System.Text.RegularExpressions.Regex.Replace(url, @"=w(\d+)-h(\d+)", match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out int originalWidth) || !int.TryParse(match.Groups[2].Value, out int originalHeight) || originalWidth <= 0 || originalHeight <= 0) return match.Value;
            int width = allowDownsample ? decode : Math.Max(originalWidth, decode);
            long height = Math.Max(1, (long)Math.Round((double)originalHeight * width / originalWidth));
            return "=w" + width + "-h" + height;
        });
        return new Uri(url.Replace("-rw", "-rj", StringComparison.Ordinal));
    }

    internal static int EffectiveDecodeWidth(int requested, double physicalWidth, int mode)
    {
        int width = Math.Clamp((int)Math.Ceiling(Math.Max(requested, physicalWidth)), 32, 1200);
        return mode switch { 1 => Math.Min(1200, (int)Math.Ceiling(width * 1.5)), 2 => Math.Min(width, requested >= 1000 ? 384 : 192), _ => width };
    }

    FrameworkElement[] FindViewportClips()
    {
        var clips = new List<FrameworkElement>();
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        FrameworkElement? root = null;
        while (current != null)
        {
            if (current is FrameworkElement element)
            {
                root = element;
                if (element is ScrollContentPresenter or Window || element.ClipToBounds) clips.Add(element);
            }
            current = VisualTreeHelper.GetParent(current);
        }
        if (root != null && !clips.Contains(root)) clips.Add(root);
        return clips.ToArray();
    }

    internal bool IsNearViewport(double margin)
    {
        if (!IsLoaded || !IsVisible || PresentationSource.FromVisual(this) == null) return false;
        try
        {
            Visual origin = this;
            var bounds = new Rect(new Point(), RenderSize);
            // Image.ArrangeOverride returns a zero RenderSize until Source exists.
            // The allocated layout slot still locates that empty cover accurately.
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                if (VisualTreeHelper.GetParent(this) is not Visual parent) return false;
                origin = parent; bounds = LayoutInformation.GetLayoutSlot(this);
            }
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;
            foreach (var clip in viewportClips)
            {
                if (!clip.IsVisible || clip.ActualWidth <= 0 || clip.ActualHeight <= 0 || clip is Window { WindowState: WindowState.Minimized }) return false;
                var viewport = new Rect(0, 0, clip.ActualWidth, clip.ActualHeight);
                viewport.Inflate(margin, margin);
                var transformed = ReferenceEquals(origin, clip) ? bounds : origin.TransformToAncestor(clip).TransformBounds(bounds);
                if (!transformed.IntersectsWith(viewport)) return false;
            }
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    internal void RefreshViewport()
    {
        // Small look-ahead and retention margins prevent edge placeholders/churn.
        if (!IsNearViewport(Source == null ? 160 : 320)) { Release(); return; }
        string? source = Url;
        if (string.IsNullOrEmpty(source)) { CancelLoad(); return; }
        int quality = QualityMode;
        double width = ActualWidth > 0 ? ActualWidth : LayoutInformation.GetLayoutSlot(this).Width;
        int decode = EffectiveDecodeWidth(DecodeWidth, width * VisualTreeHelper.GetDpi(this).DpiScaleX, quality);
        if (resolvedSource != source || resolvedDecode != decode || resolvedQuality != quality)
        {
            resolvedSource = source; resolvedDecode = decode; resolvedQuality = quality;
            resolvedUri = ResolveRequestUri(source, decode, quality == 2);
            resolvedKey = resolvedUri == null ? null : ArtworkImageStore.Key(resolvedUri, decode);
        }
        var uri = resolvedUri;
        if (uri == null) { Release(); return; }
        string key = resolvedKey!;
        if (Source != null && displayedKey == key || loadingKey == key && cancel?.IsCancellationRequested == false) return;
        CancelLoad();
        var lifetime = new CancellationTokenSource();
        cancel = lifetime; loadingKey = key;
        _ = LoadAsync(uri, new Uri(source), decode, key, generation, lifetime);
    }

    async Task LoadAsync(Uri uri, Uri fallback, int decode, string key, int version, CancellationTokenSource lifetime)
    {
        try
        {
            var image = await ArtworkImageStore.Shared.GetAsync(uri, fallback, decode, lifetime.Token);
            if (image != null && !lifetime.IsCancellationRequested && version == generation && IsNearViewport(320))
            {
                Source = image; displayedKey = key;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(cancel, lifetime)) { cancel = null; loadingKey = null; }
            lifetime.Dispose();
        }
    }

    void CancelLoad()
    {
        generation++;
        cancel?.Cancel(); cancel = null; loadingKey = null;
    }
    void Release()
    {
        CancelLoad();
        if (Source != null) Source = null;
        displayedKey = null;
    }
}
