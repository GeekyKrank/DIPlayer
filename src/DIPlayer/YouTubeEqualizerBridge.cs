using System.Text.Json;

namespace DIPlayer;

public static class YouTubeEqualizerBridge
{
    public static string Install(bool enabled, double[] gains, bool headroom = false)
    {
        var values = Enumerable.Range(0, 6).Select(index =>
            index < gains.Length && double.IsFinite(gains[index]) ? Math.Clamp(gains[index], -12, 12) : 0).ToArray();
        return """
            (() => {
              if(location.hostname!=='music.youtube.com')return 'unavailable';
              const desired=
            """ + JsonSerializer.Serialize(new { enabled, gains = values, headroom }) + """
              ;
              if(!window.__diplayerEqualizer){
                const state={enabled:false,gains:[0,0,0,0,0,0],headroom:false,status:'disabled',context:null,record:null,scheduled:false,failure:null,bypassTimer:null};
                const frequencies=[60,170,350,1000,3500,10000];
                const publish=value=>window.__diplayerEqualizerStatus=state.status=value;
                const audible=()=>state.enabled&&state.gains.some(value=>Math.abs(value)>.0001);
                const key=media=>(media.currentSrc||media.src||'')+'|'+(media.crossOrigin??'');
                const safe=media=>{
                  if(!media||media.mediaKeys)return false;
                  const source=media.currentSrc||media.src;
                  if(!source)return false;
                  try{
                    const url=new URL(source,location.href);
                    if(url.origin===location.origin&&['https:','http:','blob:'].includes(url.protocol))return true;
                    return ['https:','http:'].includes(url.protocol)&&media.crossOrigin!==null&&media.crossOrigin!==undefined&&media.readyState>=2;
                  }catch{return false;}
                };
                const silenceOutput=record=>{
                  if(record.output){try{record.output.gain.cancelScheduledValues(0);record.output.gain.value=0;}catch{}}
                };
                const restore=record=>{
                  silenceOutput(record);
                  if(record.ownsMute){record.ownsMute=false;try{record.media.muted=record.nativeMuted;}catch{}}
                };
                const stop=close=>{
                  if(state.bypassTimer!==null){clearTimeout(state.bypassTimer);state.bypassTimer=null;}
                  const record=state.record;state.record=null;
                  if(record){
                    record.abort.abort();restore(record);
                    for(const node of [record.source,...record.filters,record.wet,record.dry,record.output])try{node?.disconnect();}catch{}
                    for(const track of record.stream.getTracks())try{track.stop();}catch{}
                  }
                  if(close&&state.context){const context=state.context;state.context=null;try{context.close().catch(()=>{});}catch{}}
                };
                const schedule=()=>{
                  if(state.scheduled||!audible()&&!state.record)return;
                  state.scheduled=true;
                  queueMicrotask(()=>{state.scheduled=false;apply();});
                };
                const volume=record=>{
                  if(!record.output)return;
                  const value=record.nativeMuted?0:Math.max(0,Math.min(1,Number(record.media.volume)||0));
                  record.output.gain.setTargetAtTime(value,state.context.currentTime,.01);
                };
                const headroomGain=(gains,sampleRate)=>{
                  if(!state.headroom)return 1;
                  const filters=frequencies.map((frequency,index)=>{
                    const omega=2*Math.PI*Math.min(frequency,sampleRate*.45)/sampleRate;
                    const amplitude=Math.pow(10,gains[index]/40),alpha=Math.sin(omega)/2,cosine=-2*Math.cos(omega);
                    return [1+alpha*amplitude,cosine,1-alpha*amplitude,1+alpha/amplitude,cosine,1-alpha/amplitude];
                  });
                  const response=frequency=>{
                    const omega=2*Math.PI*Math.max(1,Math.min(frequency,sampleRate*.499))/sampleRate;
                    const c1=Math.cos(omega),s1=Math.sin(omega),c2=Math.cos(2*omega),s2=Math.sin(2*omega);
                    let decibels=0;
                    for(const [b0,b1,b2,a0,a1,a2] of filters){
                      const nr=b0+b1*c1+b2*c2,ni=b1*s1+b2*s2,dr=a0+a1*c1+a2*c2,di=a1*s1+a2*s2;
                      decibels+=10*Math.log10(Math.max(1e-30,nr*nr+ni*ni)/Math.max(1e-30,dr*dr+di*di));
                    }
                    return decibels;
                  };
                  const upper=Math.min(22000,sampleRate*.49);let peak=0;
                  for(let point=0;point<=384;point++)peak=Math.max(peak,response(20*Math.pow(upper/20,point/384)));
                  for(const frequency of frequencies)peak=Math.max(peak,response(Math.min(frequency,sampleRate*.45)));
                  return peak>.01?Math.pow(10,(-peak-.5)/20):1;
                };
                const bypass=()=>{
                  const record=state.record,context=state.context;
                  if(!record?.ownsMute||!context||context.state!=='running'){stop(true);return publish('disabled');}
                  if(state.bypassTimer!==null)return publish('disabled');
                  // Crossfade to an unprocessed capture before releasing native
                  // output. Both sides retain their own gain/headroom throughout.
                  record.signature='';
                  const neutral=(parameter,value)=>{
                    parameter.cancelScheduledValues(context.currentTime);
                    parameter.setValueAtTime(parameter.value,context.currentTime);
                    parameter.linearRampToValueAtTime(value,context.currentTime+.025);
                  };
                  neutral(record.wet.gain,0);neutral(record.dry.gain,1);
                  state.bypassTimer=setTimeout(()=>{state.bypassTimer=null;if(!audible())stop(true);else apply();},35);
                  return publish('disabled');
                };
                const apply=()=>{
                  try{
                    if(!audible())return bypass();
                    if(state.bypassTimer!==null){clearTimeout(state.bypassTimer);state.bypassTimer=null;}
                    const media=document.querySelector('ytmusic-player video')||document.querySelector('video,audio');
                    if(state.record&&(state.record.media!==media||state.record.key!==key(media)||!media?.isConnected))stop(false);
                    if(!media||!media.currentSrc&&!media.src||media.readyState<2){if(state.record)restore(state.record);return publish('pending');}
                    if(!safe(media)){stop(false);return publish('unavailable');}
                    if(typeof media.captureStream!=='function'||!(window.AudioContext||window.webkitAudioContext)){stop(false);return publish('unavailable');}
                    // Capturing, unlike createMediaElementSource(), never permanently
                    // reroutes the player. Native output is restored immediately if a
                    // source becomes inaccessible, a track mutes, or the context stops.
                    if(!state.context){
                      state.context=new (window.AudioContext||window.webkitAudioContext)();
                      const context=state.context;
                      context.addEventListener('statechange',()=>{
                        if(state.context!==context)return;
                        if(context.state!=='running'&&state.record){restore(state.record);publish('pending');}
                        schedule();
                      });
                    }
                    const context=state.context;
                    if(context.state==='closed'){stop(true);return publish('pending');}
                    if(context.state!=='running'){
                      if(state.record)restore(state.record);
                      context.resume().then(schedule).catch(()=>publish('pending'));
                      return publish('pending');
                    }
                    if(!state.record){
                      if(state.failure?.media===media&&state.failure.key===key(media)&&Date.now()-state.failure.time<3000)return publish('unavailable');
                      let stream;
                      try{stream=media.captureStream();}catch{state.failure={media,key:key(media),time:Date.now()};return publish('unavailable');}
                      const record={media,key:key(media),stream,abort:new AbortController(),source:null,filters:[],output:null,wet:null,dry:null,track:null,nativeMuted:media.muted,ownsMute:false,signature:'',preamp:1};
                      state.record=record;
                      stream.addEventListener('addtrack',event=>{if(event.track.kind==='video')event.track.stop();schedule();},{signal:record.abort.signal});
                      stream.addEventListener('removetrack',schedule,{signal:record.abort.signal});
                      for(const track of stream.getVideoTracks())track.stop();
                    }
                    const record=state.record;
                    const track=record.stream.getAudioTracks().find(value=>value.readyState==='live');
                    if(record.track&&record.track!==track){stop(false);schedule();return publish('pending');}
                    if(!track||track.muted||media.paused){restore(record);return publish('pending');}
                    if(!record.source){
                      record.track=track;
                      const fallback=()=>{if(state.record===record){restore(record);publish('pending');schedule();}};
                      track.addEventListener('mute',fallback,{signal:record.abort.signal});
                      track.addEventListener('ended',fallback,{signal:record.abort.signal});
                      track.addEventListener('unmute',schedule,{signal:record.abort.signal});
                      record.source=context.createMediaStreamSource(new MediaStream([track]));
                      record.filters=frequencies.map(frequency=>{
                        const filter=context.createBiquadFilter();filter.type='peaking';filter.Q.value=1;
                        filter.frequency.value=Math.min(frequency,context.sampleRate*.45);return filter;
                      });
                      record.output=context.createGain();record.output.gain.value=0;
                      record.wet=context.createGain();record.wet.gain.value=0;
                      record.dry=context.createGain();record.dry.gain.value=0;
                      record.source.connect(record.dry);record.dry.connect(record.output);
                      let previous=record.source;
                      for(const filter of record.filters){previous.connect(filter);previous=filter;}
                      previous.connect(record.wet);record.wet.connect(record.output);record.output.connect(context.destination);
                    }
                    const signature=state.gains.join(',')+'|'+state.headroom;
                    if(signature!==record.signature){
                      record.filters.forEach((filter,index)=>{filter.gain.cancelScheduledValues(context.currentTime);filter.gain.setTargetAtTime(state.gains[index],context.currentTime,.015);});
                      record.preamp=headroomGain(state.gains,context.sampleRate);
                      record.wet.gain.cancelScheduledValues(context.currentTime);
                      record.wet.gain.setTargetAtTime(record.preamp,context.currentTime,record.preamp < record.wet.gain.value ? .003 : .02);
                      record.dry.gain.cancelScheduledValues(context.currentTime);record.dry.gain.setTargetAtTime(0,context.currentTime,.01);
                      record.signature=signature;
                    }
                    if(!record.ownsMute){record.nativeMuted=media.muted;record.ownsMute=true;media.muted=true;}
                    volume(record);state.failure=null;
                    return publish('applied');
                  }catch{
                    stop(false);return publish('unavailable');
                  }
                };
                window.__diplayerEqualizerVolume=(media,value)=>{
                  const record=state.record;
                  if(!record||record.media!==media||!record.ownsMute||state.status!=='applied'&&state.bypassTimer===null)return false;
                  record.nativeMuted=false;
                  if(!media.muted)media.muted=true;
                  volume(record);return true;
                };
                for(const event of ['loadedmetadata','canplay','playing','play','pause','loadstart','emptied','error'])
                  document.addEventListener(event,e=>{
                    if(!['VIDEO','AUDIO'].includes(e.target?.tagName))return;
                    if(['loadstart','emptied','error'].includes(event)&&state.record?.media===e.target){stop(false);publish('pending');}
                    schedule();
                  },true);
                document.addEventListener('volumechange',e=>{
                  const record=state.record;
                  if(record?.media!==e.target||!record.ownsMute)return;
                  if(!record.media.muted){record.nativeMuted=false;record.media.muted=true;}
                  volume(record);
                },true);
                new MutationObserver(schedule).observe(document.documentElement,{childList:true,subtree:true,attributes:true,attributeFilter:['src','crossorigin']});
                window.addEventListener('pagehide',()=>{stop(true);publish('disabled');},{once:true});
                window.__diplayerEqualizer={update:config=>{state.enabled=!!config.enabled;state.gains=config.gains;state.headroom=!!config.headroom;return apply();}};
              }
              return window.__diplayerEqualizer.update(desired);
            })()
            """;
    }
}
