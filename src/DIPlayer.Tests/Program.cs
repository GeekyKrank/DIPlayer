using System.Text.Json;
using System.Net.Http;
using DIPlayer.Core;
using System.IO;
using DIPlayer.Storage;
using DIPlayer.Queue;

if (args.Length == 2 && args[0] == "--export-volume-scripts")
{
    File.WriteAllText(args[1], JsonSerializer.Serialize(new[] { DIPlayer.YouTubeVolumeBridge.Install(.23), DIPlayer.YouTubeVolumeBridge.Install(0), DIPlayer.YouTubeVolumeBridge.Install(.61) }));
    return;
}

static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
ArtworkChecks.Run(Check);
SearchSuggestionsChecks.Run(Check);
var track=new Track("1","Test","Author",100000,null,"https://soundcloud.com/test/track","playable");
Check(SoundCloudCatalog.ExplicitRestriction("geo_blocked")!=SoundCloudCatalog.ExplicitRestriction("subscription_required"),"explicit region and paid access reasons differ");
Check(SoundCloudCatalog.ExplicitRestriction("blocked")==null,"generic block does not invent a restriction reason");
var queue=new PlaybackQueue();queue.Add(track);queue.Add(track with{Id="2"});queue.Index=0;queue.Move(0,1);Check(queue.Index==1&&queue.Current?.Id=="1","moving queue preserves current");queue.Remove(0);Check(queue.Index==0&&queue.Current?.Id=="1","removing earlier entry preserves current");
Check(SoundCloudCatalog.PublicPlaylistUrl("https://soundcloud.com/test/sets/list?utm_source=abc")=="https://soundcloud.com/test/sets/list","public playlist URL normalization");
foreach(string bad in new[]{"http://soundcloud.com/a/sets/b","https://evil.test/a/sets/b","https://soundcloud.com/a/sets/b?secret_token=xyz","https://soundcloud.com/a/track"})
{bool rejected=false;try{SoundCloudCatalog.PublicPlaylistUrl(bad);}catch(CatalogException){rejected=true;}Check(rejected,"reject invalid/private playlist URL");}
using var store=new LibraryStore(Path.Combine(Path.GetTempPath(),"DIPlayer-check-"+Guid.NewGuid().ToString("N")));string id=store.Create("__DIPlayer integration test__");
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
string accountSource="soundcloud-account:test:"+Guid.NewGuid().ToString("N");
string original=store.Create("Account list"), updated=store.Create("Remote rename"), partial=store.Create("Partial",complete:false);
try
{
    store.Add(original,[track,track with { Id="missing", Issue="Track details unavailable" }]);
    Check(store.Playlists().Single(p=>p.Id==original).Count==1,"missing API placeholders are excluded from stored playlist counts");
    Check(store.PublishAccountImport(original,accountSource,true)==original,"first account import publishes a local playlist");
    store.Edit(original,"My custom name",true,[1,2,3]);
    store.SetArtwork(original,"https://i1.sndcdn.com/playlist-cover.jpg");
    Check(store.Playlists().Single(p=>p.Id==original).Artwork=="https://i1.sndcdn.com/playlist-cover.jpg","original playlist artwork persists separately from custom cover");
    store.Add(updated,[track,track with{Id="2"}]);
    Check(store.PublishAccountImport(updated,accountSource,true)==original,"repeat account import reuses the existing playlist");
    Check(store.Playlists().Single(p=>p.Id==original).Count==2&&store.Playlists().Single(p=>p.Id==original).Name=="My custom name"&&store.Cover(original)!.SequenceEqual(new byte[]{1,2,3}),"account refresh preserves the custom name and cover");
    store.Add(partial,[track]);
    Check(store.PublishAccountImport(partial,accountSource,false)==partial&&store.Playlists().Single(p=>p.Id==original).Count==2&&!store.Playlists().Single(p=>p.Id==partial).Complete,"incomplete refresh cannot overwrite a completed local copy");
}
finally{store.Delete(original);store.Delete(updated);store.Delete(partial);}
string oldList=store.Create("Z old"), likes=store.Create("Favorites", "soundcloud-account:test:likes"), newList=store.Create("A new");
try
{
var ordered=store.Playlists().Where(p=>new[]{oldList,likes,newList}.Contains(p.Id)).Select(p=>p.Id).ToArray();
Check(ordered.SequenceEqual(new[]{likes,newList,oldList}),"Likes first, then newest playlists first");
store.Rename(oldList,"AAA renamed");
Check(store.Playlists().Where(p=>new[]{oldList,likes,newList}.Contains(p.Id)).Select(p=>p.Id).SequenceEqual(ordered),"renaming preserves chronology");
}
finally{store.Delete(oldList);store.Delete(likes);store.Delete(newList);}
string restartFolder=Path.Combine(Path.GetTempPath(),"DIPlayer-restart-"+Guid.NewGuid().ToString("N"));
string restartId;
using(var first=new LibraryStore(restartFolder))
{
 restartId=first.Create("Persistent playlist","soundcloud-account:restart:playlist:1");
 first.Add(restartId,[track,track with {Id="2"}]);
 first.SetArtwork(restartId,"https://i1.sndcdn.com/cover.jpg");
}
using(var second=new LibraryStore(restartFolder))
{
 Check(second.Playlists().Single().Id==restartId && second.Playlists().Single().Count==2 && second.Playlists().Single().Artwork!=null,"playlist, tracks and cover survive closing and reopening storage");
 second.Hide(second.Page(restartId,0)[0].EntryId);
 Check(second.Playlists().Single().Count==1 && second.Page(restartId,0).Count==1,"missing tracks are hidden without deleting playlist");
}
using(var third=new LibraryStore(restartFolder))
 Check(third.Playlists().Single().Id==restartId && third.Playlists().Single().Count==1,"hidden state and playlist survive second restart");
