using DIPlayer;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
static class LyricsChecks
{
    public static async Task Run()
    {
        var lines=LyricsTimeline.Parse("[ar:Fixture]\n[00:01.25]first\n[00:05.50][00:10.50]repeat\n[00:12.00]\n[offset:250]");
        if(lines.Length!=4||lines[0].Start!=1||LyricsTimeline.ActiveIndex(lines,.9)!=-1||LyricsTimeline.ActiveIndex(lines,5.4)!=1||LyricsTimeline.ActiveIndex(lines,10.4)!=2||LyricsTimeline.ActiveIndex(lines,1.2)!=0||lines[3].Text!="")throw new Exception("Lyric timestamps / seeks / blank cues");
        var handler=new LyricsHandler();using var http=new HttpClient(handler);var provider=new SyncedLyricsProvider(http);
        var good=await provider.Get("Track","Artist",100,default);
        if(good.Length!=2||handler.Count!=1)throw new Exception("Synced lyrics match");
        await provider.Get("Track","Artist",100,default);
        if(handler.Count!=1)throw new Exception("Lyric cache");
        if((await provider.Get("Track","Another artist",100,default)).Length!=0)throw new Exception("Wrong artist lyrics accepted");
        if((await provider.Get("Track","Artist",120,default)).Length!=0)throw new Exception("Wrong version duration accepted");
        int before=handler.Count;
        if((await provider.Get("Track","Artist",double.NaN,default)).Length!=0||handler.Count!=before)throw new Exception("Invalid lyric duration reached the service");
        var retryHandler=new ScriptedLyricsHandler(count=>count==1?new HttpResponseMessage(HttpStatusCode.InternalServerError):LyricsResponse());
        using var retryHttp=new HttpClient(retryHandler);var retryProvider=new SyncedLyricsProvider(retryHttp);
        await retryProvider.Get("Track","Artist",100,default);
        if((await retryProvider.Get("Track","Artist",100,default)).Length!=2||retryHandler.Count!=2)throw new Exception("Temporary lyric failure was cached as missing");
        var limitedHandler=new ScriptedLyricsHandler(_=>{var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));return response;});
        using var limitedHttp=new HttpClient(limitedHandler);var limitedProvider=new SyncedLyricsProvider(limitedHttp);
        await limitedProvider.Get("Track","Artist",100,default);await limitedProvider.Get("Other track","Artist",100,default);
        if(limitedHandler.Count!=1)throw new Exception("Lyric service Retry-After was ignored");
        var concurrentHandler=new ScriptedLyricsHandler(_=>LyricsResponse());using var concurrentHttp=new HttpClient(concurrentHandler);var concurrentProvider=new SyncedLyricsProvider(concurrentHttp);
        var requests=await Task.WhenAll(concurrentProvider.Get("Track","Artist",100,default),concurrentProvider.Get("Track","Artist",100,default));
        if(concurrentHandler.Count!=1||requests.Any(result=>result.Length!=2))throw new Exception("Concurrent lyric requests were duplicated");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        try{await concurrentProvider.Get("Another track","Artist",100,canceled.Token);throw new Exception("Canceled lyric request was not canceled");}catch(OperationCanceledException){}
        await CheckCandidates();
        Console.WriteLine("PASS synced lyrics: timestamps, offsets, seeks, cache, version/artist/duration matching, real alternatives, cleaned metadata, transient recovery, limits, coalescing and cancellation");
    }
    static async Task CheckCandidates()
    {
        object Record(int id,string title="Track",string artist="Artist",double duration=100,string album="Album",string lrc="[00:01.00]first\n[00:05.00]next")
            =>new{id,trackName=title,artistName=artist,duration,albumName=album,syncedLyrics=lrc};
        HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
        var paths=new List<string>();
        var handler=new CandidateHandler(request=>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri.AbsolutePath=="/api/get"?Json(Record(1)):Json(new[]{Record(2,album:"Single",lrc:"[00:01.25]first\n[00:05.25]next"),Record(3,album:"Duplicate"),Record(4,title:"Track (Slowed)"),Record(5,artist:"Other"),Record(6,duration:110)});
        });
        using var http=new HttpClient(handler);var provider=new SyncedLyricsProvider(http);
        var first=await provider.Get("Track","Artist",100,default);
        var alternatives=await provider.GetCandidates("Track","Artist",100,default);
        if(first.Length!=2||alternatives.Length!=2||paths.Count!=2||paths[1].StartsWith("/api/search?")==false||alternatives[0].Id!="lrclib:1"||alternatives[1].Album!="Single")throw new Exception("Real alternate timelines were lost, duplicated or incorrectly matched");
        await provider.GetCandidates("Track","Artist",100,default);
        if(paths.Count!=2)throw new Exception("Expanded lyric alternatives were requested again");

        var cleanHandler=new CandidateHandler(request=>request.RequestUri!.AbsolutePath=="/api/get"?new(HttpStatusCode.NotFound):Json(new[]{Record(7,title:"Track",artist:"Artist")}));
        using var cleanHttp=new HttpClient(cleanHandler);var cleanProvider=new SyncedLyricsProvider(cleanHttp);
        if((await cleanProvider.GetCandidates("Track (Official Audio)","Artist - Topic",100,default)).Length!=1)throw new Exception("Harmless YouTube labels blocked lyrics fallback");
        if(cleanHandler.Count!=2||!cleanHandler.Requests.Last().Contains("track_name=Track&artist_name=Artist",StringComparison.Ordinal))throw new Exception("Clean lyric metadata was not used in search");

        var versionHandler=new CandidateHandler(request=>request.RequestUri!.AbsolutePath=="/api/get"?new(HttpStatusCode.NotFound):Json(new[]{Record(8,title:"Track",artist:"Artist"),Record(9,title:"Track (Slowed)",artist:"Artist"),Record(10,title:"Track (Other Remix)",artist:"Artist")}));
        using var versionHttp=new HttpClient(versionHandler);var versionProvider=new SyncedLyricsProvider(versionHttp);
        var slowed=await versionProvider.GetCandidates("Track (Slowed) [Official Audio]","Artist",100,default);
        if(slowed.Length!=1||slowed[0].Id!="lrclib:9")throw new Exception("Version-specific timing was replaced with an original or remix");
        if((await versionProvider.GetCandidates("Track feat. Friend (Special Remix)","Artist",100,default)).Length!=0)throw new Exception("Feature-credit cleanup discarded a recording qualifier");

        var collaborationHandler=new CandidateHandler(request=>request.RequestUri!.AbsolutePath=="/api/get"?new(HttpStatusCode.NotFound):request.RequestUri.Query.Contains("track_name=",StringComparison.Ordinal)?Json(Array.Empty<object>()):Json(new[]{Record(11,artist:"Artist feat. Friend"),Record(12,artist:"Unrelated feat. Friend")}));
        using var collaborationHttp=new HttpClient(collaborationHandler);var collaborationProvider=new SyncedLyricsProvider(collaborationHttp);
        var collaboration=await collaborationProvider.GetCandidates("Track (feat. Friend)","Artist, Friend",100,default);
        if(collaboration.Length!=1||collaboration[0].Id!="lrclib:11"||collaborationHandler.Count!=3)throw new Exception("Primary artist fallback or collaborator validation");

        var failingSearch=new CandidateHandler(request=>request.RequestUri!.AbsolutePath=="/api/get"?Json(Record(13)):new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var failingHttp=new HttpClient(failingSearch);var failingProvider=new SyncedLyricsProvider(failingHttp);
        var retained=await failingProvider.GetCandidates("Track","Artist",100,default);
        if(retained.Length!=1||(await failingProvider.Get("Track","Artist",100,default)).Length!=2||failingSearch.Count!=2)throw new Exception("Temporary alternatives failure discarded exact timing");

        var hugeHandler=new CandidateHandler(_=>new(HttpStatusCode.OK){Content=new StringContent(new string(' ',600*1024),Encoding.UTF8,"application/json")});
        using var hugeHttp=new HttpClient(hugeHandler);
        if((await new SyncedLyricsProvider(hugeHttp).GetCandidates("Track","Artist",100,default)).Length!=0)throw new Exception("Oversized lyrics response was accepted");
    }
    sealed class CandidateHandler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        public int Count;
        public List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Count++;Requests.Add(request.RequestUri!.PathAndQuery);
            if(request.RequestUri.Scheme!="https"||request.RequestUri.Host!="lrclib.net"||!request.Headers.UserAgent.ToString().Contains("DIPlayer",StringComparison.Ordinal))throw new Exception("Unsafe or unidentified lyric request");
            return Task.FromResult(response(request));
        }
    }
    static HttpResponseMessage LyricsResponse()=>new(HttpStatusCode.OK){Content=new StringContent("{\"trackName\":\"Track\",\"artistName\":\"Artist\",\"duration\":100,\"syncedLyrics\":\"[00:01.00]Fixture first\\n[00:05.00]Fixture next\"}",Encoding.UTF8,"application/json")};
    sealed class ScriptedLyricsHandler(Func<int,HttpResponseMessage> response):HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(!request.Headers.UserAgent.ToString().Contains("DIPlayer",StringComparison.Ordinal)||request.RequestUri?.Host!="lrclib.net")throw new Exception("Lyric service request identity");
            return Task.FromResult(response(Interlocked.Increment(ref Count)));
        }
    }
    sealed class LyricsHandler:HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"trackName\":\"Track\",\"artistName\":\"Artist\",\"duration\":100,\"syncedLyrics\":\"[00:01.00]Fixture first\\n[00:05.00]Fixture next\"}",Encoding.UTF8,"application/json")});
        }
    }
}
