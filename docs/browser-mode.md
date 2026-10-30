# Experimental YouTube Music browser mode

Open Settings (gear at the bottom left) and switch from SoundCloud to YouTube Music. The selection is saved. Switching back disposes the WebView and stops its audio; no WebView browser is created at normal optimized startup.

Browser mode uses Microsoft Edge WebView2 and the actual music.youtube.com website. DIPlayer provides native search and playback controls, with the original website available as a fallback. The toolbar also offers search (Enter), Back, Home and Reload. Playback/media keys route to the website in browser mode. The mini player also routes YouTube playback controls to the browser.

An Evergreen WebView2 Runtime must be installed. If missing, the browser panel explains the requirement and Settings links to Microsoft's runtime download. The runtime is not bundled or silently installed.

The browser loads the official uBO Lite 2026.1012.1545 Edge extension. Build preparation downloads the pinned unmodified archive from the author's GitHub release and checks SHA-256; runtime extraction verifies the same hash before loading. The package and included license remain separate from DIPlayer source. The extension runs only in browser mode. A small supplemental built-in filter blocks known advertising hosts and selected request paths.

This cannot guarantee all first-party/in-stream advertisements are removed. Changes to YouTube may break filtering; it does not bypass geographic, subscription, account or other playback restrictions.

Browser cookies/sign-in stay locally in LocalAppData/DIPlayer/youtube-browser. The app does not read Google passwords or transfer browser cookies to the catalog server. Google may restrict sign-in in embedded browsers. Music streams in the browser directly; the DIPlayer server is not used for YouTube.

This mode uses extra browser processes and memory. No equivalence to the optimized SoundCloud resource footprint is claimed.

Checks: unit checks for ad-host matching and audio/search/sign-in exclusions; isolated live WebView initialization and enabled uBO Lite registration, YouTube page loading/search navigation, settings visibility, returning to SoundCloud and disposal; wide/narrow settings screenshots. These checks do not prove all ads are removed, successful Google sign-in or every track can play.


## Native DIPlayer interface

The YouTube mode now defaults to DIPlayer controls: native search, result/playlist rows, Home, Library, Likes, queue, playback, seeking, volume, repeat, shuffle and like/unlike. The top-level window, artwork, mini player and media-key routing remain DIPlayer controls. The Website / sign in button exposes the original page for login, consent and operations not represented in the native view. A sign-in dialog can also bring that page forward.

This is an experimental bridge to the rendered YouTube Music page, not a public streaming API and not an audio downloader. It reads rendered text/artwork and controls the existing browser player. No Google password, cookie or media URL is sent to the DIPlayer server. Google account features require signing in on the website. Site UI changes may require bridge updates.

The browser stays active in a clipped native host while the DIPlayer interface is shown; collapsing the media page before startup can stall its loader. Switching back to SoundCloud disposes the host as before. Displayed result batches are bounded to 100 rendered rows. The original website remains available for unsupported views or long lists.

Checks include a real public search, selecting a track, waiting for actual music rather than an advertisement, pause/resume, volume, seek and locating repeat/shuffle controls; screenshots in wide/narrow layouts and the return to optimized mode. A Windows screenshot was also inspected to verify that the browser does not cover the native controls.


YouTube playback selected from the native list uses a separate in-memory DIPlayer queue. Repeat and shuffle use the player's queue logic rather than fragile website button selectors. A document media-ended bridge advances that queue; repeat-one loops the current browser media element. Opening Website returns playback-queue ownership to the website. Native and SoundCloud queues are separate.

Native live checks passed for actual music playback, pause/resume, volume, seek, shuffle toggle, repeat-one loop and the end-of-track message advancing the queue. The Google account Library/Likes paths require the user's own sign-in and were not account-tested.


YouTube browsing uses native large square artwork cards throughout Home, Library, search results and Queue. Album and playlist cards resolve to a DIPlayer collection header with artwork, Play all and track cards; Back restores the previous gallery. A single with one resolved track starts playback without replacing the gallery. Playback preserves the currently viewed collection. Account navigation is used only for sign-in/consent; a recognized Google return or signed-in state restores DIPlayer automatically. Already connected accounts stay in DIPlayer when the account button is pressed.

Live isolated checks cover a real album and a one-track single, card navigation/back, native search, actual playback, and the mini player's pause/resume, next/previous and metadata while the main window is hidden.


Before the first YouTube document loads, DIPlayer opens the packaged extension's own dashboard internally and uses its settings messages to enable Complete filtering for youtube.com and music.youtube.com. It waits for the extension to report mode 3, enabled rulesets and registered content scripts. This changes settings in DIPlayer's isolated browser profile; the pinned extension source/archive is unchanged, and other installed browsers are unaffected. Registration/readiness does not prove that every first-party advertisement will be removed.


Navigation is service-specific: YouTube mode replaces the left SoundCloud Search/Queue/Import buttons with YouTube Library/Likes/Queue, while the shared house icon opens the active service's home. Back stays above the scrolling content and restores native view history (including collection metadata and scroll position) without navigating away from playing audio. History is bounded to 32 views and cleared when YouTube is unloaded. SoundCloud playlists remain in their own SQLite store; YouTube library/likes come from the local Google browser profile, and neither is imported into the other service's library.


## Artist pages, recommendations and track menus

Artist cards and links open a native artist header and cover shelves for the top songs, albums, singles and other sections returned by YouTube Music. The artist name beside the playing track opens its artist page (or an artist search when no link is supplied). Channel and @handle URLs are supported. Home preloads additional recommendation batches; scrolling near the end and Load more request further site continuations, bounded to 600 rendered items.

Right-click a cover card for queue/artist actions or **YouTube Music actions…**. DIPlayer mirrors the site's actual menu and playlist chooser in native controls, including add/remove playlist actions when the account has permission. These actions affect the YouTube account's playlists, not the SoundCloud SQLite library. Queue and Play next actions stay integrated with DIPlayer's playback queue. No menu action is submitted until the user selects it; stale menu selections are refused.

**Lyrics** in the bottom player opens an artwork-and-text view. Text comes from YouTube Music. When it has no timing, opening Lyrics can request a timestamped version from the LRCLIB API using only the track title, artist and duration. Matches require the same title/artist and a duration difference within about two seconds; results are cached for up to 32 tracks and rate-limit retry headers are respected. Real line timestamps drive a 150 ms highlight timer using the actual playback position; pausing freezes it and seeking moves to the corresponding line. Clicking a timed line seeks to it. Unavailable timing is labelled explicitly; no artificial timestamps are generated. Unavailable lyrics clear the previous song's text.

Unexpected close requests no longer silently hide the main window; the explicit Close/Minimize buttons still send it to the tray. Bounded `window-events.log` records window state/visibility transitions and close requests without track titles or account details. The reported periodic disappearance was not reproduced during targeted checks. A five-minute playback soak test has not been completed; the guard is not a confirmed root-cause diagnosis. WebView initialization permission failures stay in the native retry screen.


The full artist view includes a high-resolution wide banner, monthly-listener text and expandable biography from YouTube, artist actions, a top-song table, album/single shelves, landscape videos, artist playlists and circular related-artist portraits. Clicking the footer artist opens that page. Large artwork is cached separately with a four-image limit; existing cover thumbnails retain their smaller decode limit.

Album and playlist details use a 270 px cover in the upper left, title/metadata/description and Play all on its right, Share/menu in the upper-right corner, and a song/artist/album/duration table below. Track menus work from the row ellipsis or right click. Artist and album navigation preserve native Back history.

LRCLIB API reference: https://lrclib.net/docs
