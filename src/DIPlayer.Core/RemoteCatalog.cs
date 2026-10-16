using System.Net.Http.Json;
using System.Text.Json;
namespace DIPlayer.Core;
public sealed class RemoteCatalog : ICatalog, IDisposable
{
    readonly HttpClient http;
    public RemoteCatalog(string endpoint)
    {
        var uri = new Uri(endpoint.TrimEnd('/') + "/");
        if (uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")) throw new CatalogException("Для сервера нужен HTTPS.");
        http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(25) };
    }
    async Task<T> Get<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        var bytes = await SoundCloudCatalog.ReadLimited(response.Content, 2*1024*1024, ct);
        if (!response.IsSuccessStatusCode)
        {
            string message = "Сервер каталога недоступен.";
            try { using var doc = JsonDocument.Parse(bytes); message = doc.RootElement.GetProperty("message").GetString() ?? message; } catch (JsonException) { }
            throw new CatalogException(message,(int)response.StatusCode);
        }
        return (T)JsonSerializer.Deserialize(bytes,typeof(T),CatalogJsonContext.Default)!;
    }
    static string E(string? s) => Uri.EscapeDataString(s ?? "");
    public Task<TrackPage> Search(string q,string? next,CancellationToken ct) => Get<TrackPage>($"search?q={E(q)}&next={E(next)}",ct);
    public Task<PlaylistInfo> ResolvePlaylist(string url,CancellationToken ct) => Get<PlaylistInfo>($"resolve?url={E(url)}",ct);
    public Task<TrackPage> PlaylistTracks(string id,string? next,CancellationToken ct) => Get<TrackPage>($"playlists/{E(id)}/tracks?next={E(next)}",ct);
    public Task<Track> GetTrack(string id,CancellationToken ct) => Get<Track>($"tracks/{E(id)}",ct);
    public Task<List<Track>> GetTracks(IReadOnlyList<string> ids,CancellationToken ct)=>Get<List<Track>>($"tracks?ids={E(string.Join(",",ids))}",ct);
    public Task<string> Stream(string id,CancellationToken ct) => Get<string>($"tracks/{E(id)}/stream",ct);
    public void Dispose() => http.Dispose();
}
