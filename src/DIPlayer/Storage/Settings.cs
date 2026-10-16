using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DIPlayer.Core;
namespace DIPlayer.Storage;

public sealed class Settings
{
    public const string DefaultServer = "https://158-180-35-133.sslip.io/diplayer/";
    public string? Server { get; set; } = DefaultServer;
    public string? ProtectedCredentials
    {
        get; set;
    }
    public double Volume { get; set; } = .6;
    static string FilePath => Path.Combine(LibraryStore.DataDirectory, "settings.json");
    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(LibraryStore.DataDirectory);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
    public void ImportCredentials(string file)
    {
        string raw = File.ReadAllText(file);
        string id = Regex.Match(raw, @"(?im)^\s*client\s*id\s*[:=]\s*(\S+)").Groups[1].Value;
        string secret = Regex.Match(raw, @"(?im)^\s*client\s*secret\s*[:=]\s*(\S+)").Groups[1].Value;
        if (id.Length == 0 || secret.Length == 0)
            throw new CatalogException("The file must contain client id: … and client secret: …");
        ProtectedCredentials = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { id, secret })), null, DataProtectionScope.CurrentUser));
        Server = null;
        Save();
    }
    public ICatalog CreateCatalog()
    {
        if (!string.IsNullOrWhiteSpace(Server))
            return new RemoteCatalog(Server);
        if (ProtectedCredentials == null)
            throw new CatalogException("Set a catalog server in settings. You can import your own keys for private testing.");
        var data = JsonSerializer.Deserialize<string[]>(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedCredentials), null, DataProtectionScope.CurrentUser))!;
        return new SoundCloudCatalog(data[0], data[1]);
    }
}
