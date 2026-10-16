using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DIPlayer.Core;

// Only this trusted adapter needs application credentials. Never embed these in a distributed binary.
public sealed class SoundCloudCatalog(string clientId, string clientSecret, string? tokenFile = null) : ICatalog, IDisposable
{
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    readonly SemaphoreSlim tokenLock = new(1, 1);
    string? token, refresh;
    DateTimeOffset expires;
    bool tokenLoaded;
    sealed record TokenState(string Token,string? Refresh,DateTimeOffset Expires);
    static readonly Uri Api = new("https://api.soundcloud.com");
    public void Dispose() { http.Dispose(); tokenLock.Dispose(); }
    async Task<string> Token(CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try
        {
            if (!tokenLoaded)
            {
                tokenLoaded=true;
                if(tokenFile!=null && File.Exists(tokenFile))
                {
                    var saved=JsonSerializer.Deserialize<TokenState>(await File.ReadAllTextAsync(tokenFile,ct));
                    if(saved!=null){token=saved.Token;refresh=saved.Refresh;expires=saved.Expires;}
                }
            }
            if (token != null && DateTimeOffset.UtcNow < expires) return token;
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://secure.soundcloud.com/oauth/token");
            if (refresh == null)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + clientSecret)));
                req.Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"] = "client_credentials" });
            }
            else req.Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["client_secret"] = clientSecret, ["refresh_token"] = refresh });
            using var res = await http.SendAsync(req, ct);
            await EnsureResponse(res,ct);
            using var json = JsonDocument.Parse(await ReadLimited(res.Content, 1024 * 1024, ct));
            token = json.RootElement.GetProperty("access_token").GetString()!;
            refresh = Text(json.RootElement, "refresh_token");
            expires = DateTimeOffset.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetDouble() - 120);
            if(tokenFile!=null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tokenFile))!);
                var tmp=tokenFile+".tmp";
                await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(new TokenState(token,refresh,expires)),ct);
                if(!OperatingSystem.IsWindows())File.SetUnixFileMode(tmp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                File.Move(tmp,tokenFile,true);
            }
            return token;
        }
        finally { tokenLock.Release(); }
    }
    public static async Task<byte[]> ReadLimited(HttpContent content, int max, CancellationToken ct)
    {
        if (content.Headers.ContentLength > max) throw new CatalogException("The response exceeds the size limit.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int n;
        while ((n = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + n > max) throw new CatalogException("The response exceeds the size limit.");
            output.Write(buffer, 0, n);
        }
        return output.ToArray();
    }
    internal static void Ensure(HttpStatusCode status)
    {
        if ((int)status < 400) return;
        throw new CatalogException((int)status switch {
            401 => "App authorization expired or credentials are invalid.",
            403 => "SoundCloud denied access; no specific reason was provided.",
            404 => "Track or playlist not found. It may have been removed or made private.",
            410 => "The resource was removed (HTTP 410 Gone).",
            429 => "SoundCloud rate limit reached. Wait before retrying.",
            >=500 => "SoundCloud is temporarily unavailable.",
            _ => $"SoundCloud rejected the request (HTTP {(int)status})."
        }, (int)status);
    }
    public static string? ExplicitRestriction(string? reason)=>reason?.ToLowerInvariant() switch
    {
        "geo_blocked" or "geoblocked" or "region_restricted" or "not_available_in_region"=>"This track is unavailable in your region.",
        "subscription_required" or "payment_required" or "paywall"=>"This track requires a paid SoundCloud subscription.",
        "track_deleted" or "deleted"=>"This track has been removed.",
        _=>null
    };
    static async Task EnsureResponse(HttpResponseMessage response,CancellationToken ct)
    {
        if((int)response.StatusCode<400)return;
        try
        {
            using var body=JsonDocument.Parse(await ReadLimited(response.Content,65536,ct));
            foreach(string key in new[]{"error_code","reason","error"})
                if(body.RootElement.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String&&ExplicitRestriction(value.GetString()) is {} message)
                    throw new CatalogException(message,(int)response.StatusCode);
        }
        catch(JsonException){}
        Ensure(response.StatusCode);
    }
    static Uri SafeApi(string path)
    {
        var uri = new Uri(Api, path);
        if (uri.Scheme != "https" || uri.Host != Api.Host || !uri.IsDefaultPort) throw new CatalogException("Unsafe API page URL.");
        return uri;
    }
    async Task<JsonDocument> Json(string path, CancellationToken ct)
    {
        Uri uri=SafeApi(path);
        for(int redirects=0;redirects<5;redirects++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", await Token(ct));
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureResponse(res,ct);
            if((int)res.StatusCode is >=300 and <400 && res.Headers.Location!=null)
            {uri=SafeApi(new Uri(uri,res.Headers.Location).AbsoluteUri);continue;}
            return JsonDocument.Parse(await ReadLimited(res.Content, 2 * 1024 * 1024, ct));
        }
        throw new CatalogException("Too many catalog redirects.");
    }
    public async Task<TrackPage> Search(string query, string? next, CancellationToken ct)
    {
        if(next!=null && SafeApi(next).AbsolutePath!="/tracks")throw new CatalogException("Invalid search cursor.");
        using var json = await Json(next ?? $"/tracks?q={Uri.EscapeDataString(query)}&limit=25&linked_partitioning=true", ct);
        return Page(json.RootElement);
    }
    public static string PublicPlaylistUrl(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var u) || u.Scheme != "https" || u.Host is not ("soundcloud.com" or "www.soundcloud.com") || !u.IsDefaultPort || u.UserInfo.Length != 0)
            throw new CatalogException("Enter https://soundcloud.com/artist/sets/playlist.");
        var parts = u.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 3 || parts[1] != "sets" || u.Query.Contains("secret", StringComparison.OrdinalIgnoreCase)) throw new CatalogException("Only public /artist/sets/playlist URLs are supported.");
        return "https://soundcloud.com" + u.AbsolutePath;
    }
    public async Task<PlaylistInfo> ResolvePlaylist(string url, CancellationToken ct)
    {
        using var json = await Json("/resolve?url=" + Uri.EscapeDataString(PublicPlaylistUrl(url)), ct);
        var r = json.RootElement;
        if (Text(r,"kind") != "playlist" || Text(r,"sharing") != "public") throw new CatalogException("The URL does not point to a public playlist.");
        return new PlaylistInfo(Id(r), Text(r,"title") ?? "Import", Number(r,"track_count"), true);
    }
    public async Task<TrackPage> PlaylistTracks(string id, string? next, CancellationToken ct)
    {
        if(next!=null && SafeApi(next).AbsolutePath!=$"/playlists/{Uri.EscapeDataString(id)}/tracks")throw new CatalogException("Invalid playlist cursor.");
        using var json = await Json(next ?? $"/playlists/{Uri.EscapeDataString(id)}/tracks?limit=25&linked_partitioning=true", ct);
        return Page(json.RootElement);
    }
    public async Task<Track> GetTrack(string id, CancellationToken ct)
    {
        using var json = await Json($"/tracks/{Uri.EscapeDataString(id)}", ct);
        return ParseTrack(json.RootElement);
    }
    public async Task<List<Track>> GetTracks(IReadOnlyList<string> ids,CancellationToken ct)
    {
        if(ids.Count is <1 or >25)throw new CatalogException("A page must contain between 1 and 25 tracks.");
        using var json=await Json("/tracks?ids="+Uri.EscapeDataString(string.Join(",",ids))+"&limit=25&linked_partitioning=true",ct);
        return Page(json.RootElement).Tracks;
    }
    public async Task<string> Stream(string id, CancellationToken ct)
    {
        var track = await GetTrack(id, ct);
        if (track.Access != "playable") throw new CatalogException(track.Issue ?? (track.Access == "preview" ? "Only a preview is available; full playback is unavailable." : "This track is unavailable outside SoundCloud; no reason was provided."));
        using var json = await Json($"/tracks/{Uri.EscapeDataString(id)}/streams", ct);
        var source = Text(json.RootElement,"hls_mp3_128_url");
        if (source == null) throw new CatalogException("This track has no supported MP3 HLS stream. AAC is not supported yet.");
        var uri = new Uri(source);
        // Stream endpoints are authenticated API redirects. OAuth is NEVER forwarded to a CDN.
        for (int redirect = 0; redirect < 5; redirect++)
        {
            if (uri.Host != Api.Host) { ValidateCdn(uri); return uri.AbsoluteUri; }
            using var req = new HttpRequestMessage(HttpMethod.Get, SafeApi(uri.AbsoluteUri));
            req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", await Token(ct));
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureResponse(res,ct);
            if ((int)res.StatusCode is >=300 and <400 && res.Headers.Location != null) { uri = new Uri(uri, res.Headers.Location); continue; }
            throw new CatalogException("Unexpected response while resolving the audio stream.");
        }
        throw new CatalogException("Too many audio redirects.");
    }
    public static void ValidateCdn(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !(uri.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase)))
            throw new CatalogException("Invalid audio CDN URL.");
    }
    static string? Text(JsonElement r, string name) => r.TryGetProperty(name,out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    static int Number(JsonElement r, string name) => r.TryGetProperty(name,out var p) && p.TryGetInt32(out int n) ? n : 0;
    static string Id(JsonElement r) => r.TryGetProperty("id",out var id) ? id.ToString() : Text(r,"urn") ?? "";
    public static Track ParseTrack(JsonElement r)
    {
        string id = Id(r);
        bool missing = !r.TryGetProperty("title",out _);
        string artist = r.TryGetProperty("user",out var user) ? Text(user,"username") ?? "Unknown artist" : "Unknown artist";
        return new Track(id, Text(r,"title") ?? $"Unavailable track {id}", Text(r,"metadata_artist") ?? artist, Number(r,"duration"), Text(r,"artwork_url"), Text(r,"permalink_url") ?? "", Text(r,"access") ?? "unknown", missing ? "Track details unavailable" : ExplicitRestriction(Text(r,"access_reason")));
    }
    static TrackPage Page(JsonElement root)
    {
        var list = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
        return new TrackPage(list.EnumerateArray().Select(ParseTrack).ToList(), root.ValueKind == JsonValueKind.Array ? null : Text(root,"next_href"));
    }
}
