using System.Text.Json;
namespace DIPlayer;
public sealed record YouTubeItem(string Key, string Title, string Subtitle, string? Artwork, string Kind, string Section = "YouTube Music");
public sealed record YouTubePage(string Title, string Subtitle, string? Artwork);
public sealed record YouTubeState(string Url, string Title, string Artist, string? Artwork, double Position, double Duration, bool Paused, double Volume, bool Shuffle, int Repeat, bool Liked, YouTubeItem[] Items, bool RequiresWebsite = false, bool IsAd = false, YouTubePage? Page = null, bool SignedIn = false, string? Views = null, string? LikeCount = null);
public static class YouTubeBridge
{
    public const string Snapshot = """
        (() => {
          if(location.hostname!=='music.youtube.com') return null;
          const text=e=>(e?.textContent||'').replace(/\s+/g,' ').trim();
          const artwork=el=>{
            if(!el)return null;
            const urls=[];
            const collect=(value,depth=0)=>{
              if(!value||depth>6)return;
              if(Array.isArray(value)){for(const item of value.slice(0,12))collect(item,depth+1);return;}
              if(typeof value!=='object')return;
              if(typeof value.url==='string'&&value.url.startsWith('https:'))urls.push(value.url);
              for(const key of ['thumbnail','thumbnails','musicThumbnailRenderer','croppedSquareThumbnailRenderer','thumbnailDetails','foregroundThumbnail'])collect(value[key],depth+1);
            };
            const dataUrls=[];
            collect(el.data);
            for(const thumb of el.querySelectorAll('ytmusic-thumbnail-renderer,ytmusic-cropped-square-thumbnail-renderer'))collect(thumb.data);
            dataUrls.push(...urls);urls.length=0;
            for(const img of el.querySelectorAll('img')){
              for(const url of [img.currentSrc,img.getAttribute('src'),img.getAttribute('data-src')])if(url?.startsWith('https:'))urls.push(url);
            }
            return dataUrls[dataUrls.length-1]||urls[0]||null;
          };
          const bar=document.querySelector('ytmusic-player-bar');
          const video=document.querySelector('video');
          const media=navigator.mediaSession?.metadata;
          if(video&&window.__diplayerNativeQueue)video.loop=!!window.__diplayerRepeatOne;
          const queue=document.documentElement.dataset.diplayerView==='queue';
          const nodes=Array.from(document.querySelectorAll(queue?'ytmusic-player-queue-item':'ytmusic-responsive-list-item-renderer,ytmusic-two-row-item-renderer')).filter(e=>queue||!e.closest('ytmusic-player-queue'));
          const items=[],seen=new Set();
          for(const el of nodes){
            if(el.closest('ytmusic-search-suggestions-section,ytmusic-search-suggestions'))continue;
            if(!queue&&(el.closest('[hidden]')||!el.getClientRects().length))continue;
            const link=el.querySelector('.title a[href],.title-column a[href],a[href*="watch?"],a[href*="playlist?"],a[href*="browse/"],a[href*="channel/"]');
            if(!link)continue;
            const u=new URL(link.getAttribute('href'),location.origin+'/');
            if(u.hostname!=='music.youtube.com'||!/^\/(watch|playlist|browse|channel)/.test(u.pathname))continue;
            const key=u.pathname+u.search;
            const shelf=el.closest('ytmusic-carousel-shelf-renderer,ytmusic-shelf-renderer,ytmusic-grid-renderer');
            const section=text(shelf?.querySelector('h2,.title.ytmusic-carousel-shelf-basic-header-renderer,ytmusic-carousel-shelf-basic-header-renderer .title'))||'YouTube Music';
            const title=text(el.querySelector('.title,.title-column'))||link.getAttribute('aria-label')||text(link);
            if(!title||seen.has(section+'|'+key))continue;
            seen.add(section+'|'+key);
            let art=artwork(el);
            if(!art?.startsWith('https:'))art=u.searchParams.get('v')?'https://i.ytimg.com/vi/'+encodeURIComponent(u.searchParams.get('v'))+'/hqdefault.jpg':null;
            const subtitle=text(el.querySelector('.secondary-flex-columns,.subtitle,.byline')).slice(0,500);
            const kind=u.pathname==='/watch'?'Track':/single|сингл|синґл/i.test(subtitle)?'Single':/album|альбом/i.test(subtitle)||u.pathname.startsWith('/browse/MPRE')?'Album':u.pathname==='/playlist'?'Playlist':'Browse';
            items.push({Section:section,Key:key,Title:title.slice(0,300),Subtitle:subtitle,Artwork:art,Kind:kind});
            if(items.length>=100)break;
          }
          const header=Array.from(document.querySelectorAll('ytmusic-detail-header-renderer,ytmusic-responsive-header-renderer,ytmusic-immersive-header-renderer')).find(el=>el.getClientRects().length&&!el.closest('[hidden]'));
          const page={Title:text(header?.querySelector('.title')),Subtitle:text(header?.querySelector('.subtitle')),Artwork:artwork(header)};
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
          return {Views:views,LikeCount:likeCount,SignedIn:!!document.querySelector('ytmusic-settings-button img[src^="https:"],#avatar-btn img[src^="https:"]'),Page:page,Url:location.href,Title:text(bar?.querySelector('.title'))||media?.title||'',Artist:parts[0]||'',Artwork:artwork(bar)||media?.artwork?.[0]?.src||null,Position:video?.currentTime||0,Duration:Number.isFinite(video?.duration)?video.duration:0,Paused:video?.paused!==false,Volume:video?.volume??1,Shuffle:shuffle?.closest('[aria-pressed]')?.getAttribute('aria-pressed')==='true'||shuffle?.closest('[aria-checked]')?.getAttribute('aria-checked')==='true',Repeat:/one|одн|один/i.test(repeat)?2:/all|вс|усі/i.test(repeat)?1:0,Liked:!!liked,Items:items,RequiresWebsite:!!document.querySelector('ytmusic-popup-container tp-yt-paper-dialog[opened],tp-yt-paper-dialog[opened]'),IsAd:!!document.querySelector('.ad-showing,.ad-interrupting')};
        })()
        """;
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
              else if(u.pathname.startsWith('/browse/'))endpoint={browseEndpoint:{browseId:u.pathname.split('/').pop()}};
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
