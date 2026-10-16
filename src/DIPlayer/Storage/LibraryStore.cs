using DIPlayer.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace DIPlayer.Storage;

public sealed record PlaylistHeader(string Id, string Name, int Count, bool Complete, bool HasCover = false)
{
    public override string ToString() => Name;
}
public sealed record LibraryRow(long EntryId, Track Track);
public sealed class LibraryStore : IDisposable
{
    readonly SqliteConnection db;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIPlayer");
    public LibraryStore(string? directory = null)
    {
        string folder = directory ?? DataDirectory;
        Directory.CreateDirectory(folder);
        db = new SqliteConnection("Data Source=" + Path.Combine(folder, "library.db"));
        db.Open();
        Exec("CREATE TABLE IF NOT EXISTS playlist_covers(playlist TEXT PRIMARY KEY,image BLOB NOT NULL)");
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; CREATE TABLE IF NOT EXISTS playlists(id TEXT PRIMARY KEY,name TEXT NOT NULL,source TEXT,complete INTEGER NOT NULL DEFAULT 1); CREATE TABLE IF NOT EXISTS entries(id INTEGER PRIMARY KEY,playlist TEXT NOT NULL,position INTEGER NOT NULL,json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_entries ON entries(playlist,position); CREATE TABLE IF NOT EXISTS queue(position INTEGER PRIMARY KEY,json TEXT NOT NULL);");
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
    public List<PlaylistHeader> Playlists()
    {
        using var c = Command("SELECT p.id,p.name,COUNT(e.id),p.complete,EXISTS(SELECT 1 FROM playlist_covers c WHERE c.playlist=p.id) FROM playlists p LEFT JOIN entries e ON e.playlist=p.id GROUP BY p.id ORDER BY p.name");
        using var r = c.ExecuteReader();
        var list = new List<PlaylistHeader>();
        while (r.Read())
            list.Add(new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetBoolean(3), r.GetBoolean(4)));
        return list;
    }
    public string Create(string name, string? source = null, bool complete = true)
    {
        string id = Guid.NewGuid().ToString("N");
        Exec("INSERT INTO playlists VALUES($id,$name,$source,$complete)", ("$id", id), ("$name", name), ("$source", source), ("$complete", complete ? 1 : 0));
        return id;
    }
    public void Rename(string id, string name) => Exec("UPDATE playlists SET name=$name WHERE id=$id", ("$name", name), ("$id", id));
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
    public void Delete(string id)
    {
        using var tx = db.BeginTransaction();
        Exec("DELETE FROM entries WHERE playlist=$id", ("$id", id));
        Exec("DELETE FROM playlist_covers WHERE playlist=$id", ("$id", id));
        Exec("DELETE FROM playlists WHERE id=$id", ("$id", id));
        tx.Commit();
    }
    public List<LibraryRow> Page(string id, int page)
    {
        using var c = Command("SELECT id,json FROM entries WHERE playlist=$id ORDER BY position,id LIMIT 25 OFFSET $offset", ("$id", id), ("$offset", page * 25));
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
            Exec("INSERT INTO entries(playlist,position,json) VALUES($id,$pos,$json)", ("$id", id), ("$pos", pos++), ("$json", JsonSerializer.Serialize(new
            {
                track.Id
            })));
        tx.Commit();
    }
    public void Remove(long entry) => Exec("DELETE FROM entries WHERE id=$id", ("$id", entry));
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
    public void Dispose() => db.Dispose();
}
