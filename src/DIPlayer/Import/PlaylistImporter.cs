using DIPlayer.Core;
using DIPlayer.Storage;
namespace DIPlayer.Import;

public sealed class PlaylistImporter(ICatalog catalog, LibraryStore store)
{
    public async Task<string> Import(string url, IProgress<string> progress, CancellationToken ct)
    {
        var info = await catalog.ResolvePlaylist(url, ct);
        string local = store.Create(info.Title, SoundCloudCatalog.PublicPlaylistUrl(url), false);
        int count = 0, unavailable = 0;
        string? next = null;
        var visited = new HashSet<string>();
        do
        {
            ct.ThrowIfCancellationRequested();
            var page = await catalog.PlaylistTracks(info.Id, next, ct);
            store.Add(local, page.Tracks);
            count += page.Tracks.Count;
            unavailable += page.Tracks.Count(t => t.Access != "playable" || t.Issue != null);
            progress.Report($"Импорт: {count} / {info.Count}; недоступных или без данных: {unavailable}");
            next = page.Next;
            if (next != null && !visited.Add(next))
                throw new CatalogException("API повторил страницу. Частичный импорт сохранён.");
            if (next != null)
                await Task.Delay(300, ct);
        } while (next != null);
        if (count < info.Count)
            progress.Report($"Импортировано {count} из {info.Count}: SoundCloud не вернул остальные записи.");
        else
            store.Complete(local);
        return local;
    }
}