Check(File.Exists(Path.Combine(restartFolder,"library.saved.db")),"nonempty library backup is saved");
var modes=new PlaybackQueue();
modes.Tracks.AddRange(Enumerable.Range(0,6).Select(i=>track with {Id="shuffle-"+i}));
modes.Index=0;
var visited=new HashSet<string>{"shuffle-0"};
for(int i=0;i<5;i++) Check(visited.Add(modes.Navigate(1,false,true)!.Id),"shuffle visits a new track");
Check(modes.Navigate(1,false,true)==null,"shuffle stops after a full cycle with repeat off");
int last=modes.Index;
Check(modes.Navigate(-1,false,true)!=null && modes.Navigate(1,false,true)!=null && modes.Index==last,"shuffle previous follows playback history");
Check(modes.Navigate(1,true,true)!=null && modes.Index!=last,"repeat playlist starts another shuffled cycle");
modes.Index=5;Check(modes.Navigate(1,true,false)?.Id=="shuffle-0","repeat playlist wraps at end");
modes.Index=0;Check(modes.Navigate(-1,true,false)?.Id=="shuffle-5","repeat playlist wraps backwards");
string dateFolder=Path.Combine(Path.GetTempPath(),"DIPlayer-date-"+Guid.NewGuid().ToString("N"));
string newer,older,favorites;
using(var dates=new LibraryStore(dateFolder))
{
 newer=dates.Create("Newer on SoundCloud"); older=dates.Create("Older on SoundCloud"); favorites=dates.Create("Likes","soundcloud-account:date:likes");
 dates.SetCreatedAt(newer,"2025-01-01T12:00:00Z"); dates.SetCreatedAt(older,"2018/06/01 12:00:00 +0000");
 dates.SetCreatedAt(newer,"invalid");
 Check(dates.Playlists().Select(p=>p.Id).SequenceEqual(new[]{favorites,newer,older}),"newest sort uses SoundCloud creation date, not import order");
 Check(dates.Playlists(true).Select(p=>p.Id).SequenceEqual(new[]{favorites,older,newer}),"oldest sort preserves Likes at the top");
}
using(var dates=new LibraryStore(dateFolder))
 Check(dates.Playlists(true).Select(p=>p.Id).SequenceEqual(new[]{favorites,older,newer}),"creation-date sorting survives restart");
