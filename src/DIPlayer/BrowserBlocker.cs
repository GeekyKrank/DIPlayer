using System.IO.Compression;
using System.Security.Cryptography;

namespace DIPlayer;

public static class BrowserBlocker
{
    public static string Prepare(string dataDirectory, bool repair = false)
    {
        string archive = Path.Combine(AppContext.BaseDirectory, "Browser", "uBOLite.zip");
        using (var input = File.OpenRead(archive))
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals("f2ff2b53deb4fba68e9f2898ac8d21d0c26b7a169655647ee34b0a59783c628e", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Browser blocker package checksum mismatch.");
        string root = Path.Combine(dataDirectory, "browser-extension");
        string destination = Path.Combine(root, "ubol-2026.1012.1545" + (repair ? "-verified" : ""));
        if (File.Exists(Path.Combine(destination, "manifest.json"))) return destination;
        Directory.CreateDirectory(root);
        string staging = Path.Combine(root, Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(archive, staging);
        if (!File.Exists(Path.Combine(staging, "manifest.json"))) throw new IOException("Browser blocker manifest is missing.");
        Directory.Move(staging, destination);
        return destination;
    }
}
