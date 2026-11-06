using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace DIPlayer;

public partial class MainWindow
{
    bool clipMode, openingClip;
    bool clipChangingMusicVariant;
    bool clipLyricsVisible = true;
    int clipRevision;
    string clipLyricsSignature = "";
    string? clipOriginalLyricsTrack, clipOriginalMedia, clipNativeMedia;
    const string ClipButtonLabel = "Music  /  Video", ClipLoadingLabel = "Loading video…", ClipUnavailableLabel = "Video unavailable";

    async void OpenTrackClip(object sender, RoutedEventArgs e) => await Guard(() => ShowTrackClip());

    void SetClipButtons(string label)
    {
        ClipButton.Content = label;
        ClipCompactButton.Content = label == ClipLoadingLabel ? "…" : "Video";
        string tip = label == ClipUnavailableLabel ? "A video is not available for this track" : "Show the current YouTube video";
        ClipButton.ToolTip = ClipCompactButton.ToolTip = tip;
    }
    async void RestoreClipButtonsLater(int revision)
    {
        await Task.Delay(2600);
        if (revision == clipRevision && !openingClip && !clipMode) SetClipButtons(ClipButtonLabel);
    }

    async Task ShowTrackClip(bool publicOnly = false, Action<CoreWebView2>? configurePublicBrowser = null)
    {
        await publicClipReturn;
        if (openingClip || clipMode || !youtubeLyricsOpen || !browserMode || youtubeState?.IsAd == true ||
            musicBrowser?.CoreWebView2 is not { } core) return;

        openingClip = true;
        int revision = ++clipRevision;
        bool shown = false;
        clipOriginalMedia = youtubeState?.VideoId;
        clipOriginalLyricsTrack = clipOriginalMedia is { Length: > 0 } id &&
            youtubeLyricsTrack.StartsWith(id + "|", StringComparison.Ordinal) ? youtubeLyricsTrack : CurrentLyricsTrack();
        clipNativeMedia = null;
        SetClipButtons(ClipLoadingLabel);
        try
        {
            string? selectedVideo = SelectedClipId(youtubeState);
            string prepare = PrepareClipScript.Replace("SELECTED_VIDEO_ID", JsonSerializer.Serialize(selectedVideo));
            string preparation = publicOnly ? "false" : await core.ExecuteScriptAsync(prepare);
            if (preparation == "null")
            {
                await core.ExecuteScriptAsync("document.querySelector('ytmusic-player-bar .expand-button')?.click()");
                for (int attempt = 0; attempt < 25 && preparation == "null"; attempt++)
                {
                    await Task.Delay(100);
                    if (revision != clipRevision || musicBrowser?.CoreWebView2 != core) return;
                    preparation = await core.ExecuteScriptAsync(prepare);
                }
            }
            bool switching = preparation == "true";
            bool ready = preparation == "\"ready\"";
            if (switching)
            {
                clipChangingMusicVariant = true;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    await Task.Delay(100);
                    if (musicBrowser?.CoreWebView2 != core) return;
                    if (revision != clipRevision)
                    {
                        await core.ExecuteScriptAsync("delete window.__diplayerClipTransition");
                        return;
                    }
                    if (await core.ExecuteScriptAsync(FinishClipSwitchScript) == "true") { ready = true; break; }
                    if (await core.ExecuteScriptAsync(MusicClipPromptScript) == "true") break;
                }
                await core.ExecuteScriptAsync("delete window.__diplayerClipTransition");
            }
            if (revision != clipRevision || !youtubeLyricsOpen || !browserMode) return;
            CoreWebView2? display = ready && await core.ExecuteScriptAsync(OpenClipScript) == "true" ? core : null;
            if (display == core && switching)
                clipNativeMedia = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("window.__diplayerClip?.mediaId()"));
            clipChangingMusicVariant = false;
            display ??= await OpenPublicClip(core, revision, configurePublicBrowser);
            if (revision != clipRevision || !youtubeLyricsOpen || !browserMode || musicBrowser?.CoreWebView2 != core)
            {
                if (display == core && musicBrowser?.CoreWebView2 == core) await core.ExecuteScriptAsync(CloseClipScript);
                return;
            }
            if (display == null)
            {
                SetClipButtons(ClipUnavailableLabel);
                RestoreClipButtonsLater(revision);
                return;
            }

            clipMode = true;
            clipLyricsSignature = "";
            // Lyrics and artwork reach the page before it becomes visible, so the
            // first painted frame already matches the track screen underneath.
            await InstallClipChrome(display);
            await UpdateClipLyrics();
            if (revision != clipRevision || !clipMode || musicBrowser?.CoreWebView2 != core) return;

