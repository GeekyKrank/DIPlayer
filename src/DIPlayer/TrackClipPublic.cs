using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DIPlayer;

public partial class MainWindow
{
    WebView2? publicClipBrowser;
    string? publicClipSong;
    ClipPlayback? publicClipPlayback;
    Task publicClipReturn = Task.CompletedTask;
    sealed record ClipPlayback(double Position, bool Paused, bool Buffering = false);

    async Task<CoreWebView2?> OpenPublicClip(CoreWebView2 music, int revision, Action<CoreWebView2>? configureBrowser)
    {
        var state = JsonSerializer.Deserialize<YouTubeState>(await music.ExecuteScriptAsync(YouTubeBridge.PlaybackSnapshot));
        if (revision != clipRevision || state is not { IsAd: false }) return null;
        string? selected = SelectedClipId(state);
        string? songId = state.VideoId ?? selected;
        if (songId == null || !IsClipId(songId) || selected == null && string.IsNullOrWhiteSpace(state.Title)) return null;
        string? direct = selected ?? JsonSerializer.Deserialize<string>(await music.ExecuteScriptAsync(PublicClipIdScript));
        if (revision != clipRevision) return null;
        var browser = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        var host = new Border { Child = browser, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        publicClipBrowser = browser;
        Grid.SetRow(host, 2);
        BrowserPanel.Children.Add(host);
        bool opened = false;
        bool Active() => revision == clipRevision && publicClipBrowser == browser && musicBrowser?.CoreWebView2 == music;
        try
        {
            await browser.EnsureCoreWebView2Async(music.Environment);
            if (!Active()) return null;
            // Keep a desktop-sized viewport for the search and adaptive player
            // while the native child window is still hidden behind the track UI.
            if (browser.Handle != IntPtr.Zero)
            {
                var region = CreateRectRgn(0, 0, 0, 0);
                if (SetWindowRgn(browser.Handle, region, true) == 0) DeleteObject(region);
                else StretchPublicClipHost(host);
            }
            var core = browser.CoreWebView2;
            core.IsMuted = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NavigationStarting += (_, args) =>
            {
                args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
                    uri.Scheme != "https" || uri.Host != "www.youtube.com" || uri.AbsolutePath is not ("/watch" or "/results");
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (!Active() || !clipMode || !Uri.TryCreate(args.Source, UriKind.Absolute, out var source) ||
                    source.Scheme != "https" || source.Host != "www.youtube.com" || source.AbsolutePath != "/watch") return;
                string message;
                try { message = args.TryGetWebMessageAsString(); }
                catch (ArgumentException) { return; }
                HandleClipMessage(message);
            };
            configureBrowser?.Invoke(core);

            async Task<string> ReadClipPage(string script)
            {
                // A site-initiated reload can interrupt a script during navigation.
                try { return await core.ExecuteScriptAsync(script); }
                catch (System.Runtime.InteropServices.COMException) { return "null"; }
            }

            async Task<bool> OpenVideo(string id)
            {
                core.Navigate("https://www.youtube.com/watch?v=" + id);
                string expected = JsonSerializer.Serialize(id);
                string check = PublicClipReadyScript.Replace("EXPECTED_ID", expected);
                for (int attempt = 0, adFrames = 0; attempt < 80 && Active(); attempt++)
                {
                    string status = await ReadClipPage(check);
                    if (status == "\"ready\"" && await ReadClipPage(OpenClipScript) == "true") return true;
                    if (status == "\"unavailable\"") break;
                    if (status == "\"ad\"" && adFrames++ < 160) attempt--;
                    await Task.Delay(250);
                }
                return false;
            }

            bool ready = IsClipId(direct) && await OpenVideo(direct!);
            if (!ready && selected == null && Active() && !string.IsNullOrWhiteSpace(state.Artist))
            {
                string query = Uri.EscapeDataString($"{state.Artist} {state.Title} official music video");
                core.Navigate("https://www.youtube.com/results?search_query=" + query);
                string search = FindPublicClipScript.Replace("EXPECTED_TRACK", JsonSerializer.Serialize(new
                {
                    title = state.Title, artist = state.Artist, channel = state.ArtistKey, duration = state.Duration
                }));
                for (int attempt = 0; attempt < 32 && Active(); attempt++)
                {
                    var candidates = JsonSerializer.Deserialize<string[]>(await ReadClipPage(search));
                    if (candidates != null)
                    {
                        foreach (string id in candidates.Where(id => IsClipId(id) && id != direct).Take(2))
                        {
                            if (!Active()) return null;
                            if (await OpenVideo(id)) { ready = true; break; }
                        }
                        break;
                    }
                    await Task.Delay(250);
                }
            }
            if (!ready || !Active()) return null;

            // The song keeps playing while YouTube loads. Transfer its current
            // position only once the public player can actually display the clip.
            string result = await music.ExecuteScriptAsync(MusicClipPlaybackScript(songId, null));
            var playback = JsonSerializer.Deserialize<ClipPlayback>(result);
            if (playback == null && selected == null) return null;
            playback ??= new ClipPlayback(0, false);
            if (!Active())
            {
                if (musicBrowser?.CoreWebView2 == music && !quitting)
                    await music.ExecuteScriptAsync(MusicClipPlaybackScript(songId, playback));
                return null;
            }
            publicClipSong = songId;
            publicClipPlayback = playback;
            await core.ExecuteScriptAsync("window.__diplayerClip?.start(" + JsonSerializer.Serialize(playback) + "," +
                JsonSerializer.Serialize(settings.Volume) + "," + (settings.RepeatMode == 2 && !youtubeQueueCleared ? "true" : "false") + ")");
            opened = Active();
            return opened ? core : null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException)
        {
            return null;
        }
        finally
        {
            if (!opened && publicClipBrowser == browser) ClosePublicClip();
        }
    }

    void RevealPublicClip()
    {
        if (publicClipBrowser is not { CoreWebView2: { } core, Parent: Border host }) return;
        StretchPublicClipHost(host);
        host.IsHitTestVisible = true;
        UpdateLayout();
        if (publicClipBrowser.Handle != IntPtr.Zero) SetWindowRgn(publicClipBrowser.Handle, IntPtr.Zero, true);
        core.IsMuted = false;
        publicClipBrowser.Focus();
    }

    static void StretchPublicClipHost(Border host)
    {
        host.Width = host.Height = double.NaN;
        host.HorizontalAlignment = HorizontalAlignment.Stretch;
        host.VerticalAlignment = VerticalAlignment.Stretch;
    }

    bool ClosePublicClip()
    {
        if (publicClipBrowser is not { } browser) return false;
        publicClipBrowser = null;
        var core = browser.CoreWebView2;
        string? song = publicClipSong;
        var initial = publicClipPlayback;
        publicClipSong = null;
        publicClipPlayback = null;
        var host = (Border)browser.Parent;
        host.IsHitTestVisible = false;
        host.Visibility = Visibility.Hidden;
        if (core == null || song == null || initial == null)
        {
            BrowserPanel.Children.Remove(host);
            browser.Dispose();
            return true;
        }
        core.IsMuted = true;
        int revision = clipRevision;
        var music = musicBrowser?.CoreWebView2;
        publicClipReturn = Guard(async () =>
        {
            ClipPlayback playback = initial;
            try
            {
                playback = JsonSerializer.Deserialize<ClipPlayback>(
                    await core.ExecuteScriptAsync("window.__diplayerClip?.playback()")) ?? initial;
            }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException) { }
            finally
            {
                BrowserPanel.Children.Remove(host);
                browser.Dispose();
            }
            if (!quitting && revision == clipRevision && music != null && musicBrowser?.CoreWebView2 == music)
                await music.ExecuteScriptAsync(MusicClipPlaybackScript(song, playback));
        });
        return true;
    }

