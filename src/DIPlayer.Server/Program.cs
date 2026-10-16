using DIPlayer.Core;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
// Do not log query strings, signed audio URLs, Authorization, or application keys.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 0; o.Limits.MaxRequestLineSize = 8192; o.Limits.MaxConcurrentConnections = 32; });
string clientId = Environment.GetEnvironmentVariable("SOUNDCLOUD_CLIENT_ID") ?? throw new InvalidOperationException("Set SOUNDCLOUD_CLIENT_ID");
string clientSecret = Environment.GetEnvironmentVariable("SOUNDCLOUD_CLIENT_SECRET") ?? throw new InvalidOperationException("Set SOUNDCLOUD_CLIENT_SECRET");
builder.Services.AddSingleton(new SoundCloudCatalog(clientId, clientSecret, Environment.GetEnvironmentVariable("DIPLAYER_TOKEN_FILE") ?? "private/token.json"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, CatalogJsonContext.Default));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Add(IPAddress.Loopback);
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetTokenBucketLimiter("budget", _ => new TokenBucketRateLimiterOptions { TokenLimit = 6, TokensPerPeriod = 2, ReplenishmentPeriod = TimeSpan.FromSeconds(1), AutoReplenishment = true, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter("daily", _ => new FixedWindowRateLimiterOptions { PermitLimit = 3000, Window = TimeSpan.FromDays(1), QueueLimit = 0, AutoReplenishment = true })),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })));
    o.OnRejected = async (ctx, ct) => { ctx.HttpContext.Response.Headers.RetryAfter = "60"; await ctx.HttpContext.Response.WriteAsJsonAsync(new { message = "Catalog server rate limit reached. Retry in a minute." }, ct); };
});
var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (ctx.Request.Query.Any(q => q.Value.ToString().Length > 4096))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new
        {
            message = "Parameter is too long."
        });
        return;
    }
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(20));
    ctx.RequestAborted = timeout.Token;
    try
    {
        await next();
    }
    catch (CatalogException e) { ctx.Response.StatusCode = e.Status is >= 400 and <= 599 ? e.Status.Value : 400; await ctx.Response.WriteAsJsonAsync(new { message = e.Message }); }
    catch (OperationCanceledException) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 504; await ctx.Response.WriteAsJsonAsync(new { message = "The server timed out." }, CancellationToken.None); } }
    catch (HttpRequestException) { ctx.Response.StatusCode = 502; await ctx.Response.WriteAsJsonAsync(new { message = "The catalog server cannot reach SoundCloud." }); }
});
app.UseRateLimiter();
static string? Next(string? next) => string.IsNullOrEmpty(next) ? null : next;
app.MapGet("/health", () => new { service = "DIPlayer catalogue", status = "ok" });
app.MapGet("/search", async (string q, string? next, SoundCloudCatalog api, CancellationToken ct) =>
{
    if (q.Length is < 1 or > 200)
        throw new CatalogException("Search: enter between 1 and 200 characters.");
    return await api.Search(q, Next(next), ct);
});
app.MapGet("/resolve", async (string url, SoundCloudCatalog api, CancellationToken ct) => await api.ResolvePlaylist(url, ct));
app.MapGet("/playlists/{id}/tracks", async (string id, string? next, SoundCloudCatalog api, CancellationToken ct) => await api.PlaylistTracks(id, Next(next), ct));
app.MapGet("/tracks/{id}", async (string id, SoundCloudCatalog api, CancellationToken ct) => await api.GetTrack(id, ct));
app.MapGet("/tracks", async (string ids, SoundCloudCatalog api, CancellationToken ct) => await api.GetTracks(ids.Split(',', StringSplitOptions.RemoveEmptyEntries), ct));
app.MapGet("/tracks/{id}/stream", async (string id, SoundCloudCatalog api, CancellationToken ct) => Results.Json(await api.Stream(id, ct)));
app.Run();
