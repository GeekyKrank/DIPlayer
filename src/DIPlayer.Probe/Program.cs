using DIPlayer.Core;
using DIPlayer.Audio;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text.Json;

string credentials=args.Length>0?args[0]:"clientAPI.txt";
string raw=await File.ReadAllTextAsync(credentials);
string id=Regex.Match(raw,@"(?im)^\s*client\s*id\s*[:=]\s*(\S+)").Groups[1].Value;
string secret=Regex.Match(raw,@"(?im)^\s*client\s*secret\s*[:=]\s*(\S+)").Groups[1].Value;
if(id.Length==0||secret.Length==0)throw new Exception("Credential fields missing");
using var api=new SoundCloudCatalog(id,secret);
var results=await api.Search(args.Length>1?args[1]:"ambient",null,default);
var track=results.Tracks.First(t=>t.Access=="playable");
Console.WriteLine($"Search OK: {track.Title}; {track.Duration}");
string stream=await api.Stream(track.Id,default);
Console.WriteLine("Stream URL resolved; URL withheld");
using var player=new HlsPlayer();
bool failed=false;player.Error+=message=>{failed=true;Console.WriteLine(message);};
await player.Open(stream,default);
if(args.Contains("--stress"))
{
    for(int i=0;i<12;i++)
    {
        if(i%3==0)await player.Open(stream,default);
        else await player.Seek(i*3);
        await Task.Delay(120);
    }
    Console.WriteLine("12 rapid source/seek changes completed");
}
var proc=Process.GetCurrentProcess();var watch=Stopwatch.StartNew();var cpu=proc.TotalProcessorTime;
var samples=new List<object>();int seconds=args.Length>2?int.Parse(args[2]):40;
for(int i=0;i<seconds;i++)
{
    await Task.Delay(1000);proc.Refresh();
    if(i==15)await player.Seek(Math.Min(60,player.Duration/2));
    samples.Add(new {second=i+1,workingSetMiB=proc.WorkingSet64/1048576d,privateMiB=proc.PrivateMemorySize64/1048576d,position=player.Position,buffer=player.BufferedSeconds});
    if(i%10==0)Console.WriteLine($"{i+1}s; position={player.Position:F1}; buffered={player.BufferedSeconds:F1}; RAM={proc.WorkingSet64/1048576d:F1} MiB");
}
double cpuSeconds=(proc.TotalProcessorTime-cpu).TotalSeconds;
double duration=player.Duration;
await player.Stop();
var report=new {utc=DateTimeOffset.UtcNow,track=track.Title,trackId=track.Id,duration,seconds=watch.Elapsed.TotalSeconds,cpuSeconds,cpuOneCorePercent=cpuSeconds/watch.Elapsed.TotalSeconds*100,cpuMachinePercent=cpuSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,logicalProcessors=Environment.ProcessorCount,failed,samples};
Directory.CreateDirectory("evidence");await File.WriteAllTextAsync("evidence/playback-probe.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(failed?"FAILED":"Probe complete; decoded playback and seek positions recorded. Audibility requires listening.");