    async Task MoveClipTrack(string action)
    {
        if (!clipMode) return;
        if (publicClipSong is not { } previous)
        {
            await RunYouTubeControl(action);
            return;
        }
        CloseTrackClip();
        int revision = clipRevision;
        await publicClipReturn;
        if (revision != clipRevision || !youtubeLyricsOpen) return;
        await RunYouTubeControl(action);
        for (int attempt = 0; attempt < 20 && revision == clipRevision && youtubeLyricsOpen; attempt++)
        {
            await Task.Delay(250);
            await RefreshYouTubeNative();
            if (revision != clipRevision || !youtubeLyricsOpen) return;
            if (youtubeState is { VideoId: { } id, IsAd: false } && id != previous)
            {
                await ShowTrackClip();
                return;
            }
        }
    }

    static bool IsClipId(string? id) => id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z");
    static string? VideoIdFromWatchKey(string? key) =>
        key != null && Regex.Match(key, @"(?:\?|&)v=([A-Za-z0-9_-]{11})(?:&|$)") is { Success: true } match ? match.Groups[1].Value : null;
    string? SelectedClipId(YouTubeState? state) => youtubeSelectedVideoId is { } id &&
        (state?.VideoId == id || state?.VideoId == null && VideoIdFromWatchKey(state?.Url) == id) ? id : null;

    static string MusicClipPlaybackScript(string id, ClipPlayback? playback) => """
        (() => {
          if (location.hostname !== 'music.youtube.com') return null;
        """ + MusicClipProbe + """

          if ((mediaId || new URL(location.href).searchParams.get('v')) !== EXPECTED_ID || !video ||
              document.querySelector('.ad-showing,.ad-interrupting')) return null;
          const restore = PLAYBACK_STATE;
          if (restore) {
            if (Number.isFinite(video.duration)) video.currentTime = Math.min(restore.Position, Math.max(0, video.duration - .25));
            if (restore.Paused) video.pause(); else video.play().catch(() => {});
            return true;
          }
          const playback = { Position: video.currentTime, Paused: video.paused };
          video.pause();
          return playback;
        })()
        """.Replace("EXPECTED_ID", JsonSerializer.Serialize(id)).Replace("PLAYBACK_STATE", JsonSerializer.Serialize(playback));

