using System.Net;
using System.Text;
using System.Text.Json;
using DIPlayer.Core;
using DIPlayer.Server;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
static async Task Rejected(Func<Task> action, string label) { try { await action(); throw new Exception("Accepted: " + label); } catch (CatalogException) { Console.WriteLine("PASS " + label); } }
var wire = new Wire();
using var connect = new AccountConnect("test-client", "test-secret", "https://example.test/callback", new HttpClient(wire));
var start = JsonSerializer.SerializeToElement(connect.Start());
string id = start.GetProperty("id").GetString()!, secret = start.GetProperty("secret").GetString()!, url = start.GetProperty("url").GetString()!;
Check(url.Contains("code_challenge_method=S256") && url.Contains("state=" + id) && !url.Contains("test-secret"), "PKCE authorization URL contains state, never the client secret");
await Rejected(() => Task.Run(() => connect.Status(id, "Bearer wrong")), "wrong session secret rejected");
Check((await connect.Callback("invalid", "code", null, default)).Contains("expired"), "unknown callback state rejected");
Check(wire.Calls == 0, "invalid sessions do not contact SoundCloud");
await connect.Callback(id, "test-code", null, default);
var ready = JsonSerializer.SerializeToElement(connect.Status(id, "Bearer " + secret));
Check(ready.GetProperty("status").GetString() == "ready" && ready.GetProperty("userId").GetString() == "42", "callback authenticates user and publishes ready state");
await connect.Callback(id, "test-code", null, default);
Check(wire.CodeExchanges == 1, "authorization code exchanged once only");
await connect.Data(id, "Bearer " + secret, "likes", null, null, default);
Check(wire.Refreshes == 1 && wire.LastPath.StartsWith("/me/likes/tracks"), "expired token refreshes once and likes use the account endpoint");
Check(wire.LastPath.Contains("limit=200"), "account imports request up to 200 tracks per page");
int calls = wire.Calls;
await Rejected(async () => { await connect.Data(id, "Bearer " + secret, "likes", null, "https://evil.test/me/likes/tracks", default); }, "cross-host pagination rejected");
await Rejected(async () => { await connect.Data(id, "Bearer " + secret, "likes", null, "https://api.soundcloud.com/me/playlists?offset=1", default); }, "cross-endpoint pagination rejected");
Check(calls == wire.Calls, "invalid cursors never receive an OAuth token");
await connect.Disconnect(id, "Bearer " + secret, default);
Check(wire.Signouts == 1, "disconnect revokes SoundCloud session");
await Rejected(() => Task.Run(() => connect.Status(id, "Bearer " + secret)), "disconnected session cannot be reused");
var denied = JsonSerializer.SerializeToElement(connect.Start());
string deniedId = denied.GetProperty("id").GetString()!, deniedSecret = denied.GetProperty("secret").GetString()!;
await connect.Callback(deniedId, null, "access_denied", default);
Check(JsonSerializer.SerializeToElement(connect.Status(deniedId, "Bearer " + deniedSecret)).GetProperty("status").GetString() == "error", "denied consent is reported without account access");
Console.WriteLine("All OAuth contract checks passed (mock transport; no real user consent).");

sealed class Wire : HttpMessageHandler
{
    public int Calls, CodeExchanges, Refreshes, Signouts;
    public string LastPath = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++; LastPath = request.RequestUri!.PathAndQuery;
        string json;
        if (request.RequestUri.Host == "secure.soundcloud.com" && request.RequestUri.AbsolutePath == "/oauth/token")
        {
            string form = await request.Content!.ReadAsStringAsync(ct);
            if (!form.Contains("client_secret=test-secret")) throw new Exception("Missing confidential server credential");
            bool refresh = form.Contains("grant_type=refresh_token");
            if (refresh) Refreshes++; else { CodeExchanges++; if (!form.Contains("code_verifier=")) throw new Exception("Missing PKCE verifier"); }
            json = "{\"access_token\":\"test-token\",\"refresh_token\":\"test-refresh\",\"expires_in\":" + (refresh ? "3600" : "0") + "}";
        }
        else if (request.RequestUri.Host == "secure.soundcloud.com" && request.RequestUri.AbsolutePath == "/sign-out") { Signouts++; json = "{}"; }
        else
        {
            if (request.RequestUri.Host != "api.soundcloud.com" || request.Method != HttpMethod.Get || request.Headers.Authorization?.Parameter != "test-token") throw new Exception("Unsafe request");
            json = request.RequestUri.AbsolutePath == "/me" ? "{\"id\":42,\"username\":\"Contract test\"}" : "{\"collection\":[],\"next_href\":null}";
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
