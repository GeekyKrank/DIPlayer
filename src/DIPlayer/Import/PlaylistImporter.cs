using DIPlayer.Core;
using DIPlayer.Storage;
namespace DIPlayer.Import;

public sealed record PlaylistImportProgress(string LocalId, int Imported, int Total, int Unavailable);

public sealed class PlaylistImporter(ICatalog catalog, LibraryStore store)
{
    public async Task<string> Import(string url, IProgress<string> progress, CancellationToken ct, IProgress<PlaylistImportProgress>? details = null)
    {
        var info = await catalog.ResolvePlaylist(url, ct);
        string local = store.Create(info.Title, SoundCloudCatalog.PublicPlaylistUrl(url), false);
        store.SetArtwork(local, info.Artwork);
        store.SetCreatedAt(local, info.CreatedAt);
        int count = 0, unavailable = 0;
        details?.Report(new(local, 0, info.Count, 0));
        string? artwork = info.Artwork;
        string? next = null;
        var visited = new HashSet<string>();
        do
        {
            ct.ThrowIfCancellationRequested();
            var page = await catalog.PlaylistTracks(info.Id, next, ct);
            if (string.IsNullOrWhiteSpace(artwork))
            {
                artwork = page.Tracks.Select(t => t.Artwork).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
                if (artwork != null) store.SetArtwork(local, artwork);
            }
            store.Add(local, page.Tracks);
            count += page.Tracks.Count;
            unavailable += page.Tracks.Count(t => t.Access != "playable" || t.Issue != null);
            details?.Report(new(local, count, info.Count, unavailable));
            progress.Report($"Import: {count} / {info.Count}; unavailable or missing: {unavailable}");
            next = page.Next;
            if (next != null && !visited.Add(next))
                throw new CatalogException("The API repeated a page. Partial import saved.");
            if (next != null)
                await Task.Delay(300, ct);
        } while (next != null);
        if (count < info.Count)
            progress.Report($"Imported {count} of {info.Count}: SoundCloud did not return the remaining tracks.");
        else
            store.Complete(local);
        return local;
    }
}
