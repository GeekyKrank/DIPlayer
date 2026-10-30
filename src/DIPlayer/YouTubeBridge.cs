using System.Text.Json;
namespace DIPlayer;
public sealed record YouTubeItem(string Key, string Title, string Subtitle, string? Artwork, string Kind, string Section = "YouTube Music", string? ArtistKey = null, bool IsVideo = false, string? Album = null, string? Plays = null, string? Duration = null, string? ArtistName = null);
public sealed record YouTubeArtistAction(string Label);
public sealed record YouTubePage(string Title, string Subtitle, string? Artwork, string? Description = null, YouTubeArtistAction[]? Actions = null, string? ArtistName = null);
public sealed record YouTubeState(string Url, string Title, string Artist, string? Artwork, double Position, double Duration, bool Paused, double Volume, bool Shuffle, int Repeat, bool Liked, YouTubeItem[] Items, bool RequiresWebsite = false, bool IsAd = false, YouTubePage? Page = null, bool SignedIn = false, string? Views = null, string? LikeCount = null, string? ArtistKey = null, string? VideoId = null, string? AccountAvatar = null, string? AccountName = null);
public static class YouTubeBridge
{
    public const string Snapshot = """
        (() => {
          if(location.hostname!=='music.youtube.com') return null;
          const readCatalog=true;
          const text=e=>(e?.textContent||'').replace(/\s+/g,' ').trim();
          const artwork=(el,extra=[])=>{
            const candidates=[];
            const thumbnailKeys=['thumbnail','thumbnails','musicThumbnailRenderer','croppedSquareThumbnailRenderer','thumbnailDetails','foregroundThumbnail'];
            if(el?.matches('ytmusic-immersive-header-renderer,ytmusic-visual-header-renderer'))thumbnailKeys.push('backgroundThumbnail');
            const add=(url,width=0,height=0,priority=0)=>{
              if(typeof url!=='string'||!url.startsWith('https:'))return;
              width=Number(width)||0;height=Number(height)||0;
              const size=url.match(/(?:=|-)w(\d+)-h(\d+)(?:-|$)/)||url.match(/=s(\d+)(?:-|$)/);
              if(size){width=Math.max(width,Number(size[1]));height=Math.max(height,Number(size[2]||size[1]));}
              candidates.push({url,area:width*(height||width),priority});
            };
            const collect=(value,depth=0)=>{
              if(!value||depth>8)return;
              if(Array.isArray(value)){for(const item of value.slice(0,32))collect(item,depth+1);return;}
              if(typeof value!=='object')return;
              add(value.url,value.width,value.height,2);
              for(const key of thumbnailKeys)collect(value[key],depth+1);
            };
            collect(el?.data);
            const thumbnails=Array.from(el?.querySelectorAll('ytmusic-thumbnail-renderer,ytmusic-cropped-square-thumbnail-renderer')||[]);
            for(const thumb of thumbnails)collect(thumb.data);
            const images=thumbnails.length?thumbnails.flatMap(thumb=>Array.from(thumb.querySelectorAll('img'))):Array.from(el?.querySelectorAll('img')||[]);
            for(const img of images){
              add(img.currentSrc||img.getAttribute('src'),img.naturalWidth,img.naturalHeight,1);
              add(img.getAttribute('src'));add(img.getAttribute('data-src'));
              for(const entry of (img.getAttribute('srcset')||img.getAttribute('data-srcset')||'').split(',')){
                const match=entry.trim().match(/^(\S+)\s+(\d+(?:\.\d+)?)(w|x)$/);
                if(!match)continue;
                const width=Number(match[2])*(match[3]==='x'?(img.width||img.naturalWidth||1):1);
                const height=img.naturalWidth>0?width*img.naturalHeight/img.naturalWidth:width;
                add(match[1],width,height,1);
              }
            }
            for(const image of extra||[]){const size=image.sizes?.match(/(\d+)x(\d+)/);add(image.src,size?.[1],size?.[2],2);}
            // Thumbnail arrays are not guaranteed to be ordered by resolution.
            // Preserve the chosen URL and its aspect ratio; the image loader sizes it.
            candidates.sort((a,b)=>b.area-a.area||b.priority-a.priority);
            return candidates[0]?.url||null;
          };
          const bar=document.querySelector('ytmusic-player-bar');
          const video=document.querySelector('video');
          const media=navigator.mediaSession?.metadata;
          if(video&&window.__diplayerNativeQueue)video.loop=!!window.__diplayerRepeatOne;
          const queue=document.documentElement.dataset.diplayerView==='queue';
          const nodes=readCatalog?Array.from(document.querySelectorAll(queue?'ytmusic-player-queue-item':'ytmusic-responsive-list-item-renderer,ytmusic-two-row-item-renderer')).filter(e=>queue||!e.closest('ytmusic-player-queue')):[];
          const items=[],seen=new Set();
          for(const el of nodes){
            if(el.closest('ytmusic-search-suggestions-section,ytmusic-search-suggestions'))continue;
            if(!queue&&(el.closest('[hidden]')||!el.getClientRects().length))continue;
            const link=el.querySelector('.title a[href],.title-column a[href],a[href*="watch?"],a[href*="playlist?"],a[href*="browse/"],a[href*="channel/"],a[href*="/@"]');
            if(!link)continue;
            const u=new URL(link.getAttribute('href'),location.origin+'/');
            if(u.hostname!=='music.youtube.com'||!/^\/(watch|playlist|browse|channel|@)/.test(u.pathname))continue;
            const key=u.pathname+u.search;
            const shelf=el.closest('ytmusic-carousel-shelf-renderer,ytmusic-shelf-renderer,ytmusic-grid-renderer');
            const section=text(shelf?.querySelector('h2,.title.ytmusic-carousel-shelf-basic-header-renderer,ytmusic-carousel-shelf-basic-header-renderer .title'))||'YouTube Music';
            const title=text(el.querySelector('.title,.title-column'))||link.getAttribute('aria-label')||text(link);
            if(!title||seen.has(section+'|'+key))continue;
            seen.add(section+'|'+key);
            let art=artwork(el);
            if(!art?.startsWith('https:'))art=u.searchParams.get('v')?'https://i.ytimg.com/vi/'+encodeURIComponent(u.searchParams.get('v'))+'/hqdefault.jpg':null;
            const subtitle=text(el.querySelector('.secondary-flex-columns,.subtitle,.byline')).slice(0,500);
            const artistKey=Array.from(el.querySelectorAll('a[href]')).map(a=>{try{const u=new URL(a.getAttribute('href'),location.origin);return /^\/(@|channel\/|browse\/UC)/.test(u.pathname)?u.pathname+u.search:null;}catch{return null;}}).find(Boolean)||null;
            const kind=/^\/(@|channel\/|browse\/UC)/.test(u.pathname)?'Artist':u.pathname==='/watch'?'Track':/single|сингл|синґл/i.test(subtitle)?'Single':/album|альбом/i.test(subtitle)||u.pathname.startsWith('/browse/MPRE')?'Album':u.pathname==='/playlist'?'Playlist':'Browse';
            const isVideo=kind==='Track'&&(/video|відео|видео/i.test(section)||el.tagName==='YTMUSIC-TWO-ROW-ITEM-RENDERER'&&/views|перегляд|просмотр/i.test(subtitle));
            const albumLink=el.querySelector('.secondary-flex-columns a[href*="browse/MPRE"],.secondary-flex-columns a[href*="playlist?"]');
            const plays=Array.from(el.querySelectorAll('.secondary-flex-columns .flex-column')).map(text).find(s=>/plays|views|прослуш|просмотр|відтвор|перегляд/i.test(s))||null;
            const duration=Array.from(el.querySelectorAll('.fixed-columns .fixed-column,.duration,.time-status')).map(text).find(s=>/^\d{1,2}:\d{2}(?::\d{2})?$/.test(s))||null;
            const artistName=Array.from(el.querySelectorAll('.secondary-flex-columns a[href],.subtitle a[href]')).filter(a=>{try{return /^\/(@|channel\/|browse\/UC)/.test(new URL(a.href,location.origin).pathname);}catch{return false;}}).map(text).filter(Boolean).join(', ');
            items.push({Section:section,Key:key,Title:title.slice(0,300),Subtitle:subtitle,Artwork:art,Kind:kind,ArtistKey:artistKey,IsVideo:isVideo,Album:text(albumLink)||null,Plays:plays,Duration:duration,ArtistName:artistName||null});
            if(items.length>=600)break;
          }
          for(const shelf of (readCatalog?document.querySelectorAll('ytmusic-carousel-shelf-renderer,ytmusic-shelf-renderer'):[])){
            if(items.length>=600)break;
            const header=shelf.querySelector('ytmusic-carousel-shelf-basic-header-renderer,ytmusic-shelf-renderer > .title')||shelf.querySelector('h2')?.parentElement;
            if(!header)continue;
            const section=text(header.querySelector('h2,.title'));
            const link=header.querySelector('a[href*="browse/"],a[href*="playlist?"]');
            const button=header.querySelector('yt-button-renderer')||shelf.querySelector('.bottom-button yt-button-renderer,yt-button-renderer.more-button');
            const endpoint=button?.data?.navigationEndpoint||button?.data?.buttonRenderer?.navigationEndpoint;
            const browse=endpoint?.browseEndpoint?.browseId;
            let key=link?new URL(link.href,location.origin).pathname+new URL(link.href,location.origin).search:browse?'/browse/'+encodeURIComponent(browse)+(endpoint.browseEndpoint.params?'?params='+encodeURIComponent(endpoint.browseEndpoint.params):''):null;
            if(section&&key&&key.startsWith('/')&&!items.some(i=>i.Section===section&&i.Key===key))items.push({Section:section,Key:key,Title:'See all · '+section,Subtitle:'',Artwork:items.find(i=>i.Section===section)?.Artwork||null,Kind:'Browse'});
          }
          const header=readCatalog?Array.from(document.querySelectorAll('ytmusic-detail-header-renderer,ytmusic-responsive-header-renderer,ytmusic-immersive-header-renderer,ytmusic-visual-header-renderer')).find(el=>el.getClientRects().length&&!el.closest('[hidden]')):null;
          const description=text(header?.querySelector('.description'))||header?.data?.description?.runs?.map(r=>r.text).join('')||'';
          const headerButtons=Array.from(header?.querySelectorAll('button,yt-button-renderer,tp-yt-paper-button')||[]).filter((el,i,all)=>!all.some(other=>other!==el&&other.contains(el)));
          const headerArtist=Array.from(header?.querySelectorAll('a[href]')||[]).find(a=>{try{return /^\/(@|channel\/|browse\/UC)/.test(new URL(a.href,location.origin).pathname);}catch{return false;}});
          const page=readCatalog?{ArtistName:text(headerArtist)||text(header?.querySelector('.strapline-text'))||header?.data?.straplineTextOne?.runs?.map(r=>r.text).join('')||null,Title:text(header?.querySelector('.title')),Subtitle:text(header?.querySelector('.subtitle,.monthly-listener,.monthly-listeners'))||header?.data?.monthlyListenerCount?.runs?.map(r=>r.text).join('')||text(header?.querySelector('.strapline')),Artwork:artwork(header),Description:description.slice(0,6000),Actions:headerButtons.map(el=>({Label:text(el)||el.getAttribute('aria-label')||''})).filter(a=>a.Label&&a.Label!==header?.data?.moreButton?.toggleButtonRenderer?.defaultText?.runs?.map(r=>r.text).join(''))}:null;
          const artistLink=Array.from(bar?.querySelectorAll('.byline a[href]')||[]).find(a=>{try{return /^\/(@|channel\/|browse\/UC)/.test(new URL(a.href,location.origin).pathname);}catch{return false;}});
          const player=document.getElementById('movie_player');
          let channel;try{channel=player?.getPlayerResponse?.()?.videoDetails?.channelId;}catch{}
          const playingArtistKey=artistLink?new URL(artistLink.href,location.origin).pathname:channel?'/channel/'+channel:null;
          const buttons=Array.from(bar?.querySelectorAll('button[aria-label]')||[]);
          const repeat=buttons.find(b=>/repeat|повтор|повторен/i.test(b.getAttribute('aria-label')))?.getAttribute('aria-label')||'';
          const shuffle=buttons.find(b=>/shuffle|переміш|перемеш/i.test(b.getAttribute('aria-label')));
          const liked=bar?.querySelector('.like button[aria-pressed="true"],#button-shape-like button[aria-pressed="true"]');
          const byline=text(bar?.querySelector('.byline'))||media?.artist||'';
          const parts=byline.split(/\s*[•·]\s*/);
          const count=value=>{
            const m=value?.match(/\d[\d\s.,]*(?:тыс\.?|тис\.?|млн\.?|млрд\.?|[KMB])?/i);
            if(!m)return null;
            const raw=m[0].trim();
            // Keep compact source values compact: expanding 38M would imply false precision.
            if(/тыс|тис|млн|млрд|[KMB]/i.test(raw))return raw;
            const number=Number(raw.replace(/\D/g,''));
            return Number.isSafeInteger(number)?number.toLocaleString('en-US'):null;
          };
          const candidates=Array.from(bar?.querySelectorAll('.byline,[title],[aria-label]')||[]).flatMap(el=>[el.getAttribute('title'),el.getAttribute('aria-label')]).filter(Boolean).flatMap(s=>s.split(/\s*[•·]\s*/)).filter(s=>!/dislike|не нравится|не подоба/i.test(s));
          const stat=(pattern,fallback)=>{
            const values=candidates.filter(s=>pattern.test(s)).map(count).filter(Boolean);
            return values.find(s=>!/[a-zа-яіїґ]/i.test(s))||count(fallback);
          };
          const views=stat(/views?|просмотр|перегляд|відтвор/i,parts.find(p=>/views?|просмотр|перегляд|відтвор/i.test(p)));
          const likesText=parts.find(p=>/likes?|нрав|вподоб|подоба|лайк/i.test(p))||bar?.querySelector('.like button,#button-shape-like button')?.getAttribute('aria-label')||'';
          const likeCount=stat(/likes?|нрав|вподоб|подоба|лайк/i,likesText);
          // Read the already-rendered account button only; opening account menus
          // would interrupt typing/playback and is unnecessary for its avatar.
          const accountButton=document.querySelector('ytmusic-nav-bar ytmusic-settings-button,ytmusic-settings-button,#avatar-btn');
          const accountImage=accountButton?.querySelector('img');
          let accountAvatar=accountImage?.currentSrc||accountImage?.getAttribute('src')||null;
          try{const url=new URL(accountAvatar);if(url.protocol!=='https:'||!(/^(?:lh\d+|yt\d+)\.googleusercontent\.com$/.test(url.hostname)||url.hostname==='yt3.ggpht.com'))accountAvatar=null;}catch{accountAvatar=null;}
          const accountLabel=(accountButton?.getAttribute('aria-label')||accountButton?.getAttribute('title')||accountImage?.getAttribute('alt')||'').trim();
          const accountName=accountLabel&&!/^(?:account|account menu|avatar|avatar image|profile picture|аккаунт|обліковий запис)$/i.test(accountLabel)?accountLabel.slice(0,160):null;
          let videoId;try{videoId=player?.getVideoData?.()?.video_id;}catch{}
          return {AccountAvatar:accountAvatar,AccountName:accountName,VideoId:videoId||new URL(location.href).searchParams.get('v')||null,ArtistKey:playingArtistKey,Views:views,LikeCount:likeCount,SignedIn:!!accountAvatar,Page:page,Url:location.href,Title:text(bar?.querySelector('.title'))||media?.title||'',Artist:parts[0]||'',Artwork:artwork(bar,media?.artwork),Position:video?.currentTime||0,Duration:Number.isFinite(video?.duration)?video.duration:0,Paused:video?.paused!==false,Volume:video?.volume??1,Shuffle:shuffle?.closest('[aria-pressed]')?.getAttribute('aria-pressed')==='true'||shuffle?.closest('[aria-checked]')?.getAttribute('aria-checked')==='true',Repeat:/one|одн|один/i.test(repeat)?2:/all|вс|усі/i.test(repeat)?1:0,Liked:!!liked,Items:items,RequiresWebsite:!!document.querySelector('ytmusic-popup-container tp-yt-paper-dialog[opened],tp-yt-paper-dialog[opened]'),IsAd:!!document.querySelector('.ad-showing,.ad-interrupting')};
        })()
        """;
    public static readonly string PlaybackSnapshot=Snapshot.Replace("const readCatalog=true;","const readCatalog=false;",StringComparison.Ordinal);
    public static string Navigate(string key)
    {
        string encoded = JsonSerializer.Serialize(key);
        return """
            (()=>{
              const key=KEY;
              const u=new URL(key,'https://music.youtube.com/');
              if(u.hostname!=='music.youtube.com')return false;
              const match=Array.from(document.querySelectorAll('a[href]')).find(a=>{try{const v=new URL(a.getAttribute('href'),location.origin+'/');return v.pathname+v.search===u.pathname+u.search;}catch{return false;}});
              if(match){match.click();return true;}
              let endpoint;
              if(u.pathname==='/search')endpoint={searchEndpoint:{query:u.searchParams.get('q')||''}};
              else if(u.pathname==='/watch')endpoint={watchEndpoint:{videoId:u.searchParams.get('v'),playlistId:u.searchParams.get('list')||undefined}};
              else if(u.pathname==='/playlist')endpoint={browseEndpoint:{browseId:'VL'+u.searchParams.get('list')}};
              else if(u.pathname==='/')endpoint={browseEndpoint:{browseId:'FEmusic_home'}};
              else if(u.pathname==='/library')endpoint={browseEndpoint:{browseId:'FEmusic_library_landing'}};
              else if(u.pathname.startsWith('/channel/'))endpoint={browseEndpoint:{browseId:u.pathname.split('/').pop(),params:u.searchParams.get('params')||undefined}};
              else if(u.pathname.startsWith('/browse/'))endpoint={browseEndpoint:{browseId:u.pathname.split('/').pop(),params:u.searchParams.get('params')||undefined}};
              else return false;
              endpoint.commandMetadata={webCommandMetadata:{url:u.pathname+u.search}};
              document.querySelector('ytmusic-app')?.dispatchEvent(new CustomEvent('yt-navigate',{detail:{endpoint},bubbles:true,composed:true}));
              return true;
            })()
            """.Replace("KEY", encoded);
    }
    public static string Command(string command) => """
        (()=>{
          const action=ACTION,bar=document.querySelector('ytmusic-player-bar'),v=document.querySelector('video');
          const click=el=>{if(!el)return false;el.click();return true;};
          if(action==='play'){if(v){if(v.paused)v.play().catch(()=>{});else v.pause();}return !!v;}
          if(action==='next'){if(click(bar?.querySelector('.next-button,#next-button')))return true;const p=document.getElementById('movie_player');if(p?.nextVideo){p.nextVideo();return true;}return false;}
          if(action==='previous'){if(click(bar?.querySelector('.previous-button,#previous-button')))return true;const p=document.getElementById('movie_player');if(p?.previousVideo){p.previousVideo();return true;}return false;}
          if(action==='stop'){v?.pause();return !!v;}
          if(action==='like'){
            if(click(bar?.querySelector('.like button,#button-shape-like button')))return true;
            const id=new URL(location.href).searchParams.get('v');
            const row=Array.from(document.querySelectorAll('ytmusic-responsive-list-item-renderer')).find(el=>Array.from(el.querySelectorAll('a[href]')).some(a=>{try{return new URL(a.getAttribute('href'),location.origin+'/').searchParams.get('v')===id;}catch{return false;}}));
            return click(row?.querySelector('.like button,#button-shape-like button'));
          }
          const re=action==='shuffle'?/shuffle|переміш|перемеш/i:/repeat|повтор/i;
          return click(Array.from(bar?.querySelectorAll('button[aria-label]')||[]).find(b=>re.test(b.getAttribute('aria-label'))));
        })()
        """.Replace("ACTION", JsonSerializer.Serialize(command));
}
