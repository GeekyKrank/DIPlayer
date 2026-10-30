using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using DIPlayer.SystemIntegration;
static class DiscordChecks
{
    public static async Task Run()
    {
        foreach (string cover in new[] { "https://i1.sndcdn.com/artworks-test-large.jpg", "https://lh3.googleusercontent.com/example=w544-h544-l90-rj" })
        {
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordPresence.Activity("Track", "Artist", "Service", 0, 100, cover)));
            if (payload.RootElement.GetProperty("assets").GetProperty("large_image").GetString() != cover) throw new Exception("Discord service artwork URL");
        }
        foreach (string? cover in new string?[] { null, "", "file:///C:/cover.jpg", "data:image/png;base64,abc", "https://user:secret@example.com/cover.jpg" })
        {
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordPresence.Activity("Track", "Artist", "Service", 0, 100, cover)));
            if (payload.RootElement.TryGetProperty("assets", out _)) throw new Exception("Invalid Discord artwork");
        }
        string prefix = "diplayer-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(prefix + "0", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task<(int opcode, JsonDocument json)> Read()
        {
            byte[] h = new byte[8]; await server.ReadExactlyAsync(h, timeout.Token);
            int length = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(4));
            byte[] p = new byte[length]; await server.ReadExactlyAsync(p, timeout.Token);
            return (BinaryPrimitives.ReadInt32LittleEndian(h), JsonDocument.Parse(p));
        }
        async Task Send(int opcode, object value)
        {
            byte[] p = JsonSerializer.SerializeToUtf8Bytes(value), h = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(h, opcode); BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), p.Length);
            // Fragment frames to verify stream reads rather than assuming message boundaries.
            await server.WriteAsync(h.AsMemory(0, 3), timeout.Token);
            await server.WriteAsync(h.AsMemory(3), timeout.Token);
            await server.WriteAsync(p, timeout.Token);
        }
        var peer = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            var handshake = await Read();
            using (handshake.json) if (handshake.opcode != 0 || handshake.json.RootElement.GetProperty("client_id").GetString() != "1561330201911103572") throw new Exception("Discord handshake");
            await Send(1, new { evt = "READY" });
            for (int i = 0; i < 2; i++)
            {
                var request = await Read();
                using var json = request.json;
                var root = json.RootElement;
                var activity = root.GetProperty("args").GetProperty("activity");
                if (i == 0 && (activity.GetProperty("details").GetString() != "Песня 🎵" || activity.GetProperty("type").GetInt32() != 2 || !activity.TryGetProperty("timestamps", out _))) throw new Exception("Discord track metadata");
                if (i == 0 && activity.GetProperty("assets").GetProperty("large_image").GetString() != "https://i1.sndcdn.com/artworks-test-large.jpg") throw new Exception("Discord cover missing from IPC");
                if (i == 1 && activity.ValueKind != JsonValueKind.Null) throw new Exception("Discord pause clearing");
                await Send(3, new { ping = true });
                var pong = await Read(); using (pong.json) if (pong.opcode != 4) throw new Exception("Discord PONG");
                await Send(1, new { nonce = root.GetProperty("nonce").GetString(), cmd = "SET_ACTIVITY", data = new { } });
            }
        });
        using var client = new DiscordPresence(prefix);
        await client.Update("1561330201911103572", DiscordPresence.Activity("Песня 🎵", "Artist", "SoundCloud", 12, 120, "https://i1.sndcdn.com/artworks-test-large.jpg"));
        if (!client.Status.StartsWith("Connected")) throw new Exception(client.Status);
        await client.Update("1561330201911103572", null);
        await peer;
        if (!client.Status.Contains("nothing playing")) throw new Exception(client.Status);
        server.Dispose();
        await client.Update("1561330201911103572", null);
        if (!client.Status.StartsWith("Not connected")) throw new Exception("Discord disconnect recovery");
        Console.WriteLine("PASS Discord IPC handshake, fragmented frames, Unicode track, SoundCloud/YouTube cover URLs, timestamps, ping/pong, pause clearing and disconnect recovery");
    }
}
