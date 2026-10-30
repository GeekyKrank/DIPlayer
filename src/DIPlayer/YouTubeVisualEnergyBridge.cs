namespace DIPlayer;

// A read-only tap for the lyrics backdrop. Unlike an equalizer it never takes
// ownership of native playback, media volume/mute, or the media element source.
public static class YouTubeVisualEnergyBridge
{
    public static readonly string Install = """
        (()=>{
          if(location.hostname!=='music.youtube.com')return 0;
          if(window.__diplayerVisualEnergy)return window.__diplayerVisualEnergy.read();
          const state={context:null,record:null,failure:null,resuming:false,low:0,high:0,abort:new AbortController(),closed:false};
          const sourceKey=media=>(media?.currentSrc||media?.src||'')+'|'+(media?.crossOrigin??'');
          const safe=media=>{
            if(!media||media.mediaKeys||media.readyState<2||media.paused||media.ended||!media.isConnected||!media.currentSrc&&!media.src)return false;
            try{
              const url=new URL(media.currentSrc||media.src,location.href);
              return url.origin===location.origin&&['https:','http:','blob:'].includes(url.protocol)
                ||['https:','http:'].includes(url.protocol)&&media.crossOrigin!==null&&media.crossOrigin!==undefined;
            }catch{return false;}
          };
          const release=()=>{
            const record=state.record;state.record=null;state.low=state.high=0;
            if(!record)return;
            record.abort.abort();
            for(const node of [record.source,record.analyser,record.silent])try{node?.disconnect();}catch{}
            for(const track of record.stream.getTracks())try{track.stop();}catch{}
          };
          const close=()=>{
            state.closed=true;state.abort.abort();release();
            const context=state.context;state.context=null;
            if(context)try{context.close().catch(()=>{});}catch{}
            if(window.__diplayerVisualEnergy===api)delete window.__diplayerVisualEnergy;
            return true;
          };
          const fail=media=>{state.failure={media,key:sourceKey(media),at:Date.now()};release();};
          const read=()=>{
            if(state.closed)return 0;
            const media=document.querySelector('ytmusic-player video')||document.querySelector('video,audio');
            const key=sourceKey(media);
            if(state.record&&(state.record.media!==media||state.record.key!==key||!safe(media)))release();
            if(!safe(media)||typeof media.captureStream!=='function'||!(window.AudioContext||window.webkitAudioContext))return 0;
            if(state.failure?.media===media&&state.failure.key===key&&Date.now()-state.failure.at<3000)return 0;
            try{
              if(!state.context){
                state.context=new (window.AudioContext||window.webkitAudioContext)();
                const context=state.context;
                context.addEventListener('statechange',()=>{if(state.context===context&&context.state!=='running')release();},{signal:state.abort.signal});
              }
              const context=state.context;
              if(context.state==='closed'){state.context=null;release();return 0;}
              if(context.state!=='running'){
                if(!state.resuming){state.resuming=true;context.resume().catch(()=>{}).finally(()=>{state.resuming=false;});}
                return 0;
              }
              if(!state.record){
                const stream=media.captureStream();
                const record={media,key,stream,source:null,analyser:null,silent:null,track:null,abort:new AbortController(),samples:null,bins:null};
                state.record=record;
                const unavailable=()=>{if(state.record===record)fail(media);};
                stream.addEventListener('addtrack',event=>{if(event.track.kind==='video')event.track.stop();},{signal:record.abort.signal});
                stream.addEventListener('removetrack',event=>{if(event.track===record.track)unavailable();},{signal:record.abort.signal});
                for(const track of stream.getVideoTracks())track.stop();
              }
              const record=state.record;
              const track=record.stream.getAudioTracks().find(track=>track.readyState==='live'&&!track.muted);
              if(!track){fail(media);return 0;}
              if(record.track&&record.track!==track){fail(media);return 0;}
              if(!record.source){
                record.track=track;
                const unavailable=()=>{if(state.record===record)fail(media);};
                track.addEventListener('mute',unavailable,{signal:record.abort.signal});
                track.addEventListener('ended',unavailable,{signal:record.abort.signal});
                record.source=context.createMediaStreamSource(new MediaStream([track]));
                record.analyser=context.createAnalyser();record.analyser.fftSize=2048;record.analyser.smoothingTimeConstant=.4;
                record.silent=context.createGain();record.silent.gain.value=0;
                record.source.connect(record.analyser);record.analyser.connect(record.silent);record.silent.connect(context.destination);
                record.samples=new Float32Array(record.analyser.fftSize);record.bins=new Float32Array(record.analyser.frequencyBinCount);
                state.failure=null;
              }
              record.analyser.getFloatTimeDomainData(record.samples);record.analyser.getFloatFrequencyData(record.bins);
              let power=0;for(const sample of record.samples)power+=sample*sample;
              const rms=Math.sqrt(power/record.samples.length);
              const band=(from,to)=>{
                const step=context.sampleRate/record.analyser.fftSize;
                const first=Math.max(1,Math.ceil(from/step)),last=Math.min(record.bins.length-1,Math.floor(to/step));
                if(last<first)return 0;
                let sum=0;for(let index=first;index<=last;index++){const db=record.bins[index];if(Number.isFinite(db))sum+=Math.pow(10,db/10);}
                return Math.sqrt(sum/(last-first+1));
              };
              const low=band(40,180),high=band(1500,6500);
              const kick=Math.max(0,low-state.low),clap=Math.max(0,high-state.high);
              state.low=state.low*.45+low*.55;state.high=state.high*.45+high*.55;
              const energy=rms*2.2+low*.8+high*.5+kick*2+clap*1.5;
              return Number.isFinite(energy)?Math.max(0,Math.min(1,energy)):0;
            }catch{fail(media);return 0;}
          };
          const api={read,stop:close};window.__diplayerVisualEnergy=api;
          for(const event of ['loadstart','emptied','error','pause','ended'])document.addEventListener(event,event=>{if(state.record?.media===event.target)release();},{capture:true,signal:state.abort.signal});
          window.addEventListener('pagehide',close,{once:true,signal:state.abort.signal});
          return read();
        })()
        """;

    public const string Read = "(()=>{try{return window.__diplayerVisualEnergy?.read()??null;}catch{return 0;}})()";
    public const string Stop = "(()=>{try{return window.__diplayerVisualEnergy?.stop()??true;}catch{return false;}})()";
}
