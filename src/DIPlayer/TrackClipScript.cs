namespace DIPlayer;

public partial class MainWindow
{
    // The clip view lives inside the page playing the video. Everything here runs on the
    // page's own animation frames (requestAnimationFrame) and reads video.currentTime
    // directly, so lyric timing and motion never wait for a host round-trip.
    const string OpenClipScript = """
        (() => {
          const publicClip = location.hostname === 'www.youtube.com' && location.pathname === '/watch';
          if (!publicClip && location.hostname !== 'music.youtube.com') return false;
          window.__diplayerClip?.close();
          const findVideo = () => document.querySelector('ytmusic-player video') || document.querySelector('#movie_player video') || document.querySelector('video');
          let video = findVideo();
          if (!video || !video.videoWidth || !Number.isFinite(video.duration) || document.querySelector('.ad-showing,.ad-interrupting')) return false;
          const moviePlayer = () => document.getElementById('movie_player');
          const ease = 'cubic-bezier(.22,1,.36,1)';

          const style = document.createElement('style');
          style.id = 'diplayer-clip-style';
          style.textContent = `
        body[data-diplayer-clip] { background:#000 !important; overflow:hidden !important }
        body[data-diplayer-clip] * { visibility:hidden !important }
        body[data-diplayer-clip] *:has(video[data-diplayer-clip-video]) {
          overflow:visible !important; transform:none !important; filter:none !important;
          clip-path:none !important; contain:none !important; opacity:1 !important;
          perspective:none !important; will-change:auto !important; translate:none !important; scale:none !important; rotate:none !important
        }
        body[data-diplayer-clip] video[data-diplayer-clip-video] {
          visibility:visible !important; position:fixed !important; inset:0 !important;
          width:100vw !important; height:100vh !important; max-width:none !important; max-height:none !important;
          object-fit:cover !important; object-position:center center !important; translate:none !important; scale:none !important;
          transform:none !important; margin:0 !important; z-index:100000 !important;
          background:#000 !important; pointer-events:auto !important
        }
        body[data-diplayer-clip][data-diplayer-idle] video[data-diplayer-clip-video] { cursor:none !important }
        body[data-diplayer-clip] #diplayer-clip-ui, body[data-diplayer-clip] #diplayer-clip-ui * { visibility:visible !important }
        #diplayer-clip-ui { position:fixed; inset:0; z-index:2147483647; pointer-events:none; color:#fff; user-select:none;
          font-family:'Segoe UI Variable Display','Segoe UI',system-ui,sans-serif; -webkit-font-smoothing:antialiased; text-rendering:optimizeLegibility }
        #diplayer-clip-ui button { font:inherit; color:inherit; border:0; margin:0; padding:0; background:none; cursor:pointer; pointer-events:auto; outline:none }
        #diplayer-clip-ui button:focus-visible { box-shadow:0 0 0 1px rgba(255,255,255,.75) }
        #diplayer-clip-ui .dc-caption { position:absolute; inset:0 0 auto; height:42px; display:flex; align-items:center;
          padding:0 12px 0 79px; pointer-events:auto; app-region:drag }
        #diplayer-clip-ui .dc-brand { height:22px; margin-right:auto; object-fit:contain; object-position:left; filter:drop-shadow(0 1px 4px #000) }
        #diplayer-clip-ui .dc-side { position:absolute; top:0; bottom:0; left:0; width:64px; display:flex; flex-direction:column;
          align-items:center; gap:6px; padding:2px 0 12px }
        #diplayer-clip-ui .dc-caption button, #diplayer-clip-ui .dc-side button { width:36px; height:26px; display:grid; place-items:center;
          border-radius:6px; app-region:no-drag; filter:drop-shadow(0 1px 2px #000) }
        #diplayer-clip-ui .dc-side button { width:46px; height:44px; flex:none }
        #diplayer-clip-ui .dc-side .dc-mascot { width:62px; height:68px; margin-bottom:4px }
        #diplayer-clip-ui .dc-mascot img { width:46px; height:58px }
        #diplayer-clip-ui .dc-glyph { display:block; width:18px; height:18px }
        #diplayer-clip-ui .dc-caption .dc-glyph { width:14px; height:14px }
        #diplayer-clip-ui .dc-side .dc-glyph { width:24px; height:24px }
        #diplayer-clip-ui .dc-back .dc-glyph { width:20px; height:20px }
        #diplayer-clip-ui .dc-side button:last-child { margin-top:auto }
        #diplayer-clip-ui .dc-caption button:hover, #diplayer-clip-ui .dc-side button:hover,
        #diplayer-clip-ui .dc-side button[aria-current=page] { background:rgba(24,24,24,.65) }
        #diplayer-clip-ui .dc-layer { position:absolute; inset:0 }
        #diplayer-clip-ui .dc-art { position:absolute; inset:-14%; background:#151515 center/cover no-repeat; filter:blur(60px) saturate(1.1); opacity:.5; transform:scale(1.12) }
        #diplayer-clip-ui .dc-curtain { background:#151515; opacity:1; transition:opacity 480ms ${ease} }
        #diplayer-clip-ui .dc-curtain::after { content:''; position:absolute; inset:0; background:rgba(0,0,0,.42) }
        #diplayer-clip-ui[data-state=shown] .dc-curtain { opacity:0 }
        #diplayer-clip-ui[data-state=leaving] .dc-curtain { opacity:1; transition:opacity 190ms cubic-bezier(.55,0,1,.45) }
        #diplayer-clip-ui .dc-fallback { opacity:0; transition:opacity 520ms ease; background:#101010; display:flex; flex-direction:column; align-items:center; justify-content:center; overflow:hidden }
        #diplayer-clip-ui[data-picture=none] .dc-fallback { opacity:1 }
        #diplayer-clip-ui[data-lyrics=on] .dc-fallback { padding-right:54% }
        #diplayer-clip-ui .dc-cover { position:relative; width:min(40vh,320px); aspect-ratio:1; border-radius:10px; background:#252525 center/cover no-repeat; box-shadow:0 24px 70px rgba(0,0,0,.55) }
        #diplayer-clip-ui .dc-note { position:relative; margin-top:18px; font-size:13px; font-weight:600; color:rgba(255,255,255,.72) }
        #diplayer-clip-ui .dc-scrim { background:linear-gradient(90deg, transparent 32%, rgba(0,0,0,.34) 56%, rgba(0,0,0,.62)); transition:opacity 420ms ease }
        #diplayer-clip-ui[data-lyrics=off] .dc-scrim, #diplayer-clip-ui[data-lyrics=empty] .dc-scrim { opacity:0 }
        #diplayer-clip-ui .dc-shade { opacity:0; transition:opacity 260ms ease;
          background:linear-gradient(180deg, rgba(0,0,0,.5), transparent 110px), linear-gradient(0deg, rgba(0,0,0,.6), transparent 150px) }
        #diplayer-clip-ui[data-awake] .dc-shade { opacity:1 }
        #diplayer-clip-ui .dc-back { position:absolute; left:88px; top:50px; width:32px; height:32px; border-radius:6px; display:grid; place-items:center;
          filter:drop-shadow(0 1px 2px #000); transition:opacity 140ms ease, transform 140ms ease }
        #diplayer-clip-ui .dc-back:active { transform:scale(.92) }
        #diplayer-clip-ui .dc-top { position:absolute; right:24px; top:51px; display:flex; gap:8px; align-items:center; opacity:0; transform:translateY(-4px);
          transition:opacity 220ms ease, transform 300ms ${ease} }
        #diplayer-clip-ui[data-awake] .dc-top { opacity:1; transform:none }
        #diplayer-clip-ui .dc-chip { height:30px; padding:0 12px; font-size:11px; font-weight:700; display:flex; align-items:center;
          text-shadow:0 1px 3px #000; transition:opacity 140ms ease }
        #diplayer-clip-ui .dc-back:hover, #diplayer-clip-ui button.dc-chip:hover { opacity:.75 }
        #diplayer-clip-ui .dc-chip[aria-pressed=false] { opacity:.55 }
        #diplayer-clip-ui .dc-quality:empty { display:none }
        #diplayer-clip-ui .dc-lyrics-toggle:disabled { opacity:.55; cursor:default }
        #diplayer-clip-ui .dc-timing { display:flex; gap:2px; align-items:center }
        #diplayer-clip-ui:not([data-seek-lyrics]) .dc-timing, #diplayer-clip-ui:not([data-lyrics=on]) .dc-timing { display:none }
        #diplayer-clip-ui .dc-timing button { padding:0 8px }
        #diplayer-clip-ui .dc-viewport { position:absolute; left:46%; right:28px; top:94px; bottom:92px; overflow:hidden;
          -webkit-mask-image:linear-gradient(180deg, transparent 0, #000 15%, #000 80%, transparent 100%);
          mask-image:linear-gradient(180deg, transparent 0, #000 15%, #000 80%, transparent 100%);
          transition:opacity 420ms ease, transform 560ms ${ease} }
        #diplayer-clip-ui[data-state=entering] .dc-viewport, #diplayer-clip-ui[data-lyrics=off] .dc-viewport { opacity:0; transform:translateX(32px) }
        #diplayer-clip-ui[data-state=leaving] .dc-viewport { opacity:0; transition-duration:170ms }
        #diplayer-clip-ui .dc-lyrics { position:absolute; left:0; right:0; top:50%; font-weight:700; font-size:32px; line-height:1.3;
          white-space:pre-wrap; overflow-wrap:anywhere; text-shadow:0 2px 12px rgba(0,0,0,.55), 0 1px 3px rgba(0,0,0,.55); will-change:transform }
        #diplayer-clip-ui[data-timing=on] .dc-lyrics { top:46% }
        #diplayer-clip-ui .dc-line { margin:0 0 30px; opacity:.36;
          transition:opacity 460ms ${ease} }
        #diplayer-clip-ui .dc-line.dc-on, #diplayer-clip-ui[data-timing=off] .dc-line { opacity:1; transform:none }
        #diplayer-clip-ui[data-lyrics=on] .dc-line[data-start] { pointer-events:auto; cursor:pointer }
        #diplayer-clip-ui .dc-line[data-start]:not(.dc-on):hover { opacity:.66 }
        #diplayer-clip-ui .dc-bar { position:absolute; left:64px; right:24px; bottom:16px; display:flex; align-items:center; gap:12px; opacity:0; transform:translateY(8px);
          transition:opacity 220ms ease, transform 320ms ${ease} }
        #diplayer-clip-ui[data-awake] .dc-bar { opacity:1; transform:none }
        #diplayer-clip-ui:not([data-awake]) .dc-bar *, #diplayer-clip-ui:not([data-awake]) .dc-top * { pointer-events:none !important }
        #diplayer-clip-ui .dc-bar button { width:34px; height:34px; border-radius:17px; display:grid; place-items:center; flex:none;
          transition:background-color 140ms ease, transform 140ms ease }
        #diplayer-clip-ui .dc-bar button:hover { background:rgba(255,255,255,.14) }
        #diplayer-clip-ui .dc-bar button:active { transform:scale(.9) }
        #diplayer-clip-ui .dc-bar .dc-play { width:40px; height:40px; border-radius:20px; background:rgba(0,0,0,.55); color:#fff }
        #diplayer-clip-ui .dc-bar .dc-play:hover { background:rgba(0,0,0,.74) }
        #diplayer-clip-ui .dc-time { font-size:11px; font-weight:600; font-variant-numeric:tabular-nums; color:rgba(255,255,255,.82); min-width:36px; text-align:center; flex:none }
        #diplayer-clip-ui .dc-track { flex:1; height:24px; display:flex; align-items:center; pointer-events:auto; cursor:pointer }
        #diplayer-clip-ui .dc-rail { position:relative; flex:1; height:4px; border-radius:2px; background:rgba(255,255,255,.24); overflow:hidden; transition:height 140ms ease }
        #diplayer-clip-ui .dc-track:hover .dc-rail { height:6px; border-radius:3px }
        #diplayer-clip-ui .dc-fill { position:absolute; inset:0; background:#fff; transform-origin:0 50%; transform:scaleX(0) }
        #diplayer-clip-ui[data-compact] .dc-viewport { left:64px; right:24px; top:auto; height:40%; bottom:80px }
        #diplayer-clip-ui[data-compact] .dc-lyrics { font-size:24px }
        #diplayer-clip-ui[data-compact] .dc-line { margin-bottom:20px }
        #diplayer-clip-ui[data-compact] .dc-scrim { background:linear-gradient(0deg, rgba(0,0,0,.72), rgba(0,0,0,.28) 50%, transparent 70%) }
        #diplayer-clip-ui[data-compact][data-lyrics=on] .dc-fallback { padding-right:0; padding-bottom:42% }
        #diplayer-clip-ui[data-wide] .dc-lyrics { font-size:36px }
        #diplayer-clip-ui[data-motion=off], #diplayer-clip-ui[data-motion=off] * { transition:none !important }
        `;
          document.head.append(style);

          const make = (tag, className, parent) => { const node = document.createElement(tag); if (className) node.className = className; parent?.append(node); return node; };
          let chromeAssets = {};
          const applyAsset = (image, asset) => {
            image.dataset.asset = asset;
            if (chromeAssets[asset] && image.src !== chromeAssets[asset]) image.src = chromeAssets[asset];
          };
          const chromeImage = (parent, asset, className = 'dc-glyph', label = '') => {
            const image = make('img', className, parent);
            image.alt = label; image.draggable = false;
            applyAsset(image, asset);
            return image;
          };
          const ui = make('div');
          ui.id = 'diplayer-clip-ui';
          ui.dataset.state = 'entering'; ui.dataset.lyrics = 'empty'; ui.dataset.timing = 'off'; ui.dataset.picture = 'video'; ui.dataset.motion = 'on';
          const post = message => { try { window.chrome.webview.postMessage(message); } catch {} };
          const actionButton = (parent, label, action, asset = action, className = '') => {
            const button = make('button', className, parent);
            button.title = label; button.setAttribute('aria-label', label);
            button.dataset.action = action;
            chromeImage(button, asset, asset === 'mascot' ? '' : 'dc-glyph');
            button.addEventListener('click', () => post('diplayer-clip-' + action));
            return button;
          };
          const fallback = make('div', 'dc-layer dc-fallback', ui);
          const fallbackArt = make('div', 'dc-art', fallback);
          const cover = make('div', 'dc-cover', fallback);
          const note = make('div', 'dc-note', fallback); note.textContent = 'No video for this track';
          make('div', 'dc-layer dc-scrim', ui);
          make('div', 'dc-layer dc-shade', ui);
          const viewport = make('div', 'dc-viewport', ui);
          const lyrics = make('div', 'dc-lyrics', viewport);
          const bar = make('div', 'dc-bar', ui);
          const previous = make('button', '', bar); chromeImage(previous, 'previous'); previous.setAttribute('aria-label', 'Previous'); previous.title = 'Previous';
          const play = make('button', 'dc-play', bar); play.setAttribute('aria-label', 'Play / pause');
          const playImage = chromeImage(play, 'play');
          const next = make('button', '', bar); chromeImage(next, 'next'); next.setAttribute('aria-label', 'Next'); next.title = 'Next';
          const elapsed = make('span', 'dc-time', bar);
          const track = make('div', 'dc-track', bar);
          const rail = make('div', 'dc-rail', track);
          const fill = make('div', 'dc-fill', rail);
          const total = make('span', 'dc-time', bar);
          const top = make('div', 'dc-top', ui);
          const quality = make('span', 'dc-chip dc-quality', top);
          const timing = make('div', 'dc-timing', top);
          const timingButton = (label, title, action) => {
            const button = make('button', 'dc-chip', timing);
            button.textContent = label; button.title = title; button.setAttribute('aria-label', title);
            button.addEventListener('click', () => post('diplayer-clip-lyrics-' + action));
            return button;
          };
          timingButton('−', 'Show lyrics later by 0.5 seconds', 'later');
          const timingLabel = timingButton('0.0s', 'Reset clip lyric timing', 'reset');
          timingButton('+', 'Show lyrics earlier by 0.5 seconds', 'earlier');
          const lyricsToggle = make('button', 'dc-chip dc-lyrics-toggle', top); lyricsToggle.textContent = 'Lyrics'; lyricsToggle.title = 'Show or hide lyrics (L)';
          lyricsToggle.disabled = true;
          const curtain = make('div', 'dc-layer dc-curtain', ui);
          const curtainArt = make('div', 'dc-art', curtain);
          const back = make('button', 'dc-back', ui);
          chromeImage(back, 'back');
          back.setAttribute('aria-label', 'Back to music'); back.title = 'Back to music (Esc)';
          const caption = make('div', 'dc-caption', ui);
          const wordmark = chromeImage(caption, 'wordmark', 'dc-brand', 'DIPlayer');
          actionButton(caption, 'Minimize', 'minimize');
          const maximizeImage = actionButton(caption, 'Maximize / restore', 'maximize').firstElementChild;
          actionButton(caption, 'Close to tray', 'close-window', 'close');
          const side = make('nav', 'dc-side', ui);
          actionButton(side, 'Home', 'home', 'mascot', 'dc-mascot');
          actionButton(side, 'Home', 'home');
          actionButton(side, 'YouTube Music library', 'library');
          actionButton(side, 'Liked music', 'likes');
          actionButton(side, 'Settings', 'settings');
          document.body.append(ui);
          document.body.setAttribute('data-diplayer-clip', '');

          let motion = true, offset = 0, timed = false, starts = [], hostActive = -1, activeLine = -2, lit = [], signature = null, artwork = '';
          let position = NaN, velocity = 0, target = 0, manualUntil = 0, lastFrame = 0, frameId = 0, lastSecond = -1, adShowing = false;
          let closing = false, idleTimer = 0, revealTimer = 0, seeking = null, pictureLostAt = 0;
          let lyricsVisible = true;
          lyricsToggle.setAttribute('aria-pressed', String(lyricsVisible));
          const clock = seconds => {
            if (!Number.isFinite(seconds) || seconds < 0) return '0:00';
            seconds = Math.floor(seconds);
            const hours = Math.floor(seconds / 3600), minutes = Math.floor(seconds / 60) % 60, rest = String(seconds % 60).padStart(2, '0');
            return hours ? hours + ':' + String(minutes).padStart(2, '0') + ':' + rest : minutes + ':' + rest;
          };
          const syncPlay = () => { applyAsset(playImage, video.paused ? 'play' : 'pause'); if (video.paused) wake(); };

          // ---- Lyrics: active line straight from video time, critically damped glide.
          const activeIndex = () => {
            if (adShowing) return -1;
            if (!timed) return hostActive;
            const time = video.currentTime + offset;
            let low = 0, high = starts.length - 1, found = -1;
            while (low <= high) { const middle = (low + high) >> 1; if (starts[middle] <= time) { found = middle; low = middle + 1; } else high = middle - 1; }
            return found;
          };
          const place = immediate => {
            const lines = lyrics.children;
            if (!lines.length) { target = 0; }
            else if (lines[activeLine]) target = -(lines[activeLine].offsetTop + lines[activeLine].offsetHeight / 2);
            else if (ui.dataset.timing === 'on') target = viewport.clientHeight * .2 - lines[0].offsetHeight / 2;
            else target = -viewport.clientHeight * .33;
            const reach = Math.max(220, viewport.clientHeight * .55);
            if (immediate || !motion || Number.isNaN(position)) { position = target; velocity = 0; }
            else if (Math.abs(target - position) > reach * 2.5) { position = target + Math.sign(position - target) * reach; velocity = 0; }
            lyrics.style.transform = 'translate3d(0,' + position.toFixed(2) + 'px,0)';
          };
          const setActive = index => {
            if (index === activeLine) return;
            for (const node of lit) node.classList.remove('dc-on');
            lit = []; activeLine = index;
            const lines = lyrics.children;
            if (lines[index]) {
              const start = starts[index];
              if (timed) for (let i = index; i >= 0 && starts[i] === start; i--) { lines[i].classList.add('dc-on'); lit.push(lines[i]); }
              else { lines[index].classList.add('dc-on'); lit.push(lines[index]); }
            }
            ui.dataset.timing = timed || hostActive >= 0 ? 'on' : 'off';
            if (!manualUntil && (!lines[index] || lines[index].textContent.trim() || index < 0)) place(false);
          };
          const render = (incoming, isTimed) => {
            timed = isTimed && incoming.length > 0 && incoming.every(line => line.start !== null);
            starts = timed ? incoming.map(line => line.start) : [];
            lit = []; activeLine = -2; manualUntil = 0; position = NaN;
            lyrics.replaceChildren(...incoming.map(line => {
              const node = document.createElement('div');
              node.className = 'dc-line';
              node.textContent = line.text;
              if (timed && line.text.trim()) node.dataset.start = String(line.start);
              return node;
            }));
            const hasLyrics = incoming.some(line => line.text.trim());
            ui.dataset.lyrics = hasLyrics ? (lyricsVisible ? 'on' : 'off') : 'empty';
            lyricsToggle.disabled = !hasLyrics;
            lyricsToggle.title = hasLyrics ? 'Show or hide lyrics (L)' : 'Lyrics unavailable';
            ui.toggleAttribute('data-seek-lyrics', timed);
            if (motion && incoming.length) lyrics.animate?.([{ opacity: 0 }, { opacity: 1 }], { duration: 380, easing: 'ease-out' });
            setActive(activeIndex());
            place(true);
          };
          const setLyricsVisible = visible => {
            lyricsVisible = visible;
            lyricsToggle.setAttribute('aria-pressed', String(visible));
            if (ui.dataset.lyrics !== 'empty') ui.dataset.lyrics = visible ? 'on' : 'off';
          };
          const toggleLyrics = () => {
            if (lyricsToggle.disabled) return;
            setLyricsVisible(!lyricsVisible);
            post(lyricsVisible ? 'diplayer-clip-lyrics-on' : 'diplayer-clip-lyrics-off');
            wake();
          };

          // ---- Per-frame work: lyric glide and progress. Writes happen only on change.
          const frame = now => {
            frameId = requestAnimationFrame(frame);
            const step = Math.min(.05, lastFrame ? (now - lastFrame) / 1000 : 0);
            lastFrame = now;
            if (manualUntil && now > manualUntil) { manualUntil = 0; place(false); }
            setActive(activeIndex());
            if (position !== target || velocity) {
              if (!motion || Number.isNaN(position)) { position = target; velocity = 0; }
              else {
                const stiffness = 9, distance = position - target, change = (velocity + stiffness * distance) * step, decay = Math.exp(-stiffness * step);
                velocity = (velocity - stiffness * change) * decay;
                const rest = (distance + change) * decay;
                position = target + rest;
                if (Math.abs(rest) < .25 && Math.abs(velocity) < 5) { position = target; velocity = 0; }
              }
              lyrics.style.transform = 'translate3d(0,' + position.toFixed(2) + 'px,0)';
            }
            if (ui.hasAttribute('data-awake') && !seeking) {
              const duration = video.duration;
              if (Number.isFinite(duration) && duration > 0) fill.style.transform = 'scaleX(' + Math.min(1, video.currentTime / duration).toFixed(4) + ')';
              const second = Math.floor(video.currentTime);
              if (second !== lastSecond) { lastSecond = second; elapsed.textContent = clock(second); total.textContent = clock(duration); }
            }
          };

          // ---- Controls that fade away while watching.
          const wake = () => {
            if (closing) return;
            ui.setAttribute('data-awake', '');
            document.body.removeAttribute('data-diplayer-idle');
            clearTimeout(idleTimer);
            idleTimer = setTimeout(() => {
              if (video.paused || seeking || bar.matches(':hover') || top.matches(':hover') || back.matches(':hover')) { wake(); return; }
              ui.removeAttribute('data-awake');
              document.body.setAttribute('data-diplayer-idle', '');
            }, 2600);
          };
          const togglePlayback = event => {
            event?.preventDefault(); event?.stopImmediatePropagation();
            if (video.paused) video.play().catch(() => {}); else video.pause();
            wake();
          };
          const seekTo = seconds => {
            if (!Number.isFinite(video.duration)) return;
            video.currentTime = Math.min(Math.max(0, seconds), Math.max(0, video.duration - .25));
            manualUntil = 0; lastSecond = -1;
          };
          const requestClose = () => {
            if (closing || ui.dataset.state === 'leaving') return;
            ui.dataset.state = 'leaving';
            ui.removeAttribute('data-awake');
            if (motion) setTimeout(() => post('diplayer-close-clip'), 190); else post('diplayer-close-clip');
          };
          const seekFraction = event => {
            const bounds = rail.getBoundingClientRect();
            return Math.min(1, Math.max(0, (event.clientX - bounds.left) / Math.max(1, bounds.width)));
          };
          track.addEventListener('pointerdown', event => {
            if (!Number.isFinite(video.duration)) return;
            event.preventDefault(); track.setPointerCapture(event.pointerId);
            seeking = seekFraction(event); fill.style.transform = 'scaleX(' + seeking + ')'; elapsed.textContent = clock(seeking * video.duration);
          });
          track.addEventListener('pointermove', event => {
            if (seeking === null) return;
            seeking = seekFraction(event); fill.style.transform = 'scaleX(' + seeking + ')'; elapsed.textContent = clock(seeking * video.duration);
          });
          const finishSeek = event => {
            if (seeking === null) return;
            const fraction = event.type === 'pointerup' ? seekFraction(event) : seeking;
            seeking = null; seekTo(fraction * video.duration); wake();
          };
          track.addEventListener('pointerup', finishSeek);
          track.addEventListener('pointercancel', finishSeek);
          play.addEventListener('click', togglePlayback);
          previous.addEventListener('click', () => { post('diplayer-clip-previous'); wake(); });
          next.addEventListener('click', () => { post('diplayer-clip-next'); wake(); });
          back.addEventListener('click', requestClose);
          lyricsToggle.addEventListener('click', toggleLyrics);
          lyrics.addEventListener('click', event => {
            const line = event.target.closest?.('.dc-line[data-start]');
            if (!line) return;
            event.stopPropagation();
            seekTo(Number(line.dataset.start) - offset);
          });
          const keydown = event => {
            if (event.ctrlKey || event.altKey || event.metaKey) return;
            const handled = () => { event.preventDefault(); event.stopImmediatePropagation(); };
            if (event.key === 'Escape') { handled(); requestClose(); }
            else if (event.code === 'Space' && !(event.target instanceof HTMLButtonElement)) togglePlayback(event);
            else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') { handled(); seekTo(video.currentTime + (event.key === 'ArrowLeft' ? -5 : 5)); wake(); }
            else if (event.code === 'KeyL' && !event.shiftKey) { handled(); toggleLyrics(); }
            else if (event.shiftKey && (event.code === 'KeyN' || event.code === 'KeyP')) { handled(); post(event.code === 'KeyN' ? 'diplayer-clip-next' : 'diplayer-clip-previous'); }
            else if (event.code === 'KeyF') handled();
          };
          const wheel = event => {
            if (ui.dataset.lyrics !== 'on' || !lyrics.children.length) return;
            const bounds = viewport.getBoundingClientRect();
            if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) return;
            event.preventDefault(); event.stopImmediatePropagation();
            const first = lyrics.firstElementChild, last = lyrics.lastElementChild;
            target = Math.min(-first.offsetHeight / 2, Math.max(-(last.offsetTop + last.offsetHeight / 2), target - event.deltaY));
            if (!motion) { position = target; lyrics.style.transform = 'translate3d(0,' + position.toFixed(2) + 'px,0)'; }
            manualUntil = performance.now() + 4000;
          };
          const swallow = event => { event.preventDefault(); event.stopImmediatePropagation(); };
          const resize = () => {
            ui.toggleAttribute('data-compact', innerWidth < 760);
            ui.toggleAttribute('data-wide', innerWidth >= 1500);
            place(true);
          };

          // ---- Prefer 1080p and report the decoded picture size.
          const order = ['tiny', 'small', 'medium', 'large', 'hd720', 'hd1080', 'hd1440', 'hd2160', 'highres'];
          const heights = { tiny: 144, small: 240, medium: 360, large: 480, hd720: 720, hd1080: 1080, hd1440: 1440, hd2160: 2160, highres: 4320 };
          let qualityTries = 0, storedQuality = null, qualityStored = false, menuTried = false, menuHeight = 0;
          let lastRequestedQuality = '';
          try {
            storedQuality = localStorage.getItem('yt-player-quality'); qualityStored = true;
            const now = Date.now();
            localStorage.setItem('yt-player-quality', JSON.stringify({ data: JSON.stringify({ quality: 1080, previousQuality: 1080 }), expiration: now + 2592e6, creation: now }));
          } catch {}
          const qualityMenu = player => {
            const settings = player.querySelector('.ytp-settings-button');
            if (!settings || settings.getAttribute('aria-expanded') === 'true') return;
            settings.click();
            const options = () => Array.from(player.querySelectorAll('.ytp-settings-menu .ytp-menuitem'));
            const qualityItem = options().find(item => /^(?:Quality|Качество|Якість)$/i.test(item.querySelector('.ytp-menuitem-label')?.textContent.trim() || ''));
            qualityItem?.click();
            const levels = options().map(item => {
              const label = item.querySelector('.ytp-menuitem-label')?.textContent.trim() || '';
              const match = label.match(/^(\d+)p(?:\d{2})?(?:\s*HD)?$/i);
              return { item, height: Number(match?.[1]) };
            }).filter(level => level.height <= 1080).sort((a, b) => b.height - a.height);
            if (levels.length) levels[0].item.click();
            if (settings.getAttribute('aria-expanded') === 'true') settings.click();
            return levels[0]?.height || 0;
          };
          const requestQuality = () => {
            const player = moviePlayer();
            if (!player) return false;
            if (menuHeight) return Math.min(video.videoWidth, video.videoHeight) >= menuHeight;
            let levels = [];
            try { levels = (player.getAvailableQualityLevels?.() || []).filter(level => order.includes(level)); } catch {}
            const wanted = levels.filter(level => heights[level] <= 1080).sort((a, b) => heights[b] - heights[a])[0] || 'hd1080';
            if (publicClip && !menuTried && qualityTries >= 4 && Math.min(video.videoWidth, video.videoHeight) < heights[wanted]) {
              menuTried = true;
              menuHeight = qualityMenu(player) || 0;
              if (menuHeight) return false;
            }
            if (qualityTries % 4 === 0 || wanted !== lastRequestedQuality) {
              lastRequestedQuality = wanted;
              try { player.setPlaybackQualityRange?.(wanted, wanted); } catch {}
              try { player.setPlaybackQuality?.(wanted); } catch {}
            }
            // Available levels may arrive after the first adaptive segment.
            return Math.min(video.videoWidth, video.videoHeight) >= 1080;
          };
          const showQuality = () => {
            const height = Math.min(video.videoWidth, video.videoHeight);
            const text = !adShowing && ui.dataset.picture === 'video' && height >= 144 ? height + 'p' : '';
            if (quality.textContent !== text) quality.textContent = text;
          };

          // ---- Watchdog: follows track changes, keeps video mode, restores the picture.
          const selected = element => element && (element.getAttribute('aria-selected') === 'true' || element.getAttribute('aria-pressed') === 'true' ||
            element.hasAttribute('selected') || element.classList.contains('iron-selected'));
          const videoOption = () => {
            const toggle = document.querySelector('ytmusic-av-toggle');
            if (!toggle || toggle.hidden) return null;
            const song = toggle.querySelector('button.song-button, #song, #audio');
            const option = toggle.querySelector('button.video-button, #video, #av-toggle-video, [aria-label="Video"], [aria-label="Видео"], [aria-label="Відео"]');
            if (!option || selected(option) || option.disabled || option.getAttribute('aria-disabled') === 'true') return null;
            return toggle.videoMode === false || selected(song) ? option : null;
          };
          const mediaId = () => { try { return moviePlayer()?.getVideoData?.()?.video_id || ''; } catch { return ''; } };
          let currentMedia = mediaId(), switchedFor = currentMedia, switching = null;
          let fallbackRequested = false;
          const onLoaded = () => { qualityTries = 0; menuTried = false; menuHeight = 0; lastSecond = -1; syncPlay(); };
          const ended = event => {
            if (!publicClip || event.target !== video || adShowing || video.loop) return;
            event.stopImmediatePropagation();
            post('diplayer-clip-ended');
          };
          if (publicClip) document.addEventListener('ended', ended, true);
          const bind = element => {
            video = element;
            video.setAttribute('data-diplayer-clip-video', '');
            video.addEventListener('click', togglePlayback, true);
            video.addEventListener('dblclick', swallow, true);
            video.addEventListener('play', syncPlay);
            video.addEventListener('pause', syncPlay);
            video.addEventListener('loadedmetadata', onLoaded);
          };
          const unbind = () => {
            video.removeAttribute('data-diplayer-clip-video');
            video.removeEventListener('click', togglePlayback, true);
            video.removeEventListener('dblclick', swallow, true);
            video.removeEventListener('play', syncPlay);
            video.removeEventListener('pause', syncPlay);
            video.removeEventListener('loadedmetadata', onLoaded);
          };
          const tick = () => {
            if (closing) return;
            const found = findVideo();
            if (found && found !== video) { unbind(); bind(found); onLoaded(); }
            adShowing = !!document.querySelector('.ad-showing,.ad-interrupting');
            const id = mediaId();
            if (id !== currentMedia) { currentMedia = id; onLoaded(); fallbackRequested = false; }
            if (!publicClip && id && switchedFor !== id && !adShowing) {
              switchedFor = id;
              const option = videoOption();
              if (option) { switching = { source: video.currentSrc, position: video.currentTime, started: performance.now() }; option.click(); }
            }
            if (switching && (video.currentSrc !== switching.source && video.readyState >= 2 || performance.now() - switching.started > 6000)) {
              if (video.currentSrc !== switching.source && switching.position > 2 && Math.abs(video.currentTime - switching.position) > 1.5 && Number.isFinite(video.duration))
                video.currentTime = Math.min(switching.position, video.duration - .25);
              switching = null;
            }
            if (qualityTries < 24 && !adShowing) qualityTries = requestQuality() ? 24 : qualityTries + 1;
            const type = !publicClip && moviePlayer()?.getPlayerResponse?.()?.videoDetails?.musicVideoType;
            const song = !publicClip && document.querySelector('ytmusic-av-toggle');
            const picture = video.videoWidth > 0 && video.readyState >= 2 && type !== 'MUSIC_VIDEO_TYPE_ATV' && song?.videoMode !== false;
            if (picture) { pictureLostAt = 0; ui.dataset.picture = 'video'; }
            else if (!pictureLostAt) pictureLostAt = performance.now();
            else if (performance.now() - pictureLostAt > 1400) ui.dataset.picture = 'none';
            if (!publicClip && !adShowing && !switching && !picture && !fallbackRequested && performance.now() - pictureLostAt > 3000) {
              fallbackRequested = true;
              post('diplayer-clip-music-fallback');
            }
            showQuality();
          };

          bind(video);
          syncPlay();
          resize();
          window.addEventListener('resize', resize);
          document.addEventListener('keydown', keydown, true);
          document.addEventListener('mousemove', wake, true);
          document.addEventListener('wheel', wheel, { capture: true, passive: false });
          const watchdog = setInterval(tick, 500);
          frameId = requestAnimationFrame(frame);
          wake();
          showQuality();

          const api = window.__diplayerClip = {
            mediaId,
            setChrome(assets, brandWidth, active, maximized) {
              chromeAssets = assets;
              wordmark.style.width = brandWidth + 'px';
              for (const image of ui.querySelectorAll('img[data-asset]')) applyAsset(image, image.dataset.asset);
              for (const button of side.querySelectorAll('button:not(.dc-mascot)'))
                if (button.dataset.action === active) button.setAttribute('aria-current', 'page');
              this.setMaximized(maximized);
            },
            setMaximized(maximized) { applyAsset(maximizeImage, maximized ? 'restore' : 'maximize'); },
            requestQuality() { qualityTries = 0; menuTried = false; menuHeight = 0; requestQuality(); showQuality(); },
            start(playback, volume, loop) {
              if (!publicClip) return;
              seekTo(playback.Position);
              video.volume = volume;
              video.loop = loop;
              video.muted = false;
              if (playback.Paused) video.pause(); else video.play().catch(() => {});
            },
            playback() { return { Position: video.currentTime, Paused: video.paused, Buffering: video.readyState < 3 || video.seeking }; },
            togglePlayback,
            pause() { video.pause(); },
            setVolume(value) { video.volume = value; },
            setLoop(value) { video.loop = value; },
            reveal() {
              clearTimeout(revealTimer);
              requestAnimationFrame(() => requestAnimationFrame(() => { if (!closing && ui.dataset.state === 'entering') { ui.dataset.state = 'shown'; place(true); } }));
            },
            update(data) {
              if (!data || closing) return;
              if (typeof data.motion === 'boolean') { motion = data.motion; ui.dataset.motion = motion ? 'on' : 'off'; }
              if (typeof data.offset === 'number' && Number.isFinite(data.offset) && offset !== data.offset) {
                offset = data.offset; manualUntil = 0;
              }
              if (Number.isFinite(data.clipOffset)) timingLabel.textContent = (data.clipOffset > 0 ? '+' : '') + data.clipOffset.toFixed(1) + 's';
              if (typeof data.showLyrics === 'boolean') setLyricsVisible(data.showLyrics);
              if (typeof data.artwork === 'string' && data.artwork !== artwork) {
                artwork = data.artwork;
                const image = /^https:\/\//.test(artwork) ? 'url(' + JSON.stringify(artwork) + ')' : 'none';
                curtainArt.style.backgroundImage = fallbackArt.style.backgroundImage = cover.style.backgroundImage = image;
              }
              if (typeof data.active === 'number') hostActive = data.active;
              if (Array.isArray(data.lines)) {
                const incoming = data.lines.slice(0, 1000).map(line => typeof line === 'string' ? { text: line, start: null }
                  : { text: String(line?.text ?? ''), start: Number.isFinite(line?.start) ? line.start : null });
                const next = (data.timed === true) + '\n' + incoming.map(line => line.start + '|' + line.text).join('\n');
                if (next !== signature) { signature = next; render(incoming, data.timed === true); }
              }
              setActive(activeIndex());
              return true;
            },
            close() {
              if (closing) return;
              closing = true;
              cancelAnimationFrame(frameId); clearInterval(watchdog); clearTimeout(idleTimer); clearTimeout(revealTimer);
              window.removeEventListener('resize', resize);
              document.removeEventListener('keydown', keydown, true);
              document.removeEventListener('mousemove', wake, true);
              document.removeEventListener('wheel', wheel, { capture: true });
              if (publicClip) document.removeEventListener('ended', ended, true);
              unbind();
              document.body.removeAttribute('data-diplayer-clip');
              document.body.removeAttribute('data-diplayer-idle');
              ui.remove(); style.remove();
              try { if (qualityStored) { if (storedQuality === null) localStorage.removeItem('yt-player-quality'); else localStorage.setItem('yt-player-quality', storedQuality); } } catch {}
              // Outside the clip the video plays in a hidden one-pixel host: let it adapt again.
              try { moviePlayer()?.setPlaybackQualityRange?.('auto', 'auto'); } catch {}
              if (window.__diplayerClip === api) delete window.__diplayerClip;
            }
          };
          revealTimer = setTimeout(() => api.reveal(), 1500);
          return true;
        })()
        """;
}
