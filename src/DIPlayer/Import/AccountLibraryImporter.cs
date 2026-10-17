using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using DIPlayer.Core;
using DIPlayer.Storage;

namespace DIPlayer.Import;

public sealed class AccountLibraryImporter : IDisposable
{
    readonly HttpClient http;
    readonly LibraryStore store;
    string? session, secret;
    DateTimeOffset nextRequest;
    public AccountLibraryImporter(string server, LibraryStore store)
    {
        var endpoint = new Uri(server.TrimEnd('/') + "/");
        if (endpoint.Scheme != "https" && !(endpoint.IsLoopback && endpoint.Scheme == "http")) throw new CatalogException("Account sign-in requires HTTPS.");
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(25) };
        this.store = store;
    }
    async Task<JsonDocument> Request(string path, HttpMethod method, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (nextRequest > DateTimeOffset.UtcNow) await Task.Delay(nextRequest - DateTimeOffset.UtcNow, ct);
            nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(2200);
            using var request = new HttpRequestMessage(method, path);
            if (secret != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode == 429 && attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(60), ct); continue; }
            var bytes = await SoundCloudCatalog.ReadLimited(response.Content, 2 * 1024 * 1024, ct);
            if (!response.IsSuccessStatusCode)
            {
                string message = "Account import is unavailable. Please try again later.";
                try { using var error = JsonDocument.Parse(bytes); if (error.RootElement.TryGetProperty("message", out var m)) message = m.GetString() ?? message; } catch (JsonException) { }
                throw new CatalogException(message, (int)response.StatusCode);
            }
            return JsonDocument.Parse(bytes);
        }
    }
    static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    static JsonElement Collection(JsonElement root) => root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
    static JsonElement Unwrap(JsonElement value, string name) => value.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : value;
    string DataPath(string kind, string? playlist, string? next) => "auth/session/" + session + "/data?kind=" + kind + "&playlist=" + Uri.EscapeDataString(playlist ?? "") + "&next=" + Uri.EscapeDataString(next ?? "");
    public async Task<int> Run(IProgress<string> progress, CancellationToken ct)
    {
        try
        {
            using var start = await Request("auth/start", HttpMethod.Post, ct);
            session = Text(start.RootElement, "id") ?? throw new CatalogException("Invalid sign-in session.");
            secret = Text(start.RootElement, "secret") ?? throw new CatalogException("Invalid sign-in session.");
            string url = Text(start.RootElement, "url") ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var authorize) || authorize.Scheme != "https" || authorize.Host != "secure.soundcloud.com" || authorize.AbsolutePath != "/authorize") throw new CatalogException("Invalid SoundCloud sign-in URL.");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            progress.Report("Finish signing in on the SoundCloud page in your browser. Waiting for permission…");
            string? user = null;
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(5000, ct);
                using var status = await Request("auth/session/" + session, HttpMethod.Get, ct);
                string? state = Text(status.RootElement, "status");
                if (state == "error") throw new CatalogException(Text(status.RootElement, "message") ?? "SoundCloud sign-in failed.");
                if (state == "ready") { user = Text(status.RootElement, "userId"); break; }
            }
            if (user == null) throw new CatalogException("Sign-in timed out. Please try again.");
            int imported = 0, partial = 0;
            var seen = new HashSet<string>();
            foreach (string kind in new[] { "playlists", "liked-playlists" })
            {
                string? next = null;
                var cursors = new HashSet<string>();
                do
                {
                    using var page = await Request(DataPath(kind, null, next), HttpMethod.Get, ct);
                    foreach (var item in Collection(page.RootElement).EnumerateArray())
                    {
                        var entry = Unwrap(item, "playlist");
                        string id = entry.TryGetProperty("id", out var pid) ? pid.ToString() : Text(entry, "urn") ?? "";
                        if (id.Length == 0 || !seen.Add(id)) continue;
                        if (seen.Count > 500) throw new CatalogException("The import limit is 500 playlists. The imported lists have been saved.");
                        string name = Text(entry, "title") ?? "SoundCloud playlist";
                        int expected = entry.TryGetProperty("track_count", out var count) && count.TryGetInt32(out int n) ? n : -1;
                        bool complete = await ImportList(user, "playlist:" + id, name, "tracks", id, expected, progress, ct, Text(entry, "artwork_url"), Text(entry, "created_at"));
                        imported++; if (!complete) partial++;
                    }
                    next = Text(page.RootElement, "next_href");
                    if (next != null && !cursors.Add(next)) throw new CatalogException("SoundCloud repeated a playlist page. Partial import saved.");
                } while (next != null);
            }
            bool likesComplete = await ImportList(user, "likes", "Likes", "likes", null, -1, progress, ct);
            imported++; if (!likesComplete) partial++;
            progress.Report($"Done. {imported} local playlists saved, including Likes." + (partial > 0 ? $" Incomplete lists: {partial}." : ""));
            return imported;
        }
        finally
        {
            if (session != null && secret != null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    using var request = new HttpRequestMessage(HttpMethod.Delete, "auth/session/" + session);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                    using var response = await http.SendAsync(request, timeout.Token);
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
            }
            session = null; secret = null;
        }
    }
    async Task<bool> ImportList(string user, string sourceId, string name, string kind, string? playlist, int expected, IProgress<string> progress, CancellationToken ct, string? artwork = null, string? createdAt = null)
    {
        string staging = store.Create(name, complete: false);
        int count = 0, unavailable = 0;
        string? next = null;
        var cursors = new HashSet<string>();
        do
        {
            using var page = await Request(DataPath(kind, playlist, next), HttpMethod.Get, ct);
            var tracks = Collection(page.RootElement).EnumerateArray().Select(item => SoundCloudCatalog.ParseTrack(Unwrap(item, "track"))).Where(t => t.Id.Length > 0).ToList();
            count += tracks.Count; unavailable += tracks.Count(t => t.Access != "playable" || t.Issue != null);
            artwork ??= tracks.Select(t => t.Artwork).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
            store.Add(staging, tracks);
            progress.Report($"Importing {name}: {count} tracks · unavailable or unverified: {unavailable}");
            next = Text(page.RootElement, "next_href");
            if (next != null && !cursors.Add(next)) throw new CatalogException("SoundCloud repeated a track page. Partial import saved.");
            if (count >= 20000 && next != null) throw new CatalogException("A list is limited to 20,000 imported tracks. Partial import saved.");
        } while (next != null);
        bool complete = expected < 0 || count >= expected;
        string local = store.PublishAccountImport(staging, "soundcloud-account:" + user + ":" + sourceId, complete);
        store.SetArtwork(local, artwork);
        store.SetCreatedAt(local, createdAt);
        return complete;
    }
    public void Dispose() => http.Dispose();
}
