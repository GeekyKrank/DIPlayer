namespace DIPlayer.Core;

public static class BrowserAdFilter
{
    static readonly string[] AdHosts = ["doubleclick.net", "googlesyndication.com", "googleadservices.com"];
    public static bool Block(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (AdHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) return true;
        bool youtube = uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
        return youtube && (uri.AbsolutePath.StartsWith("/pagead/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/api/stats/ads", StringComparison.Ordinal));
    }
}
