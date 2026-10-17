using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DIPlayer.Core;

namespace DIPlayer.Server;

// One-time library import. User tokens live only in bounded, expiring RAM sessions.
public sealed class AccountConnect(string clientId, string clientSecret, string redirectUri, HttpClient http) : IDisposable
{
    sealed class Session
    {
        public required string Secret, Verifier;
        public DateTimeOffset Expires = DateTimeOffset.UtcNow.AddMinutes(10), TokenExpires;
        public string? Token, Refresh, UserId, Name, Error;
        public bool CallbackUsed;
        public SemaphoreSlim Gate = new(1);
    }
    readonly ConcurrentDictionary<string, Session> sessions = new();
    readonly object admission = new();
    System.Threading.Timer? cleanup;
    static string Random() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string? Text(JsonElement r, string name) => r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    void Prune() { foreach (var pair in sessions) if (pair.Value.Expires < DateTimeOffset.UtcNow) sessions.TryRemove(pair.Key, out _); }
    public object Start()
    {
        lock (admission)
        {
            Prune();
            cleanup ??= new System.Threading.Timer(_ => Prune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            if (sessions.Count >= 32) throw new CatalogException("Account import is busy. Please try again later.", 429);
            string id = Random(); var session = new Session { Secret = Random(), Verifier = Random() };
            sessions[id] = session;
            string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(session.Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string url = "https://secure.soundcloud.com/authorize?response_type=code&client_id=" + Uri.EscapeDataString(clientId) + "&redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&state=" + id + "&code_challenge_method=S256&code_challenge=" + challenge;
            return new { id, secret = session.Secret, url, expiresIn = 600 };
        }
    }
    Session Authorized(string id, string? header)
    {
        Prune();
        if (!sessions.TryGetValue(id, out var session) || header == null || !header.StartsWith("Bearer ") ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[7..]), Encoding.UTF8.GetBytes(session.Secret)))
            throw new CatalogException("This sign-in session has expired. Sign in again.", 401);
        return session;
    }
    public object Status(string id, string? header)
    {
        var s = Authorized(id, header);
        return new { status = s.Error != null ? "error" : s.Token != null && s.UserId != null ? "ready" : "waiting", userId = s.UserId, name = s.Name, message = s.Error };
    }
    async Task SetToken(Session session, Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = clientId; form["client_secret"] = clientSecret;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://secure.soundcloud.com/oauth/token") { Content = new FormUrlEncodedContent(form) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new CatalogException("SoundCloud could not complete sign-in. Check the registered callback URL and try again.", 401);
        using var json = JsonDocument.Parse(await SoundCloudCatalog.ReadLimited(response.Content, 64 * 1024, ct));
        session.Token = Text(json.RootElement, "access_token") ?? throw new CatalogException("SoundCloud returned an invalid token response.");
        session.Refresh = Text(json.RootElement, "refresh_token");
        session.TokenExpires = DateTimeOffset.UtcNow.AddSeconds(json.RootElement.TryGetProperty("expires_in", out var n) ? n.GetInt32() : 3600);
    }
    async Task<JsonDocument> Read(Session s, string path, CancellationToken ct)
    {
        var uri = new Uri("https://api.soundcloud.com" + path);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", s.Token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new CatalogException(response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "SoundCloud sign-in expired. Sign in again.",
            System.Net.HttpStatusCode.Forbidden => "SoundCloud did not allow access to this list.",
            System.Net.HttpStatusCode.NotFound => "SoundCloud could not find this playlist.",
            System.Net.HttpStatusCode.TooManyRequests => "SoundCloud request limit reached. Please try again later.",
            _ => "SoundCloud could not load the account library. Please try again."
        }, (int)response.StatusCode);
        return JsonDocument.Parse(await SoundCloudCatalog.ReadLimited(response.Content, 2 * 1024 * 1024, ct));
    }
    public async Task<string> Callback(string? state, string? code, string? error, CancellationToken ct)
    {
        Prune();
        if (state == null || !sessions.TryGetValue(state, out var s)) return "Sign-in expired. Return to DIPlayer and try again.";
        await s.Gate.WaitAsync(ct);
        try
        {
            if (s.CallbackUsed) return "This sign-in has already been handled. Return to DIPlayer.";
            s.CallbackUsed = true;
            if (error != null || string.IsNullOrEmpty(code)) { s.Error = "Sign-in was canceled or denied."; return s.Error; }
            try
            {
                await SetToken(s, new() { ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = s.Verifier, ["redirect_uri"] = redirectUri }, ct);
                using var me = await Read(s, "/me", ct);
                s.UserId = me.RootElement.GetProperty("id").ToString(); s.Name = Text(me.RootElement, "username") ?? "SoundCloud";
                s.Expires = DateTimeOffset.UtcNow.AddMinutes(55);
                return "Signed in. Return to DIPlayer to import your playlists and Likes. You may close this tab.";
            }
            catch (Exception e) when (e is CatalogException or HttpRequestException or JsonException or OperationCanceledException)
            {
                s.Token = null; s.Refresh = null; s.Error = e is CatalogException ? e.Message : "Could not finish sign-in. Return to DIPlayer and try again.";
                return s.Error;
            }
        }
        finally { s.Gate.Release(); }
    }
    public async Task<JsonElement> Data(string id, string? header, string kind, string? playlist, string? next, CancellationToken ct)
    {
        var s = Authorized(id, header);
        await s.Gate.WaitAsync(ct);
        try
        {
            if (s.Token == null || s.UserId == null) throw new CatalogException("Finish signing in before importing.", 401);
            string path = kind switch { "playlists" => "/me/playlists", "liked-playlists" => "/me/likes/playlists", "likes" => "/me/likes/tracks", "tracks" when playlist != null && playlist.Length <= 100 && playlist.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_') => "/playlists/" + Uri.EscapeDataString(playlist) + "/tracks", _ => throw new CatalogException("Invalid library request.") };
            string target = path + "?limit=200&linked_partitioning=true" + (kind is "playlists" or "liked-playlists" ? "&show_tracks=false" : "&access=playable,preview,blocked");
            if (!string.IsNullOrEmpty(next))
            {
                if (!Uri.TryCreate(next, UriKind.Absolute, out var cursor) || cursor.Scheme != "https" || cursor.Host != "api.soundcloud.com" || !cursor.IsDefaultPort || cursor.UserInfo.Length != 0 || cursor.AbsolutePath != path || cursor.Query.Length > 4096)
                    throw new CatalogException("Invalid account-library page.");
                target = cursor.PathAndQuery;
            }
            if (s.TokenExpires < DateTimeOffset.UtcNow.AddSeconds(30) && s.Refresh != null)
                await SetToken(s, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = s.Refresh }, ct);
            using var json = await Read(s, target, ct);
            return json.RootElement.Clone();
        }
        finally { s.Gate.Release(); }
    }
    public async Task Disconnect(string id, string? header, CancellationToken ct)
    {
        var s = Authorized(id, header);
        sessions.TryRemove(id, out _);
        await s.Gate.WaitAsync(ct);
        try
        {
            if (s.Token != null)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://secure.soundcloud.com/sign-out") { Content = JsonContent.Create(new { access_token = s.Token }) };
                    using var response = await http.SendAsync(request, ct);
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) { }
            }
            s.Token = null; s.Refresh = null;
        }
        finally { s.Gate.Release(); }
    }
    public void Dispose() { sessions.Clear(); cleanup?.Dispose(); http.Dispose(); }
}
