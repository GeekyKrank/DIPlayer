namespace DIPlayer;
public sealed record YouTubeLyricsLine(string Text, bool Active, double? Start = null);
public sealed record YouTubeLyricsState(YouTubeLyricsLine[] Lines, string? Message = null);
public static class YouTubeLyricsBridge
{
    public const string Open = """
        (()=>{
          if(location.hostname!=='music.youtube.com')return false;
          const tabs=Array.from(document.querySelectorAll('ytmusic-player-page tp-yt-paper-tab,ytmusic-player-page [role="tab"]'));
          const tab=tabs.find(e=>/lyrics|текст|слова|песни|пісні/i.test(e.textContent||e.getAttribute('aria-label')||''));
          if(!tab||tab.disabled||tab.getAttribute('aria-disabled')==='true')return false;tab.click();return true;
        })()
        """;
    public const string Read = """
        (()=>{
          const visible=e=>e.getClientRects().length&&!e.closest('[hidden]');
          const root=Array.from(document.querySelectorAll('ytmusic-synced-lyrics-renderer,ytmusic-player-page ytmusic-description-shelf-renderer,ytmusic-player-page [page-type="MUSIC_PAGE_TYPE_TRACK_LYRICS"]')).find(visible);
          if(!root)return {Lines:[],Message:'Lyrics are not available for this track.'};
          const timed=Array.from(root.querySelectorAll('[data-start-time],.lyric-line,.lyrics-line,ytmusic-lyrics-line-renderer'));
          const start=e=>{const d=e.data||{};const ms=d.startTimeMs??d.startTimeMilliseconds??d.cue?.startTimeMs;const raw=e.getAttribute('data-start-time');const value=ms!=null?Number(ms)/1000:raw!=null?Number(raw):d.startTimeSeconds!=null?Number(d.startTimeSeconds):null;return value!=null&&Number.isFinite(value)?value:null;};
          if(timed.length)return {Lines:timed.map(e=>({Start:start(e),Text:(e.textContent||'').trim(),Active:e.getAttribute('aria-current')==='true'||e.classList.contains('active')||e.classList.contains('current')||e.hasAttribute('active')||e.getAttribute('aria-selected')==='true'})).filter(l=>l.Text).slice(0,500)};
          const content=root.querySelector('.description,yt-formatted-string[slot="description"],#description')||root;
          return {Lines:(content.innerText||content.textContent||'').split(/\n/).map(s=>({Text:s.trim(),Active:false})).filter(l=>l.Text).slice(0,500)};
        })()
        """;
}
