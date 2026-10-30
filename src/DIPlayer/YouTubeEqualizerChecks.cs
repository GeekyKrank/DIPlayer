using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyYouTubeEqualizerFixture(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory()!=null,"Equalizer browser checks require isolated storage");
        ++browserGeneration;DisposeMusicBrowser();browserMode=true;
        var browser=new Microsoft.Web.WebView2.Wpf.WebView2();
        musicBrowser=browser;BrowserHost.Child=browser;
        browser.LayoutUpdated+=(_,_)=>ClipYouTubeHost();ParkYouTubeBrowser();
        var options=new CoreWebView2EnvironmentOptions {AdditionalBrowserArguments="--autoplay-policy=no-user-gesture-required"};
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(directory,"equalizer-browser"),options);
        await browser.EnsureCoreWebView2Async(environment);
        var core=browser.CoreWebView2;
        const string html="""
            <!doctype html><html><body><audio id="fixture" loop muted></audio><script>
            window.fixtureContexts=[];
            const NativeContext=window.AudioContext;
            window.AudioContext=class extends NativeContext {
              constructor(...args){super(...args);window.fixtureContexts.push(this);this.fixtureFilters=[];this.fixtureGains=[];}
              createBiquadFilter(){const node=super.createBiquadFilter();this.fixtureFilters.push(node);return node;}
              createGain(){const node=super.createGain();this.fixtureGains.push(node);return node;}
              createMediaStreamSource(stream){this.fixtureStream=stream;return super.createMediaStreamSource(stream);}
            };
            const rate=48000,count=rate*4,bytes=new ArrayBuffer(44+count*2),data=new DataView(bytes);
            const text=(at,value)=>{for(let i=0;i<value.length;i++)data.setUint8(at+i,value.charCodeAt(i));};
            text(0,'RIFF');data.setUint32(4,36+count*2,true);text(8,'WAVE');text(12,'fmt ');
            data.setUint32(16,16,true);data.setUint16(20,1,true);data.setUint16(22,1,true);
            data.setUint32(24,rate,true);data.setUint32(28,rate*2,true);data.setUint16(32,2,true);data.setUint16(34,16,true);
            text(36,'data');data.setUint32(40,count*2,true);
            for(let i=0;i<count;i++)data.setInt16(44+i*2,Math.round(Math.sin(2*Math.PI*1000*i/rate)*3276),true);
            const audio=document.getElementById('fixture');audio.volume=.02;audio.muted=true;
            window.fixtureBlob=URL.createObjectURL(new Blob([bytes],{type:'audio/wav'}));audio.src=window.fixtureBlob;
            audio.play().catch(error=>window.fixtureError=error.name);
            </script></body></html>
            """;
        core.AddWebResourceRequestedFilter("https://music.youtube.com/*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,e)=>e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html; charset=utf-8");
        core.Navigate("https://music.youtube.com/diplayer-equalizer-fixture");
        for(int i=0;i<100;i++)
        {
            await Task.Delay(80);
            if(await core.ExecuteScriptAsync("!!document.getElementById('fixture')&&document.getElementById('fixture').readyState>=2&&!document.getElementById('fixture').paused")=="true")break;
        }
        string status="pending";
        double[] gains=[0,0,0,6,0,0];
        for(int i=0;i<60&&status!="applied";i++)
        {
            status=JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(YouTubeEqualizerBridge.Install(true,gains)))??"pending";
            if(status!="applied")await Task.Delay(100);
        }
        await File.WriteAllTextAsync(Path.Combine(directory,"equalizer-browser-initial.json"),await core.ExecuteScriptAsync("JSON.stringify({status:window.__diplayerEqualizerStatus,error:window.fixtureError,ready:document.getElementById('fixture')?.readyState,paused:document.getElementById('fixture')?.paused,contexts:window.fixtureContexts?.map(c=>({state:c.state,filters:c.fixtureFilters.length,tracks:c.fixtureStream?.getAudioTracks().map(t=>({muted:t.muted,state:t.readyState}))}))})"));
        CheckInteraction(status=="applied","Real Web Audio equalizer did not attach to a playing same-origin blob");
        await Task.Delay(220);
        const string inspect="""
            (()=>{
              const context=window.fixtureContexts[0],audio=document.getElementById('fixture');
              const frequencies=new Float32Array([1000]),magnitude=new Float32Array(1),phase=new Float32Array(1);
              let gain=1;
              for(const filter of context.fixtureFilters){filter.getFrequencyResponse(frequencies,magnitude,phase);gain*=magnitude[0];}
              window.fixtureAnalyser=context.createAnalyser();window.fixtureAnalyser.fftSize=2048;
              context.fixtureFilters[5].connect(window.fixtureAnalyser);
              return {contexts:window.fixtureContexts.length,bands:context.fixtureFilters.length,centers:context.fixtureFilters.map(f=>f.frequency.value),responseDb:20*Math.log10(gain),nativeMuted:audio.muted,volume:audio.volume,paused:audio.paused};
            })()
            """;
        string response=await core.ExecuteScriptAsync(inspect);
        using(var state=JsonDocument.Parse(response))
        {
            var value=state.RootElement;
            CheckInteraction(value.GetProperty("contexts").GetInt32()==1&&value.GetProperty("bands").GetInt32()==6,"Real equalizer did not create exactly one six-band graph");
            CheckInteraction(Math.Abs(value.GetProperty("responseDb").GetDouble()-6)<.15,"Real Web Audio peak response did not measure +6 dB at 1 kHz");
            CheckInteraction(value.GetProperty("nativeMuted").GetBoolean()&&!value.GetProperty("paused").GetBoolean(),"Equalizer restarted playback or changed original mute intent");
        }
        await Task.Delay(150);
        string rms=await core.ExecuteScriptAsync("(()=>{const samples=new Float32Array(window.fixtureAnalyser.fftSize);window.fixtureAnalyser.getFloatTimeDomainData(samples);return Math.sqrt(samples.reduce((sum,value)=>sum+value*value,0)/samples.length);})()");
        CheckInteraction(double.TryParse(rms,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double energy)&&energy>.05,"The live captured audio graph produced no PCM samples");
        await core.ExecuteScriptAsync(YouTubeEqualizerBridge.Install(true,[3,0,0,-6,0,0]));
        CheckInteraction(await core.ExecuteScriptAsync("window.fixtureContexts.length===1&&window.fixtureContexts[0].fixtureFilters.length===6")=="true","Live update leaked a second AudioContext or duplicate filters");
        await core.ExecuteScriptAsync(YouTubeEqualizerBridge.Install(true,gains,headroom:true));
        await Task.Delay(220);
        string compensatedResponse=await core.ExecuteScriptAsync("""
            (()=>{
              const context=window.fixtureContexts[0],frequency=new Float32Array([1000]),magnitude=new Float32Array(1),phase=new Float32Array(1);
              let gain=1;
              for(const filter of context.fixtureFilters){filter.getFrequencyResponse(frequency,magnitude,phase);gain*=magnitude[0];}
              return 20*Math.log10(gain*context.fixtureGains[1].gain.value);
            })()
            """);
        CheckInteraction(double.TryParse(compensatedResponse,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double compensatedDb)
            &&Math.Abs(compensatedDb+.5)<.15,"Real Web Audio automatic headroom did not compensate the 1 kHz boost with a half-decibel margin");
        CheckInteraction(JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(YouTubeEqualizerBridge.Install(false,gains)))=="disabled","Equalizer disable did not report bypass");
        bool settledBypass=false;
        for(int attempt=0;attempt<30;attempt++)
        {
            settledBypass=await core.ExecuteScriptAsync("document.getElementById('fixture').muted&&!document.getElementById('fixture').paused&&window.fixtureContexts[0].state==='closed'")=="true";
            if(settledBypass)break;
            await Task.Delay(25);
        }
        CheckInteraction(settledBypass,"Settled bypass did not restore original muted playback and close the graph");
        await core.ExecuteScriptAsync("(()=>{const audio=document.getElementById('fixture');window.fixtureVisualBefore={muted:audio.muted,volume:audio.volume,src:audio.src,currentSrc:audio.currentSrc};})()");
        await core.ExecuteScriptAsync(YouTubeVisualEnergyBridge.Install);
        double visualEnergy=0;
        for(int attempt=0;attempt<40;attempt++)
        {
            await Task.Delay(50);
            string measured=await core.ExecuteScriptAsync(YouTubeVisualEnergyBridge.Read);
            if(double.TryParse(measured,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out visualEnergy)&&visualEnergy>.03)break;
        }
        CheckInteraction(visualEnergy>.03&&visualEnergy<=1,"Read-only visual analyser produced no energy from captured live sine PCM");
        CheckInteraction(await core.ExecuteScriptAsync("(()=>{const audio=document.getElementById('fixture'),before=window.fixtureVisualBefore;return audio.muted===before.muted&&audio.volume===before.volume&&audio.src===before.src&&audio.currentSrc===before.currentSrc&&!audio.paused;})()")=="true","Visual analyser changed native media volume, mute, source or playback");
        CheckInteraction(await core.ExecuteScriptAsync("window.fixtureContexts.length===2&&window.fixtureContexts[1].state==='running'&&window.fixtureContexts[1].fixtureFilters.length===0&&window.fixtureContexts[1].fixtureGains.length===1&&window.fixtureContexts[1].fixtureGains[0].gain.value===0")=="true","Visual analyser did not create exactly one silent, separate read-only graph");
        await core.ExecuteScriptAsync("window.fixtureVisualStream=window.fixtureContexts[1].fixtureStream");
        for(int attempt=0;attempt<4;attempt++)await core.ExecuteScriptAsync(YouTubeVisualEnergyBridge.Read);
        await core.ExecuteScriptAsync(YouTubeVisualEnergyBridge.Install);
        CheckInteraction(await core.ExecuteScriptAsync("window.fixtureContexts.length===2&&window.fixtureContexts[1].fixtureStream===window.fixtureVisualStream")=="true","Repeated visual reads/install duplicated the context or capture graph");
        CheckInteraction(await core.ExecuteScriptAsync(YouTubeVisualEnergyBridge.Stop)=="true","Visual analyser did not stop");
        bool visualStopped=false;
        for(int attempt=0;attempt<30;attempt++)
        {
            visualStopped=await core.ExecuteScriptAsync("!window.__diplayerVisualEnergy&&window.fixtureContexts[1].state==='closed'&&window.fixtureVisualStream.getTracks().every(track=>track.readyState==='ended')")=="true";
            if(visualStopped)break;
            await Task.Delay(25);
        }
        CheckInteraction(visualStopped,"Visual stop retained its page singleton, context or capture tracks");
        CheckInteraction(await core.ExecuteScriptAsync("(()=>{const audio=document.getElementById('fixture'),before=window.fixtureVisualBefore;return audio.muted===before.muted&&audio.volume===before.volume&&audio.src===before.src&&audio.currentSrc===before.currentSrc&&!audio.paused;})()")=="true","Visual analyser cleanup changed native audio");
        await File.WriteAllTextAsync(Path.Combine(directory,"visual-energy-browser-check.txt"),$"PASS real WebView2: read-only capture of same-origin sine WAV measured visual energy {visualEnergy:F4}; native volume/mute/src/currentSrc/playback unchanged, one zero-gain graph reused across reads/install, Stop released context, capture tracks and singleton. No audible output or remote data.");
        await File.WriteAllTextAsync(Path.Combine(directory,"equalizer-browser-response.json"),response);
        await File.WriteAllTextAsync(Path.Combine(directory,"equalizer-browser-check.txt"),$"PASS real WebView2: muted same-origin blob WAV captured live, six real peaking filters, +6 dB response at 1 kHz, live PCM RMS {energy:F4}, graph reused for updates, automatic headroom measures {compensatedDb:F2} dB, smooth bypass settles with original playback/mute preserved and context closed. No audible output, remote tracks or accounts used.");
        await core.ExecuteScriptAsync("document.getElementById('fixture').pause();URL.revokeObjectURL(window.fixtureBlob)");
        await ApplyBrowserMode(false);
    }
}
