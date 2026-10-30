using System.Text.Json;
namespace DIPlayer;
public sealed record YouTubeMenuChoice(string Token, string Label, string? Action = null);
public sealed record YouTubeMenuState(string Title, YouTubeMenuChoice[] Choices, bool TextInput = false, string InputValue = "");
public static class YouTubeMenuBridge
{
    public static string Open(string key) => """
        (()=>{
          if(location.hostname!=='music.youtube.com')return false;
          const u=new URL(KEY,location.origin),id=u.searchParams.get('v');
          const visible=el=>el.getClientRects().length&&!el.closest('[hidden]');
          const rows=Array.from(document.querySelectorAll('ytmusic-responsive-list-item-renderer,ytmusic-two-row-item-renderer,ytmusic-player-queue-item')).filter(visible);
          const row=rows.find(el=>Array.from(el.querySelectorAll('a[href]')).some(a=>{try{const v=new URL(a.href,location.origin);return id?v.searchParams.get('v')===id:v.pathname+v.search===u.pathname+u.search;}catch{return false;}}));
          const menu=row?.querySelector('ytmusic-menu-renderer');
          const buttons=Array.from(menu?.querySelectorAll('button:not([aria-pressed]),tp-yt-paper-icon-button,yt-icon-button')||[]);
          const button=buttons.find(b=>b.getAttribute('aria-label')===menu?.getAttribute('aria-label'))||buttons.find(b=>/menu|actions|дій|действ|more|ще|ещё/i.test(b.getAttribute('aria-label')||''))||buttons.at(-1);
          if(!button)return false;row.scrollIntoView({block:'center'});button.click();return true;
        })()
        """.Replace("KEY", JsonSerializer.Serialize(key));
    public const string Read = """
        (()=>{
          const visible=el=>!el.closest('[hidden]')&&getComputedStyle(el).display!=='none'&&getComputedStyle(el).visibility!=='hidden';
          const dialog=Array.from(document.querySelectorAll('tp-yt-paper-dialog[opened],ytmusic-dialog')).find(visible);
          const root=dialog||Array.from(document.querySelectorAll('ytmusic-menu-popup-renderer')).find(visible);
          if(!root)return {Title:'',Choices:[]};
          const text=el=>(el?.textContent||'').replace(/\s+/g,' ').trim();
          const selector=dialog?'ytmusic-playlist-add-to-option-renderer,yt-list-item-view-model,button,yt-button-renderer,tp-yt-paper-button':'ytmusic-menu-service-item-renderer,ytmusic-menu-navigation-item-renderer,ytmusic-toggle-menu-service-item-renderer';
          const elements=Array.from(root.querySelectorAll(selector)).filter(el=>visible(el)&&(dialog||!el.disabled&&el.getAttribute('aria-disabled')!=='true')).filter((el,i,all)=>!all.some(other=>other!==el&&other.contains(el))).filter(el=>text(el).length>0).slice(0,100);
          const generation=String((window.__diplayerMenuGeneration||0)+1);window.__diplayerMenuGeneration=Number(generation);
          window.__diplayerMenuElements=elements.map(el=>({el,label:text(el)}));
          const input=root.querySelector('input:not([type="hidden"]),textarea');window.__diplayerMenuInput=input||null;
          return {Title:text(root.querySelector('h2,#title,.title'))||'YouTube Music',Choices:elements.map((el,i)=>{const d=el.data?.menuServiceItemRenderer||el.data;const q=d?.serviceEndpoint?.queueAddEndpoint;return {Token:generation+':'+i,Label:text(el),Action:q?(q.queueInsertPosition==='INSERT_AFTER_CURRENT_VIDEO'?'play-next':'queue'):null};}),TextInput:!!input,InputValue:input?.value||''};
        })()
        """;
    public static string Choose(string token, string text) => """
        (()=>{
          const [generation,index]=__DIPLAYER_TOKEN__.split(':');
          if(Number(generation)!==window.__diplayerMenuGeneration)return false;
          const item=window.__diplayerMenuElements?.[Number(index)];
          if(!item?.el.isConnected||item.el.closest('[hidden]'))return false;
          const input=window.__diplayerMenuInput;
          if(input?.isConnected){const proto=input.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(input,__DIPLAYER_INPUT__);input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));}
          item.el.click();return true;
        })()
        """.Replace("__DIPLAYER_TOKEN__",JsonSerializer.Serialize(token)).Replace("__DIPLAYER_INPUT__",JsonSerializer.Serialize(text));
    public const string Close = """
        (()=>{document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',code:'Escape',keyCode:27,bubbles:true}));for(const dialog of document.querySelectorAll('tp-yt-paper-dialog[opened]'))dialog.close?.();document.querySelector('ytmusic-popup-container')?.dispatchEvent(new CustomEvent('yt-close-popup',{bubbles:true,composed:true}));window.__diplayerMenuElements=[];})()
        """;
}