foreach(bool shuffledMode in new[]{false,true})
{
 var skipping=new PlaybackQueue();
 skipping.Tracks.AddRange(Enumerable.Range(0,5).Select(i=>track with{Id="skip-"+i,Access=i==3?"playable":"blocked"}));
 skipping.Index=0;
 var attempted=new List<string>();
 bool ok=await skipping.PlayAvailable(skipping.Current!,1,true,shuffledMode,t=>{attempted.Add(t.Id);return Task.FromResult(t.Access=="playable");},default);
 Check(ok && skipping.Current?.Id=="skip-3" && attempted.Distinct().Count()==attempted.Count,"blocked tracks are skipped in "+(shuffledMode?"shuffle":"ordered")+" playback");
 foreach(var t in skipping.Tracks.ToArray()) skipping.Tracks[skipping.Tracks.IndexOf(t)]=t with{Access="blocked"};
 int attempts=0;
 Check(!await skipping.PlayAvailable(skipping.Current!,1,true,shuffledMode,t=>{attempts++;return Task.FromResult(false);},default) && attempts<=5,"all-blocked repeat queue stops after bounded attempts");
 bool networkFailed=false;
 try{await skipping.PlayAvailable(skipping.Current!,1,true,shuffledMode,t=>throw new HttpRequestException("offline"),default);}catch(HttpRequestException){networkFailed=true;}
 Check(networkFailed,"network failures are not silently treated as blocked tracks");
}
Check(ReleaseUpdates.Compare("v0.3.1-alpha","0.3.0-alpha")>0 && ReleaseUpdates.Compare("v0.3.1","0.3.1-alpha")>0 && ReleaseUpdates.Compare("0.3.1-alpha.10","0.3.1-alpha.2")>0,"update comparison handles patch, stable and numeric prereleases");
using(var releases=JsonDocument.Parse("""[{"draft":false,"tag_name":"v0.3.1-alpha","html_url":"https://evil.test"},{"draft":true,"tag_name":"v9.0.0"},{"draft":false,"tag_name":"not-a-version"},{"draft":false,"tag_name":"v0.3.0-alpha"}]"""))
{
 var update=ReleaseUpdates.Find(releases.RootElement,"0.3.0-alpha");
 Check(update?.Tag=="v0.3.1-alpha" && update.Url=="https://github.com/GeekyKrank/DIPlayer/releases/tag/v0.3.1-alpha","published alpha update uses trusted GitHub link and ignores drafts/invalid tags");
 Check(ReleaseUpdates.Find(releases.RootElement,"0.3.1-alpha+build123")==null,"same installed version never shows an update");
 Check(ReleaseUpdates.Find(releases.RootElement,"0.4.0-alpha")==null,"older releases never prompt a downgrade");
}
Check(BrowserAdFilter.Block("https://googleads.g.doubleclick.net/pagead/ad") && BrowserAdFilter.Block("https://music.youtube.com/api/stats/ads?x=1"),"browser ad filter blocks ad hosts and YouTube ad requests");
Check(!BrowserAdFilter.Block("https://music.youtube.com/search?q=music") && !BrowserAdFilter.Block("https://rr1.googlevideo.com/videoplayback") && !BrowserAdFilter.Block("https://accounts.google.com/") && !BrowserAdFilter.Block("https://doubleclick.net.evil.test/"),"browser filter preserves search, audio, sign-in and exact hostname boundaries");
await EqualizerChecks.Run();
await DiscordChecks.Run();
await LyricsChecks.Run();
if(args.Contains("--check-live-lyrics"))
{
    var synced=await new DIPlayer.SyncedLyricsProvider().Get("20 Min","Lil Uzi Vert",221,default);
    Check(synced.Length>10,"live LRCLIB matched 20 Min and returned real timed lines");
}
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