            // The page draws the chrome over the same native video surface.
            BrowserPanel.IsHitTestVisible = true;
            Grid.SetColumn(BrowserPanel, 0);
            Grid.SetColumnSpan(BrowserPanel, 2);
            BrowserPanel.Margin = new Thickness(0);
            Panel.SetZIndex(BrowserPanel, 70);
            youtubeClippedHandle = IntPtr.Zero;
            UpdateLayout();
            if (display == core)
            {
                if (musicBrowser.Handle != IntPtr.Zero) SetWindowRgn(musicBrowser.Handle, IntPtr.Zero, true);
                musicBrowser.Focus();
            }
            else RevealPublicClip();
            shown = true;
            SetClipButtons(ClipButtonLabel);
            // Covered WPF animations would only burn frames behind the video.
            SyncTrackBackdrop();
            await display.ExecuteScriptAsync("window.__diplayerClip?.reveal();window.__diplayerClip?.requestQuality()");
        }
        finally
        {
            clipChangingMusicVariant = false;
            openingClip = false;
            if (!shown && !clipMode) clipOriginalLyricsTrack = clipOriginalMedia = clipNativeMedia = null;
            if (!shown && ClipButton.Content as string == ClipLoadingLabel) SetClipButtons(ClipButtonLabel);
        }
    }

    void CloseTrackClip()
    {
        clipRevision++;
        bool publicClip = ClosePublicClip();
        if (!clipMode) return;
        clipMode = false;
        clipLyricsSignature = "";
        BrowserPanel.IsHitTestVisible = false;
        Grid.SetColumn(BrowserPanel, 1);
        Grid.SetColumnSpan(BrowserPanel, 1);
        BrowserPanel.Margin = new Thickness(8, 42, 8, 8);
        Panel.SetZIndex(BrowserPanel, 65);
        youtubeClippedHandle = IntPtr.Zero;
        ClipYouTubeHost();
        SetClipButtons(ClipButtonLabel);
        if (!publicClip && musicBrowser?.CoreWebView2 is { } core)
            _ = Guard(async () => { await core.ExecuteScriptAsync(CloseClipScript); });
        SyncTrackBackdrop();
        if (youtubeLyricsOpen)
        {
            ScrollToLyric(youtubeActiveLyric, true);
            // Return keyboard focus to the button that opened the video; otherwise
            // the hidden page keeps receiving Space and arrow keys.
            Dispatcher.BeginInvoke(() =>
            {
                if (clipMode || !youtubeLyricsOpen) return;
                if (ClipButton.IsVisible) ClipButton.Focus();
                else if (ClipCompactButton.IsVisible) ClipCompactButton.Focus();
            }, DispatcherPriority.Input);
        }
    }

    async Task UpdateClipLyrics()
    {
        if (!clipMode || (publicClipBrowser?.CoreWebView2 ?? musicBrowser?.CoreWebView2) is not { } core) return;
        string track = CurrentLyricsTrack();
        bool publicClip = publicClipBrowser != null;
        LyricsChoice? alternative = youtubeLyricsTrack == track && youtubeTimedLyrics.Length == 0
            ? lyricsChoices.FirstOrDefault(choice => choice.Lines.Length > 0 && choice.Lines.All(line => line.Start.HasValue)) : null;
        YouTubeLyricsLine[] source = youtubeLyricsTrack == track ? youtubeRenderedLyrics : [];
        if (alternative != null) source = alternative.Lines;
        bool timed = source.Length > 0 && source.All(line => line.Start.HasValue);
        var active = timed || publicClip || source.Length == 0 ? -1 : youtubeActiveLyric;
        double clipOffset = GetLyricsOffset("clip:" + track);
        var state = EffectiveLyricsState();
        string signature = string.Join('\n', track, youtubeLyricsSignature, alternative?.Id ?? "", active.ToString(CultureInfo.InvariantCulture),
            clipOffset.ToString("R", CultureInfo.InvariantCulture), lyricsOffset.ToString("R", CultureInfo.InvariantCulture),
            state?.Artwork ?? "", settings.MotionEnabled ? "motion" : "still", clipLyricsVisible ? "visible" : "hidden");
        if (signature == clipLyricsSignature) return;
        var lines = source.Select(line => new { text = line.Text, start = line.Start }).ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            lines, active, timed, offset = lyricsOffset + clipOffset, clipOffset, showLyrics = clipLyricsVisible,
            motion = settings.MotionEnabled, artwork = state?.Artwork ?? ""
        });
        if (await core.ExecuteScriptAsync("window.__diplayerClip?.update(" + payload + ")") == "true")
            clipLyricsSignature = signature;
    }

    void HandleClipMessage(string message)
    {
        if (message == "diplayer-close-clip") { CloseTrackClip(); return; }
        if (!clipMode) return;
        var click = new RoutedEventArgs();
        switch (message)
        {
            case "diplayer-clip-lyrics-on": clipLyricsVisible = true; return;
            case "diplayer-clip-lyrics-off": clipLyricsVisible = false; return;
            case "diplayer-clip-home": HomeView(this, click); return;
            case "diplayer-clip-library": YouTubeLibraryClick(this, click); return;
            case "diplayer-clip-likes": YouTubeLikesClick(this, click); return;
            case "diplayer-clip-settings": OpenPlayerSettings(this, click); return;
            case "diplayer-clip-minimize": MinimizeClick(this, click); return;
            case "diplayer-clip-maximize": MaximizeClick(this, click); return;
            case "diplayer-clip-close-window": CloseClick(this, click); return;
        }
        if (message is "diplayer-clip-lyrics-earlier" or "diplayer-clip-lyrics-later" or "diplayer-clip-lyrics-reset")
        {
            string key = "clip:" + CurrentLyricsTrack();
            double offset = message == "diplayer-clip-lyrics-reset" ? 0 :
                Math.Clamp(GetLyricsOffset(key) + (message == "diplayer-clip-lyrics-earlier" ? .5 : -.5), -30, 30);
            SaveLyricsOffset(key, offset);
            _ = Guard(UpdateClipLyrics);
            return;
        }
        string? action = message switch { "diplayer-clip-next" => "next", "diplayer-clip-previous" => "previous", _ => null };
        if (message == "diplayer-clip-ended") action = "next";
        if (action != null) Dispatcher.BeginInvoke(async () => await Guard(() => MoveClipTrack(action)));
        else if (message == "diplayer-clip-music-fallback" && publicClipBrowser == null)
            Dispatcher.BeginInvoke(async () => await Guard(async () => { if (!clipMode || publicClipBrowser != null) return; CloseTrackClip(); await ShowTrackClip(true); }));
    }

    const string MusicClipProbe = """
          const player = document.getElementById('movie_player');
          const mediaId = player?.getVideoData?.()?.video_id;
          const response = player?.getPlayerResponse?.();
          const type = response?.videoDetails?.musicVideoType || response?.microformat?.microformatDataRenderer?.musicVideoType ||
            player?.getVideoData?.()?.musicVideoType || '';
          const video = document.querySelector('ytmusic-player video') || document.querySelector('#movie_player video') || document.querySelector('video');
          const toggle = document.querySelector('ytmusic-av-toggle');
          const selected = element => element && (element.getAttribute('aria-selected') === 'true' ||
            element.getAttribute('aria-pressed') === 'true' || element.hasAttribute('selected') ||
            element.classList.contains('iron-selected'));
          const song = toggle?.querySelector('button.song-button, #song, #audio');
          const option = toggle?.querySelector('button.video-button, #video, #av-toggle-video, [aria-label="Video"], [aria-label="Видео"], [aria-label="Відео"]');
          const songMode = type === 'MUSIC_VIDEO_TYPE_ATV' || toggle?.videoMode === false || selected(song);
          const picture = video?.videoWidth > 0 && video.readyState >= 2;
        """;

    const string PrepareClipScript = """
        (() => {
          if (location.hostname !== 'music.youtube.com') return false;
        """ + MusicClipProbe + """

          if (!video) return null;
          if (document.querySelector('.ad-showing,.ad-interrupting')) return false;
          const selectedVideo = SELECTED_VIDEO_ID;
          if (selectedVideo && (mediaId !== selectedVideo || songMode)) return false;
          if (picture && !songMode) return 'ready';
          if (selectedVideo) return null;
          if (!toggle) return type === 'MUSIC_VIDEO_TYPE_ATV' ? false : null;
          if (!option || selected(option) || option.disabled || option.getAttribute('aria-disabled') === 'true') return false;
          window.__diplayerClipTransition = { video, source: video.currentSrc, width: video.videoWidth,
            position: video.currentTime, paused: video.paused, mediaId };
          option.click();
          return true;
        })()
        """;

    const string FinishClipSwitchScript = """
        (() => {
        """ + MusicClipProbe + """

          const pending = window.__diplayerClipTransition;
          if (!pending || !picture || songMode || document.querySelector('.ad-showing,.ad-interrupting')) return false;
          const replaced = video !== pending.video || video.currentSrc !== pending.source || video.videoWidth !== pending.width || mediaId !== pending.mediaId;
          if (!replaced && toggle?.videoMode !== true && !selected(option)) return false;
          if (Number.isFinite(video.duration) && Number.isFinite(pending.position))
            video.currentTime = Math.min(pending.position, video.duration);
          if (pending.paused) video.pause();
          else if (video.paused) video.play().catch(() => {});
          return true;
        })()
        """;

    const string MusicClipPromptScript = """
        (() => {
          const prompt = Array.from(document.querySelectorAll('tp-yt-paper-dialog[opened]'))
            .find(dialog => /premium/i.test(dialog.textContent));
          prompt?.close?.();
          return !!prompt;
        })()
        """;

    const string CloseClipScript = "window.__diplayerClip?.close()";
}