    const string PublicClipIdScript = """
        (() => {
        """ + MusicClipProbe + """

          const alternate = response?.microformat?.microformatDataRenderer?.alternateVideoId;
          if (alternate) return alternate;
          return /MUSIC_VIDEO_TYPE_(OMV|UGC|OFFICIAL_SOURCE_MUSIC)$/.test(type) ||
            (picture && !songMode) ? mediaId : null;
        })()
        """;

    const string PublicClipReadyScript = """
        (() => {
          if (location.hostname !== 'www.youtube.com' || location.pathname !== '/watch' ||
              new URL(location.href).searchParams.get('v') !== EXPECTED_ID) return null;
          const player = document.getElementById('movie_player');
          const video = player?.querySelector('video');
          if (player?.matches('.ad-showing,.ad-interrupting')) return 'ad';
          const status = window.ytInitialPlayerResponse?.playabilityStatus?.status || player?.getPlayerResponse?.()?.playabilityStatus?.status;
          if (['ERROR', 'UNPLAYABLE', 'LOGIN_REQUIRED'].includes(status)) return 'unavailable';
          if (!video?.videoWidth || !Number.isFinite(video.duration)) return null;
          return 'ready';
        })()
        """;

    const string FindPublicClipScript = """
        (() => {
          if (location.hostname !== 'www.youtube.com' || location.pathname !== '/results') return null;
          const expected = EXPECTED_TRACK;
          const normalize = value => value.normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
          const trimVideoLabel = value => normalize(value.replace(/[([]\s*(?:prod\.?|produced)\s+(?:by\s+)?[^)\]]+[)\]]/gi, ' '))
            .replace(/(?:\s+(?:official music video|official video|music video|video oficial|официальный клип|офіційний кліп|4k|full hd|hd|\d{3,4}p))+$/, '');
          const title = trimVideoLabel(expected.title), artist = normalize(expected.artist);
          if (!title || !artist) return [];
          const channelId = expected.channel?.startsWith('/channel/') ? expected.channel.slice(9)
            : expected.channel?.startsWith('/browse/UC') ? expected.channel.slice(8) : '';
          const sections = window.ytInitialData?.contents?.twoColumnSearchResultsRenderer?.primaryContents?.sectionListRenderer?.contents || [];
          const initialRows = sections.flatMap(section => section.itemSectionRenderer?.contents || [])
            .flatMap(item => item.videoRenderer ? [{ data: item.videoRenderer }] : []);
          const rows = initialRows.length ? initialRows : Array.from(document.querySelectorAll('ytd-video-renderer'));
          if (!rows.length) return null;
          const candidates = [];
          for (const row of rows.slice(0, 32)) {
            const data = row.data;
            const anchor = row.querySelector?.('a#video-title[href]');
            const rawTitle = data?.title?.runs?.map(run => run.text).join('') || anchor?.textContent?.trim() || '';
            const id = data?.videoId || (anchor ? new URL(anchor.href).searchParams.get('v') : null);
            const owner = data?.ownerText?.runs?.[0];
            const channel = normalize(owner?.text || row.querySelector?.('#channel-name a')?.textContent || '');
            if (!id || /(?:^| )topic$/.test(channel)) continue;
            const name = trimVideoLabel(rawTitle);
            const prefixed = name === artist + ' ' + title || name === title + ' ' + artist;
            if (name !== title && !prefixed) continue;
            const sameChannel = channelId && owner?.navigationEndpoint?.browseEndpoint?.browseId === channelId;
            const sameArtist = channel === artist || channel === artist + ' official' || channel === artist + ' vevo' ||
              channel.replaceAll(' ', '') === artist.replaceAll(' ', '') + 'vevo';
            const artistAlias = prefixed && channel.startsWith(artist + ' ');
            const official = /music video|official video|video oficial|официальный клип|офіційний кліп/i.test(rawTitle);
            if (!sameChannel && !sameArtist && !artistAlias && !(prefixed && official)) continue;
            const clock = data?.lengthText?.simpleText || row.querySelector?.('ytd-thumbnail-overlay-time-status-renderer')?.textContent?.trim() || '';
            const duration = /^\d+(?::\d{2}){1,2}$/.test(clock) ? clock.split(':').reduce((total, part) => total * 60 + Number(part), 0) : 0;
            if (duration && expected.duration > 0 && Math.abs(duration - expected.duration) > Math.max(45, expected.duration * .25)) continue;
            candidates.push({ id, score: (sameChannel ? 8 : sameArtist ? 5 : artistAlias ? 3 : 0) + (official ? 4 : 0) });
          }
          return [...new Set(candidates.sort((a, b) => b.score - a.score).map(candidate => candidate.id))];
        })()
        """;
}
