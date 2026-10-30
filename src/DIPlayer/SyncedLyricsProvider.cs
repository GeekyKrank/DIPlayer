using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace DIPlayer;

public sealed record SyncedLyricsCandidate(string Id,string Title,string Artist,string Album,double Duration,TimedLyric[] Lines,string Provider="LRCLIB")
{
    public string DisplayName => Provider+" · "+(Album.Length>0?Album:Title);
    public override string ToString()=>DisplayName;
}

public sealed class SyncedLyricsProvider
{
    static readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(8)};
    readonly HttpClient client;
    public SyncedLyricsProvider(HttpClient? client=null)=>this.client=client??http;
    sealed record CachedLyrics(SyncedLyricsCandidate[] Candidates,bool Expanded,DateTime Expires);
    readonly Dictionary<string,CachedLyrics> cache=[];
    readonly SemaphoreSlim requests=new(1,1);
    DateTime retryAfter,lastRequest;

    // Fast path: one exact lookup when possible. Search is a fallback, never a
    // reason to replace a known version with another recording of the song.
    public async Task<TimedLyric[]> Get(string title,string artist,double duration,CancellationToken token)
        =>(await Load(title,artist,duration,false,token)).FirstOrDefault()?.Lines??[];

    // Explicit alternatives lookup: all entries have their own real LRC timing.
    // Native YouTube timing can be added by the view as a separate source.
    public Task<SyncedLyricsCandidate[]> GetCandidates(string title,string artist,double duration,CancellationToken token)
        =>Load(title,artist,duration,true,token);

    async Task<SyncedLyricsCandidate[]> Load(string title,string artist,double duration,bool alternatives,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(artist)||title.Length>1000||artist.Length>1000||!double.IsFinite(duration)||duration<1||duration>3600)return [];
        string key=title+"|"+artist+"|"+Math.Round(duration);
        await requests.WaitAsync(token);
        List<SyncedLyricsCandidate> found=[];
        try
        {
            cache.TryGetValue(key,out var existing);
            if(existing?.Expires<=DateTime.UtcNow)existing=null;
            if(existing!=null&&(!alternatives||existing.Expanded))return existing.Candidates;
            if(DateTime.UtcNow<retryAfter)return existing?.Candidates??[];

            found=new List<SyncedLyricsCandidate>(existing?.Candidates??[]);
            bool completed=true;
            if(existing==null)
            {
                string url="https://lrclib.net/api/get?track_name="+Uri.EscapeDataString(title)+"&artist_name="+Uri.EscapeDataString(artist)+"&duration="+Math.Round(duration).ToString(CultureInfo.InvariantCulture);
                var exact=await Fetch(url,token);
                if(!exact.Completed)return [];
                if(exact.Json!=null)
                {
                    using var document=exact.Json;
                    if(ReadCandidate(document.RootElement,title,artist,duration) is { } candidate)found.Add(candidate);
                }
                if(found.Count>0&&!alternatives)return Remember(key,found,false);
            }

            string queryTitle=CleanTitle(title),queryArtist=CleanArtist(artist);
            if(queryTitle.Length==0||queryArtist.Length==0)return found.ToArray();
            var search=await Fetch("https://lrclib.net/api/search?track_name="+Uri.EscapeDataString(queryTitle)+"&artist_name="+Uri.EscapeDataString(queryArtist),token);
            completed=search.Completed;
            if(search.Json!=null)
            {
                using var document=search.Json;
                AddCandidates(document.RootElement,found,title,artist,duration);
            }
            // Structured artist fields are often uploader credits ("A, B"). A
            // single broad query using the primary artist recovers these cases.
            // Both title and version/duration are still checked locally.
            if(completed&&found.Count==0)
            {
                string primary=ArtistParts(queryArtist).FirstOrDefault()??queryArtist;
                var broad=await Fetch("https://lrclib.net/api/search?q="+Uri.EscapeDataString(queryTitle+" "+primary),token);
                completed=broad.Completed;
                if(broad.Json!=null)
                {
                    using var document=broad.Json;
                    AddCandidates(document.RootElement,found,title,artist,duration);
                }
            }
            if(!completed)
            {
                // A transient search failure must remain retryable. Keep any
                // exact hit, without claiming alternatives were fully checked.
                return found.Count>0?Remember(key,found,false):[];
            }
            return Remember(key,found,true);
        }
        catch(Exception e) when(e is HttpRequestException or JsonException or IOException or OperationCanceledException)
        {if(token.IsCancellationRequested)throw;if(found.Count>0)return Remember(key,found,false);}
        finally{requests.Release();}
        return [];
    }

    SyncedLyricsCandidate[] Remember(string key,List<SyncedLyricsCandidate> found,bool expanded)
    {
        var candidates=found.DistinctBy(candidate=>string.Join('\n',candidate.Lines.Select(line=>line.Start.ToString("R",CultureInfo.InvariantCulture)+"|"+line.Text))).Take(8).ToArray();
        if(cache.Count>=32&&!cache.ContainsKey(key))cache.Remove(cache.Keys.First());
        cache[key]=new(candidates,expanded,DateTime.UtcNow+(candidates.Length>0?TimeSpan.FromHours(6):TimeSpan.FromMinutes(15)));
        return candidates;
    }

    void AddCandidates(JsonElement root,List<SyncedLyricsCandidate> found,string title,string artist,double duration)
    {
        if(root.ValueKind!=JsonValueKind.Array)return;
        var matches=root.EnumerateArray().Take(30).Select(record=>ReadCandidate(record,title,artist,duration)).OfType<SyncedLyricsCandidate>()
            .OrderBy(candidate=>Math.Abs(candidate.Duration-duration)).ThenBy(candidate=>candidate.Id,StringComparer.Ordinal);
        found.AddRange(matches);
    }

    static SyncedLyricsCandidate? ReadCandidate(JsonElement root,string title,string artist,double duration)
    {
        if(root.ValueKind!=JsonValueKind.Object)return null;
        string Read(string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
        string candidateTitle=Read("trackName"),candidateArtist=Read("artistName");
        string wantedTitle=Normalize(CleanTitle(title));
        if(wantedTitle.Length==0||Normalize(CleanTitle(candidateTitle))!=wantedTitle||!ArtistsMatch(candidateArtist,artist))return null;
        if(!root.TryGetProperty("duration",out var length)||length.ValueKind!=JsonValueKind.Number||!length.TryGetDouble(out double seconds)||!double.IsFinite(seconds)||Math.Abs(seconds-duration)>2.1)return null;
        // No time stretching, guessed cues, or automatic use of the original
        // recording's lyrics for a slowed/live/remixed version.
        var lines=LyricsTimeline.Parse(Read("syncedLyrics")).Where(line=>line.Start<=duration+2.1&&line.Text.Length<=2000).Take(500).ToArray();
        if(lines.Length==0||!lines.Any(line=>line.Text.Length>0))return null;
        string id=root.TryGetProperty("id",out var recordId)&&recordId.ValueKind==JsonValueKind.Number?recordId.GetRawText():Normalize(candidateTitle)+"|"+Normalize(candidateArtist)+"|"+seconds.ToString("R",CultureInfo.InvariantCulture);
        return new("lrclib:"+id,candidateTitle,candidateArtist,Read("albumName"),seconds,lines);
    }

    static string Normalize(string value)=>string.Concat(value.Normalize(NormalizationForm.FormKC).EnumerateRunes().Where(Rune.IsLetterOrDigit)).ToUpperInvariant();
    static string CleanArtist(string value)=>Regex.Replace(value.Trim(),@"\s*[-–—]\s*Topic$","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant).Trim();
    static string CleanTitle(string value)
    {
        // Deliberately keep recording qualifiers such as slowed, sped up,
        // remix, live, acoustic and remaster: similar names are not same timing.
        value=Regex.Replace(value,@"[\(\[]([^\)\]]+)[\)\]]",match=>
        {
            string label=match.Groups[1].Value.Trim();
            if(Regex.IsMatch(label,@"^(?:official\s+)?(?:audio|music\s+video|video|lyric\s+video|lyrics?)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))return "";
            if(Regex.IsMatch(label,@"^(?:ft\.?|feat\.?|featuring)\s+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)&&!Regex.IsMatch(label,@"\b(?:slowed|sped|remix|live|acoustic|remaster|reverb|edit|extended|instrumental|cover|karaoke|demo)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))return "";
            return match.Value;
        });
        value=Regex.Replace(value,@"\s+[-–—]\s+(?:official\s+)?(?:audio|music\s+video|video|lyrics?)\s*$","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        value=Regex.Replace(value,@"\s+(?:feat\.?|ft\.?|featuring)\s+[^\(\[]+","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return Regex.Replace(value,@"\s+"," ").Trim();
    }
    static string[] ArtistParts(string value)=>Regex.Split(CleanArtist(value),@"\s*(?:,|;|&|、|\bfeat\.?\s|\bft\.?\s|\bfeaturing\s)\s*|\s+[x×/]\s+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant).Select(part=>part.Trim()).Where(part=>part.Length>0).ToArray();
    static bool ArtistsMatch(string candidate,string requested)
    {
        if(Normalize(CleanArtist(candidate))==Normalize(CleanArtist(requested)))return true;
        var candidateParts=ArtistParts(candidate).Select(Normalize).ToArray();
        var requestedParts=ArtistParts(requested).Select(Normalize).ToArray();
        if(candidateParts.Length==0||requestedParts.Length==0)return false;
        return candidateParts[0]==requestedParts[0]||candidateParts.Contains(requestedParts[0])&&requestedParts.Contains(candidateParts[0]);
    }

    async Task<(bool Completed,JsonDocument? Json)> Fetch(string url,CancellationToken token)
    {
        if(DateTime.UtcNow<retryAfter)return (false,null);
        TimeSpan delay=TimeSpan.FromMilliseconds(300)-(DateTime.UtcNow-lastRequest);
        if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.UserAgent.ParseAdd("DIPlayer/0.4 (https://github.com/GeekyKrank/DIPlayer)");
        lastRequest=DateTime.UtcNow;
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if((int)response.StatusCode is 429 or 503)
        {
            DateTime deadline=response.Headers.RetryAfter?.Date?.UtcDateTime??DateTime.UtcNow+(response.Headers.RetryAfter?.Delta??TimeSpan.FromMinutes(1));
            retryAfter=deadline+TimeSpan.FromMilliseconds(Random.Shared.Next(100,500));
            return (false,null);
        }
        if(response.StatusCode==HttpStatusCode.NotFound)return (true,null);
        if(!response.IsSuccessStatusCode)return (false,null);
        if(response.Content.Headers.ContentType?.MediaType is not ("application/json" or "text/json"))return (false,null);
        using var stream=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();
        byte[] chunk=new byte[8192];int count;
        while((count=await stream.ReadAsync(chunk,token))>0){if(buffer.Length+count>512*1024)return (false,null);buffer.Write(chunk,0,count);}
        return (true,JsonDocument.Parse(buffer.ToArray()));
    }
}
