using System.Diagnostics;
using System.Globalization;
using System.Text;
using DIPlayer.Core;
using NAudio.Wave;

namespace DIPlayer.Audio;

public sealed class HlsPlayer : IDisposable
{
    record Segment(Uri Uri, double Start, double Duration);
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    readonly SemaphoreSlim change = new(1,1);
    CancellationTokenSource? lifetime;
    Task? worker;
    WaveOutEvent? output;
    BufferedWaveProvider? pcm;
    List<Segment> segments = [];
    bool disposed;
    volatile bool paused, faulted;
    double origin, decodedSeconds;
    float volume = .6f;
    public event Action<string>? Error;
    public event Action? Ended;
    public bool Playing => output?.PlaybackState == PlaybackState.Playing;
    public bool Paused => paused;
    public bool Faulted => faulted;
    public double Duration { get; private set; }
    public double Position => Math.Clamp(origin + Interlocked.CompareExchange(ref decodedSeconds,0,0) - (pcm?.BufferedDuration.TotalSeconds ?? 0),0,Duration);
    public double BufferedSeconds => pcm?.BufferedDuration.TotalSeconds ?? 0;
    public float Volume { get => volume; set { volume=Math.Clamp(value,0,1); if(output != null) output.Volume=volume; } }
    public async Task Open(string url, CancellationToken ct, double position=0)
    {
        var uri = new Uri(url);
        var data = await Download(uri, 1024*1024, ct);
        string manifest = Encoding.UTF8.GetString(data);
        if (!manifest.StartsWith("#EXTM3U")) throw new CatalogException("The server did not return an HLS manifest.");
        if(manifest.Contains("#EXT-X-KEY") || manifest.Contains("#EXT-X-BYTERANGE")) throw new CatalogException("Encrypted and byte-range HLS streams are not supported yet.");
        var list = new List<Segment>(); double start=0, duration=0;
        foreach(string line in manifest.Split('\n').Select(s=>s.Trim()))
        {
            if(line.StartsWith("#EXTINF:")) duration=double.Parse(line[8..].TrimEnd(','),CultureInfo.InvariantCulture);
            else if(line.Length>0 && line[0]!='#')
            {
                if (duration<=0) throw new CatalogException("Expected a finite MP3 HLS playlist, not a master or live stream.");
                var item = new Uri(uri,line); SoundCloudCatalog.ValidateCdn(item);
                list.Add(new Segment(item,start,duration)); start+=duration; duration=0;
            }
        }
        if(list.Count==0 || !manifest.Contains("#EXT-X-ENDLIST")) throw new CatalogException("Empty and live streams are not supported yet.");
        ct.ThrowIfCancellationRequested();
        await change.WaitAsync(ct);
        try { await StopCore(); ct.ThrowIfCancellationRequested(); segments=list; Duration=start; await StartCore(Math.Clamp(position,0,Math.Max(0,start-.1)),false); }
        finally { change.Release(); }
    }
    async Task<byte[]> Download(Uri uri,int max,CancellationToken ct)
    {
        SoundCloudCatalog.ValidateCdn(uri);
        using var res = await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!res.IsSuccessStatusCode) throw new CatalogException($"Audio server: HTTP {(int)res.StatusCode}. Try reopening the track.",(int)res.StatusCode);
        return await SoundCloudCatalog.ReadLimited(res.Content,max,ct);
    }
    public void Toggle() { paused=!paused;if(output==null)return;if(paused)output.Pause();else output.Play(); }
    public void Pause() { paused=true;output?.Pause(); }
    public async Task Seek(double seconds, CancellationToken ct=default)
    {
        await change.WaitAsync(ct);
        try { if(segments.Count==0)return; bool wasPaused=paused; await StopCore(); await StartCore(Math.Clamp(seconds,0,Math.Max(0,Duration-.1)),wasPaused); }
        finally { change.Release(); }
    }
    async Task StopCore()
    {
        lifetime?.Cancel();
        output?.Stop();
        if(worker!=null) { try { await worker; } catch(OperationCanceledException) {} }
        output?.Dispose(); output=null;pcm=null;
        lifetime?.Dispose();lifetime=null;worker=null;
    }
    Task StartCore(double seconds,bool startPaused)
    {
        if(disposed)throw new ObjectDisposedException(nameof(HlsPlayer));
        lifetime=new CancellationTokenSource();var ct=lifetime.Token;
        origin=seconds;Interlocked.Exchange(ref decodedSeconds,0);paused=startPaused;faulted=false;
        worker=Task.Run(()=>Pump(seconds,ct),ct);
        return Task.CompletedTask;
    }
    async Task Pump(double seconds,CancellationToken ct)
    {
        IMp3FrameDecompressor? decoder=null;
        try
        {
            int index=segments.FindLastIndex(s=>s.Start<=seconds); if(index<0)index=0;
            double skip=seconds-segments[index].Start;
            byte[] decoded=new byte[65536];
            for(int i=index;i<segments.Count;i++)
            {
                ct.ThrowIfCancellationRequested();
                // One compressed segment, capped at 2 MiB. No whole-track buffer or audio disk cache.
                byte[] bytes=await Download(segments[i].Uri,2*1024*1024,ct);
                using var input=new MemoryStream(bytes,false);
                Mp3Frame? frame;
                while((frame=Mp3Frame.LoadFromStream(input))!=null)
                {
                    ct.ThrowIfCancellationRequested();
                    if(decoder==null)
                    {
                        var format=new Mp3WaveFormat(frame.SampleRate,frame.ChannelMode==ChannelMode.Mono?1:2,frame.FrameLength,frame.BitRate);
                        decoder=new AcmMp3FrameDecompressor(format);
                        pcm=new BufferedWaveProvider(decoder.OutputFormat){BufferDuration=TimeSpan.FromSeconds(4),ReadFully=true,DiscardOnBufferOverflow=false};
                        output=new WaveOutEvent { DesiredLatency=150,NumberOfBuffers=2,Volume=volume };
                        output.Init(pcm);
                        output.PlaybackStopped+=(_,e)=> { if(e.Exception!=null && !ct.IsCancellationRequested) { faulted=true;Error?.Invoke("Audio device disconnected. Connect a device and press Retry."); lifetime?.Cancel(); } };
                        if(!paused)output.Play();
                    }
                    int count=decoder.DecompressFrame(frame,decoded,0);
                    int discard=(int)Math.Min(count,Math.Floor(skip*pcm!.WaveFormat.AverageBytesPerSecond/pcm.WaveFormat.BlockAlign)*pcm.WaveFormat.BlockAlign);
                    skip-=discard/(double)pcm.WaveFormat.AverageBytesPerSecond;
                    if(skip<.0001)skip=0;
                    count-=discard;
                    if(count==0)continue;
                    while(pcm.BufferedBytes+count>pcm.BufferLength)await Task.Delay(40,ct);
                    pcm.AddSamples(decoded,discard,count);
                    Interlocked.Exchange(ref decodedSeconds,decodedSeconds+count/(double)pcm.WaveFormat.AverageBytesPerSecond);
                }
            }
            while(pcm?.BufferedBytes>0)await Task.Delay(100,ct);
            if(!ct.IsCancellationRequested) { output?.Stop(); Ended?.Invoke(); }
        }
        catch(OperationCanceledException){if(!ct.IsCancellationRequested){faulted=true;Error?.Invoke("The audio server timed out. Check your network and press Retry.");output?.Stop();}}
        catch(HttpRequestException){faulted=true;Error?.Invoke("Cannot reach the audio server. Check your network and retry.");output?.Stop();}
        catch(Exception e){ if(!ct.IsCancellationRequested) { faulted=true;Error?.Invoke(e is CatalogException ? e.Message : "Could not decode the stream or open the audio device.");output?.Stop(); } }
        finally{decoder?.Dispose();}
    }
    public async Task Stop() { await change.WaitAsync();try { await StopCore();segments=[];Duration=0; }finally{change.Release();} }
    public void Dispose(){disposed=true;lifetime?.Cancel();output?.Stop();http.Dispose();}
}
