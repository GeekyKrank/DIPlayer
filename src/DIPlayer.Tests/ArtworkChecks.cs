using DIPlayer.UI;

static class ArtworkChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(Artwork.ResolveRequestUri("https://lh3.googleusercontent.com/cover=w160-h90-rw", 640)?.AbsoluteUri == "https://lh3.googleusercontent.com/cover=w640-h360-rj", "landscape cover keeps 16:9 when requesting a sharper image");
        check(Artwork.ResolveRequestUri("https://lh3.googleusercontent.com/cover=w1080-h1080-rj", 384)?.AbsoluteUri.EndsWith("=w1080-h1080-rj") == true, "large cover metadata is not downgraded to a small thumbnail");
        check(Artwork.ResolveRequestUri("https://lh3.googleusercontent.com/portrait=w120-h180-rj", 400)?.AbsoluteUri.EndsWith("=w400-h600-rj") == true, "portrait cover keeps every edge and its original aspect ratio");
        check(Artwork.ResolveRequestUri("https://i1.sndcdn.com/artworks-cover-large.jpg", 384)?.AbsoluteUri.EndsWith("-t500x500.jpg") == true, "SoundCloud cards request the sharper available cover variant");
        check(Artwork.ResolveRequestUri("https://i.ytimg.com/vi/test/hqdefault.jpg", 640)?.AbsoluteUri.EndsWith("/hqdefault.jpg") == true, "YouTube image URLs are preserved without guessing unavailable variants");
        check(Artwork.ResolveRequestUri("http://lh3.googleusercontent.com/cover", 400) == null && Artwork.ResolveRequestUri("https://example.org/cover", 400) == null, "cover resizing preserves the HTTPS image-host allowlist");
    }
}
