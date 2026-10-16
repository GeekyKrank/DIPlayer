using System.Text.Json.Serialization;

namespace DIPlayer.Core;

public sealed record Track(string Id, string Title, string Artist, long DurationMs, string? Artwork, string Permalink, string Access, string? Issue = null)
{
    public string Duration => TimeSpan.FromMilliseconds(DurationMs).ToString(DurationMs >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");
    public string Description => $"{Artist}  ·  {Duration}";
    public string Availability => Issue ?? (Access == "preview" ? "Preview only" : Access == "blocked" ? "Unavailable: no reason provided" : "");
}
public sealed record TrackPage(List<Track> Tracks, string? Next);
public sealed record PlaylistInfo(string Id, string Title, int Count, bool Public);
public sealed class LocalPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New playlist";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public List<Track> Tracks { get; set; } = [];
    public string? Source { get; set; }
    public bool ImportComplete { get; set; } = true;
    public override string ToString() => Name;
}
public sealed class CatalogException(string message, int? status = null) : Exception(message)
{
    public int? Status { get; } = status;
}
public interface ICatalog
{
    Task<TrackPage> Search(string query, string? next, CancellationToken ct);
    Task<PlaylistInfo> ResolvePlaylist(string url, CancellationToken ct);
    Task<TrackPage> PlaylistTracks(string id, string? next, CancellationToken ct);
    Task<Track> GetTrack(string id, CancellationToken ct);
    Task<List<Track>> GetTracks(IReadOnlyList<string> ids, CancellationToken ct);
    Task<string> Stream(string id, CancellationToken ct);
}
