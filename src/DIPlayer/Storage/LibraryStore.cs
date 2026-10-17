using DIPlayer.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace DIPlayer.Storage;

public sealed record PlaylistHeader(string Id, string Name, int Count, bool Complete, bool HasCover = false, string? Artwork = null)
{
    public override string ToString() => Name;
}
public sealed record LibraryRow(long EntryId, Track Track);
public sealed class LibraryStore : IDisposable
{
    readonly SqliteConnection db;
    readonly string snapshotPath;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIPlayer");
    public LibraryStore(string? directory = null)
    {
        string folder = directory ?? DataDirectory;
        Directory.CreateDirectory(folder);
        snapshotPath = Path.Combine(folder, "library.saved.db");
        string databasePath = Path.Combine(folder, "library.db");
        if (!File.Exists(databasePath) && File.Exists(snapshotPath)) File.Copy(snapshotPath, databasePath);
        db = new SqliteConnection("Data Source=" + databasePath);
        db.Open();
        Exec("PRAGMA synchronous=FULL");
        Exec("CREATE TABLE IF NOT EXISTS playlist_dates(playlist TEXT PRIMARY KEY,created INTEGER NOT NULL)");
        Exec("CREATE TABLE IF NOT EXISTS hidden_entries(entry INTEGER PRIMARY KEY)");
        Exec("CREATE TABLE IF NOT EXISTS playlist_artwork(playlist TEXT PRIMARY KEY,url TEXT NOT NULL)");
        Exec("CREATE TABLE IF NOT EXISTS playlist_covers(playlist TEXT PRIMARY KEY,image BLOB NOT NULL)");
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS playlists(id TEXT PRIMARY KEY,name TEXT NOT NULL,source TEXT,complete INTEGER NOT NULL DEFAULT 1); CREATE TABLE IF NOT EXISTS entries(id INTEGER PRIMARY KEY,playlist TEXT NOT NULL,position INTEGER NOT NULL,json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_entries ON entries(playlist,position); CREATE TABLE IF NOT EXISTS queue(position INTEGER PRIMARY KEY,json TEXT NOT NULL);");
        // Persist user-created references/order, not a cross-session copy of SoundCloud metadata.
        using var version = Command("PRAGMA user_version");
        if (Convert.ToInt32(version.ExecuteScalar()) < 1)
        {
            using var tx = db.BeginTransaction();
            Exec("UPDATE entries SET json=json_object('Id',json_extract(json,'$.Id')); UPDATE queue SET json=json_object('Id',json_extract(json,'$.Id')); PRAGMA user_version=1;");
            tx.Commit();
        }
        Exec("UPDATE queue SET json=json_object('Id',json_extract(json,'$.Id')) WHERE instr(json,'\"Title\"')>0;");
    }
    void Exec(string sql, params (string, object?)[] args)
    {
        using var c = Command(sql, args);
        c.ExecuteNonQuery();
    }
    SqliteCommand Command(string sql, params (string, object?)[] args)
    {
        var c = db.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in args)
            c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return c;
    }
    public List<PlaylistHeader> Playlists(bool oldestFirst = false)
    {
        using var c = Command("SELECT p.id,p.name,COUNT(e.id),p.complete,EXISTS(SELECT 1 FROM playlist_covers c WHERE c.playlist=p.id),(SELECT url FROM playlist_artwork a WHERE a.playlist=p.id) FROM playlists p LEFT JOIN entries e ON e.playlist=p.id AND NOT EXISTS(SELECT 1 FROM hidden_entries h WHERE h.entry=e.id) GROUP BY p.id ORDER BY CASE WHEN p.source LIKE 'soundcloud-account:%:likes' THEN 0 ELSE 1 END,(SELECT created FROM playlist_dates d WHERE d.playlist=p.id) IS NULL,(SELECT created FROM playlist_dates d WHERE d.playlist=p.id) " + (oldestFirst ? "ASC" : "DESC") + ",p.rowid " + (oldestFirst ? "ASC" : "DESC"));
        using var r = c.ExecuteReader();
        var list = new List<PlaylistHeader>();
        while (r.Read())
            list.Add(new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetBoolean(3), r.GetBoolean(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }
    public string Create(string name, string? source = null, bool complete = true)
    {
        string id = Guid.NewGuid().ToString("N");
        Exec("INSERT INTO playlists VALUES($id,$name,$source,$complete)", ("$id", id), ("$name", name), ("$source", source), ("$complete", complete ? 1 : 0));
        SaveSnapshot();
        return id;
    }
    public void Rename(string id, string name) => Exec("UPDATE playlists SET name=$name WHERE id=$id", ("$name", name), ("$id", id));
    public void SetArtwork(string id, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) Exec("DELETE FROM playlist_artwork WHERE playlist=$id", ("$id", id));
        else Exec("INSERT INTO playlist_artwork VALUES($id,$url) ON CONFLICT(playlist) DO UPDATE SET url=$url", ("$id", id), ("$url", url));
    }
    public void SetCreatedAt(string id, string? createdAt)
    {
        if (string.IsNullOrWhiteSpace(createdAt)) return;
        string normalized = System.Text.RegularExpressions.Regex.Replace(createdAt, @"([+-]\d{2})(\d{2})$", "$1:$2");
        if (!DateTimeOffset.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)) return;
        Exec("INSERT INTO playlist_dates VALUES($id,$created) ON CONFLICT(playlist) DO UPDATE SET created=$created", ("$id", id), ("$created", date.ToUnixTimeSeconds()));
    }
    public byte[]? Cover(string id)
    {
        using var c = Command("SELECT image FROM playlist_covers WHERE playlist=$id", ("$id", id));
        return c.ExecuteScalar() as byte[];
    }
    public void Edit(string id, string name, bool changeCover, byte[]? image)
    {
        using var tx = db.BeginTransaction();
        Rename(id, name);
        if (changeCover)
        {
            if (image == null) Exec("DELETE FROM playlist_covers WHERE playlist=$id", ("$id", id));
            else Exec("INSERT INTO playlist_covers VALUES($id,$image) ON CONFLICT(playlist) DO UPDATE SET image=$image", ("$id", id), ("$image", image));
        }
        tx.Commit();
    }
    public void Complete(string id) => Exec("UPDATE playlists SET complete=1 WHERE id=$id", ("$id", id));
    public string PublishAccountImport(string staging, string source, bool complete)
    {
        using var tx = db.BeginTransaction();
        using var find = Command("SELECT id FROM playlists WHERE source=$source AND id<>$id LIMIT 1", ("$source", source), ("$id", staging));
        string? existing = find.ExecuteScalar() as string;
        if (existing != null && complete)
        {
            Exec("DELETE FROM hidden_entries WHERE entry IN (SELECT id FROM entries WHERE playlist=$id)", ("$id", existing));
            Exec("DELETE FROM entries WHERE playlist=$id", ("$id", existing));
            Exec("UPDATE entries SET playlist=$existing WHERE playlist=$staging", ("$existing", existing), ("$staging", staging));
            Exec("UPDATE playlists SET complete=1 WHERE id=$id", ("$id", existing));
            Exec("DELETE FROM playlists WHERE id=$id", ("$id", staging));
        }
        else
            Exec("UPDATE playlists SET source=$source,complete=$complete WHERE id=$id", ("$source", existing == null ? source : "partial:" + staging), ("$complete", complete ? 1 : 0), ("$id", staging));
        tx.Commit();
        SaveSnapshot();
        return existing != null && complete ? existing : staging;
    }
    public void Delete(string id)
    {
        using var tx = db.BeginTransaction();
        Exec("DELETE FROM hidden_entries WHERE entry IN (SELECT id FROM entries WHERE playlist=$id)", ("$id", id));
        Exec("DELETE FROM entries WHERE playlist=$id", ("$id", id));
        Exec("DELETE FROM playlist_covers WHERE playlist=$id", ("$id", id));
        Exec("DELETE FROM playlist_artwork WHERE playlist=$id", ("$id", id));
        Exec("DELETE FROM playlist_dates WHERE playlist=$id", ("$id", id));
        Exec("DELETE FROM playlists WHERE id=$id", ("$id", id));
        tx.Commit();
    }
    public List<LibraryRow> Page(string id, int page)
    {
        using var c = Command("SELECT id,json FROM entries WHERE playlist=$id AND NOT EXISTS(SELECT 1 FROM hidden_entries h WHERE h.entry=entries.id) ORDER BY position,id LIMIT 25 OFFSET $offset", ("$id", id), ("$offset", page * 25));
        using var r = c.ExecuteReader();
        var list = new List<LibraryRow>();
        while (r.Read())
            list.Add(new(r.GetInt64(0), Reference(r.GetString(1))));
        return list;
    }
    public void Add(string id, IEnumerable<Track> tracks)
    {
        using var tx = db.BeginTransaction();
        using var max = Command("SELECT COALESCE(MAX(position),-1)+1 FROM entries WHERE playlist=$id", ("$id", id));
        long pos = (long)max.ExecuteScalar()!;
        foreach (var track in tracks)
        {
            if (track.Issue == "Track details unavailable") continue;
            Exec("INSERT INTO entries(playlist,position,json) VALUES($id,$pos,$json)", ("$id", id), ("$pos", pos++), ("$json", JsonSerializer.Serialize(new
            {
                track.Id
            })));
        }
        tx.Commit();
        SaveSnapshot();
    }
    public void Remove(long entry)
    {
        Exec("DELETE FROM hidden_entries WHERE entry=$id", ("$id", entry));
        Exec("DELETE FROM entries WHERE id=$id", ("$id", entry));
    }
    public void Hide(long entry) => Exec("INSERT OR IGNORE INTO hidden_entries VALUES($id)", ("$id", entry));
    public void Move(long entry, int direction)
    {
        using var tx = db.BeginTransaction();
        using var row = Command("SELECT playlist,position FROM entries WHERE id=$id", ("$id", entry));
        string list;
        long pos;
        using (var r = row.ExecuteReader())
        {
            if (!r.Read())
                return;
            list = r.GetString(0);
            pos = r.GetInt64(1);
        }
        using var target = Command("SELECT id,position FROM entries WHERE playlist=$list AND position " + (direction < 0 ? "<" : ">") + " $pos ORDER BY position " + (direction < 0 ? "DESC" : "ASC") + " LIMIT 1", ("$list", list), ("$pos", pos));
        long other, otherPos;
        using (var r = target.ExecuteReader())
        {
            if (!r.Read())
                return;
            other = r.GetInt64(0);
            otherPos = r.GetInt64(1);
        }
        Exec("UPDATE entries SET position=$pos WHERE id=$id", ("$pos", otherPos), ("$id", entry));
        Exec("UPDATE entries SET position=$pos WHERE id=$id", ("$pos", pos), ("$id", other));
        tx.Commit();
    }
    public List<Track> LoadQueue()
    {
        using var c = Command("SELECT json FROM queue ORDER BY position LIMIT 1000");
        using var r = c.ExecuteReader();
        var list = new List<Track>();
        while (r.Read())
            list.Add(Reference(r.GetString(0)));
        return list;
    }
    public void SaveQueue(IEnumerable<Track> tracks)
    {
        using var tx = db.BeginTransaction();
        Exec("DELETE FROM queue");
        using var insert = Command("INSERT INTO queue VALUES($p,$j)", ("$p", 0), ("$j", ""));
        insert.Prepare();
        int i = 0;
        foreach (var t in tracks.Take(1000))
        {
            insert.Parameters["$p"].Value = i++;
            insert.Parameters["$j"].Value = JsonSerializer.Serialize(new
            {
                t.Id
            });
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }
    static Track Reference(string json)
    {
        using var doc = JsonDocument.Parse(json);
        string id = doc.RootElement.GetProperty("Id").GetString()!;
        return new Track(id, "Track " + id, "Loading track details…", 0, null, "", "unknown");
    }
    public void SaveSnapshot()
    {
        using var count = Command("SELECT COUNT(*) FROM playlists");
        if (Convert.ToInt32(count.ExecuteScalar()) == 0) return;
        using var saved = new SqliteConnection("Data Source=" + snapshotPath);
        saved.Open();
        db.BackupDatabase(saved);
    }
    public void Dispose()
    {
        try { SaveSnapshot(); }
        finally { db.Dispose(); }
    }
}
