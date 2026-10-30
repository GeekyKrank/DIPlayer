using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
namespace DIPlayer.SystemIntegration;

// Local Discord IPC only: no account token or background web service.
public sealed class DiscordPresence : IDisposable
{
    readonly string pipePrefix;
    public DiscordPresence(string pipePrefix = "discord-ipc-") => this.pipePrefix = pipePrefix;
    NamedPipeClientStream? pipe;
    string? applicationId;
    public string Status { get; private set; } = "Disabled";
    public void Dispose() { pipe?.Dispose(); pipe = null; applicationId = null; }
    public async Task Update(string id, object? activity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var token = timeout.Token;
        try
        {
            if (applicationId != id) Dispose();
            if (pipe == null)
            {
                for (int i = 0; i < 10; i++)
                {
                    var candidate = new NamedPipeClientStream(".", pipePrefix + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    try { await candidate.ConnectAsync(100, token); pipe = candidate; break; }
                    catch (TimeoutException) { candidate.Dispose(); }
                    catch { candidate.Dispose(); throw; }
                }
                if (pipe == null) throw new IOException();
                applicationId = id;
                await Send(0, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = id }), token);
                using var ready = await Receive(token);
                if (!ready.RootElement.TryGetProperty("evt", out var evt) || evt.GetString() != "READY") throw new IOException();
            }
            string nonce = Guid.NewGuid().ToString("N");
            await Send(1, JsonSerializer.SerializeToUtf8Bytes(new { cmd = "SET_ACTIVITY", args = new { pid = Environment.ProcessId, activity }, nonce }), token);
            for (int i = 0; i < 16; i++)
            {
                using var reply = await Receive(token);
                var root = reply.RootElement;
                if (!root.TryGetProperty("nonce", out var n) || n.GetString() != nonce) continue;
                if (root.TryGetProperty("evt", out var evt) && evt.GetString() == "ERROR") throw new IOException();
                Status = activity == null ? "Connected · nothing playing" : "Connected · sharing current track";
                return;
            }
            throw new IOException();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Dispose();
            Status = "Not connected · open Discord";
        }
    }
    async Task Send(int opcode, byte[] payload, CancellationToken token)
    {
        byte[] frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        await pipe!.WriteAsync(frame, token);
    }
    async Task<JsonDocument> Receive(CancellationToken token)
    {
        for (int i = 0; i < 16; i++)
        {
            byte[] header = new byte[8];
            await pipe!.ReadExactlyAsync(header, token);
            int opcode = BinaryPrimitives.ReadInt32LittleEndian(header), length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (length < 0 || length > 65536) throw new IOException();
            byte[] payload = new byte[length];
            await pipe.ReadExactlyAsync(payload, token);
            if (opcode == 3) { await Send(4, payload, token); continue; }
            if (opcode != 1) throw new IOException();
            return JsonDocument.Parse(payload);
        }
        throw new IOException();
    }
    public static object Activity(string title, string artist, string service, double position, double duration, string? artwork = null)
    {
        string Clip(string value) => string.Concat(value.EnumerateRunes().Take(120));
        var result = new Dictionary<string, object> { ["type"] = 2, ["details"] = Clip(title), ["state"] = Clip(string.IsNullOrWhiteSpace(artist) ? service : artist) };
        // Discord fetches the public cover itself; local cache paths cannot be used.
        if (artwork?.Length <= 2048 && Uri.TryCreate(artwork, UriKind.Absolute, out var image)
            && image.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(image.UserInfo)
            && !image.IsLoopback)
            result["assets"] = new { large_image = image.AbsoluteUri, large_text = Clip(title) };
        if (double.IsFinite(position) && double.IsFinite(duration) && duration > position && position >= 0)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            result["timestamps"] = new { start = now - (long)position, end = now + (long)(duration - position) };
        }
        return result;
    }
}
