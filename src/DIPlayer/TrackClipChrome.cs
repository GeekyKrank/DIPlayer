using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DIPlayer.UI;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;

public partial class MainWindow
{
    string? clipChromeAssets;

    Task InstallClipChrome(CoreWebView2 core)
    {
        // WebView2 owns the entire video HWND. Reuse the WPF artwork and font
        // inside it so the same controls can sit directly over the picture.
        clipChromeAssets ??= JsonSerializer.Serialize(new
        {
            mascot = ClipArtwork(BrandMascot.Source, 46, 58),
            wordmark = ClipArtwork(BrandLogo.Source, BrandLogo.Width, BrandLogo.Height),
            home = ClipGlyph(Symbols.Home, 20),
            library = ClipGlyph(Symbols.Library, 20),
            likes = ClipGlyph(Symbols.Heart, 20),
            settings = ClipGlyph(Symbols.Settings, 20),
            minimize = ClipGlyph(Symbols.Minimize, 10),
            maximize = ClipGlyph(Symbols.Maximize, 10),
            restore = ClipGlyph(Symbols.Restore, 10),
            close = ClipGlyph(Symbols.Close, 10),
            back = ClipGlyph(Symbols.Down, 16),
            previous = ClipGlyph(Symbols.Previous, 14),
            next = ClipGlyph(Symbols.Next, 14),
            play = ClipGlyph(Symbols.Play, 14),
            pause = ClipGlyph(Symbols.Pause, 14)
        });
        string active = Equals(HomeNav.Tag, "active") ? "home" : Equals(YouTubeLibraryNav.Tag, "active") ? "library" :
            Equals(YouTubeLikesNav.Tag, "active") ? "likes" : "";
        return core.ExecuteScriptAsync("window.__diplayerClip?.setChrome(" + clipChromeAssets + "," +
            JsonSerializer.Serialize(BrandLogo.Width) + "," + JsonSerializer.Serialize(active) + "," + ClipMaximized + ")");
    }

    string ClipMaximized => WindowState == WindowState.Maximized ? "true" : "false";

    void SyncClipWindowState()
    {
        if (clipMode && (publicClipBrowser?.CoreWebView2 ?? musicBrowser?.CoreWebView2) is { } core)
            _ = Guard(() => core.ExecuteScriptAsync("window.__diplayerClip?.setMaximized(" + ClipMaximized + ")"));
    }

    static string ClipArtwork(ImageSource image, double width, double height) =>
        ClipPng(width, height, context =>
        {
            double scale = Math.Min(width / image.Width, height / image.Height);
            double w = image.Width * scale, h = image.Height * scale;
            context.DrawImage(image, new Rect((width - w) / 2, (height - h) / 2, w, h));
        });

    static string ClipGlyph(string symbol, double size)
    {
        var typeface = new Typeface(Symbols.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(symbol, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, 2);
        return ClipPng(size + 4, size + 4, context =>
            context.DrawText(text, new Point((size + 4 - text.Width) / 2, (size + 4 - text.Height) / 2)));
    }

    static string ClipPng(double width, double height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) draw(context);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
    }
}
