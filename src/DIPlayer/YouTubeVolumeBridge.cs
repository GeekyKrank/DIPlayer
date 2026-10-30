using System.Text.Json;
namespace DIPlayer;
public static class YouTubeVolumeBridge
{
    public static string Install(double value) => """
        (() => {
          if(location.hostname !== 'music.youtube.com') return;
          const initial =
        """ + JsonSerializer.Serialize(Math.Clamp(value, 0, 1)) + """
        ;
          if (!window.__diplayerSetVolume) {
            let desired = initial;
            const apply = video => {
              if(video?.tagName!=='VIDEO') return;
              if(Math.abs(video.volume-desired)>.0001) video.volume=desired;
            };
            window.__diplayerSetVolume = value => {
              desired=Math.max(0,Math.min(1,value));
              for(const video of document.querySelectorAll('video')) { apply(video); if(!window.__diplayerEqualizerVolume?.(video,desired)) video.muted=false; }
            };
            for(const event of ['loadstart','loadedmetadata','play','volumechange'])
              document.addEventListener(event, e=>apply(e.target), true);
            for(const video of document.querySelectorAll('video')) apply(video);
          }
          window.__diplayerSetVolume(initial);
        })();
        """;
}
