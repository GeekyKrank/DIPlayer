using DIPlayer.Core;
using DIPlayer.Storage;
using DIPlayer.Queue;

static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
var track=new Track("1","Test","Author",100000,null,"https://soundcloud.com/test/track","playable");
Check(SoundCloudCatalog.ExplicitRestriction("geo_blocked")!=SoundCloudCatalog.ExplicitRestriction("subscription_required"),"explicit region and paid access reasons differ");
Check(SoundCloudCatalog.ExplicitRestriction("blocked")==null,"generic block does not invent a restriction reason");
var queue=new PlaybackQueue();queue.Add(track);queue.Add(track with{Id="2"});queue.Index=0;queue.Move(0,1);Check(queue.Index==1&&queue.Current?.Id=="1","moving queue preserves current");queue.Remove(0);Check(queue.Index==0&&queue.Current?.Id=="1","removing earlier entry preserves current");
Check(SoundCloudCatalog.PublicPlaylistUrl("https://soundcloud.com/test/sets/list?utm_source=abc")=="https://soundcloud.com/test/sets/list","public playlist URL normalization");
foreach(string bad in new[]{"http://soundcloud.com/a/sets/b","https://evil.test/a/sets/b","https://soundcloud.com/a/sets/b?secret_token=xyz","https://soundcloud.com/a/track"})
{bool rejected=false;try{SoundCloudCatalog.PublicPlaylistUrl(bad);}catch(CatalogException){rejected=true;}Check(rejected,"reject invalid/private playlist URL");}
using var store=new LibraryStore();string id=store.Create("__DIPlayer integration test__");
try
{
    store.Add(id,Enumerable.Range(0,1001).Select(i=>track with{Id=i.ToString(),Title="Track "+i}));
    Check(store.Page(id,0).Count==25&&store.Page(id,40).Count==1,"1001-track SQLite library is paginated");
    Check(store.Page(id,0)[0].Track.Title!="Test","stored references do not retain old API metadata");
    var rows=store.Page(id,0);store.Move(rows[0].EntryId,1);Check(store.Page(id,0)[1].Track.Id=="0","move track persists");
    store.Remove(rows[1].EntryId);Check(store.Playlists().Single(p=>p.Id==id).Count==1000,"remove track updates count");
    store.Rename(id,"__renamed__");Check(store.Playlists().Single(p=>p.Id==id).Name=="__renamed__","rename persists");
}
finally{store.Delete(id);}
Check(!store.Playlists().Any(p=>p.Id==id),"delete removes test playlist");
Console.WriteLine("All integration checks passed");
if(args.Contains("--network"))
{
    var settings=Settings.Load();var catalog=settings.CreateCatalog();
    try
    {
        var first=await catalog.Search("ambient",null,default);Check(first.Tracks.Count>0,"live search");
        if(first.Next!=null){var second=await catalog.Search("ambient",first.Next,default);Check(second.Tracks.Count>0&&second.Tracks[0].Id!=first.Tracks[0].Id,"live search pagination");}
        int pi=Array.IndexOf(args,"--playlist");string playlistUrl=pi>=0?args[pi+1]:"https://soundcloud.com/laugebabagnohm/sets/ambient-soundscapes-selections";
        var info=await catalog.ResolvePlaylist(playlistUrl,default);Console.WriteLine($"Public playlist resolved: {info.Title}; {info.Count}");
        var page=await catalog.PlaylistTracks(info.Id,null,default);Check(page.Tracks.Count>0,"live public playlist tracks");
        Console.WriteLine($"Playlist page: {page.Tracks.Count}; next={page.Next!=null}");
        if(page.Next!=null){var second=await catalog.PlaylistTracks(info.Id,page.Next,default);Check(second.Tracks.Count>0,"live playlist pagination");}
        var importer=new DIPlayer.Import.PlaylistImporter(catalog,store);string? imported=null;
        try{if(!args.Contains("--skip-import")){imported=await importer.Import(playlistUrl,new Progress<string>(Console.WriteLine),default);Check(store.Playlists().Single(p=>p.Id==imported).Count>0,"live public playlist imported into SQLite");}}
        finally{if(imported!=null)store.Delete(imported);}
    }
    finally{(catalog as IDisposable)?.Dispose();}
}
