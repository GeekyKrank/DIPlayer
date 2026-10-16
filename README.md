# DIPlayer

A standalone Windows player for public SoundCloud tracks, built with C# and WPF. Independent project, not affiliated with SoundCloud. The interface is in English.

## Download and install

Get the latest files from [GitHub Releases](https://github.com/GeekyKrank/DIPlayer/releases/tag/v0.2.0-alpha).

- **DIPlayer-Setup-0.2.0-alpha-x64.exe**: Windows installer for the current user, with Start menu and optional desktop shortcuts. Administrator access and a separate .NET installation are not needed.
- **DIPlayer-win-x64.zip**: portable build. Extract the entire archive and run `DIPlayer.exe`; keep its companion files beside it.

Tested on Windows 11 x64. This alpha is unsigned, so Windows may show an unknown-publisher warning. ARM64 and a separate 32-bit Windows installation have not been tested. Uninstalling the application preserves playlists, custom covers and settings in `%LOCALAPPDATA%\DIPlayer`.

The default build connects to the shared HTTPS catalog on Oracle Cloud. Listener registration and user API keys are not needed. Shared quotas and service availability apply. Audio streams directly from SoundCloud's CDN; the catalog server does not proxy or store music.

## Features and controls

- The library is the home screen. Create playlists with **+**, filter by name, and open a card to browse its tracks.
- **Edit** changes a playlist's name and custom image. Supported images: JPG, PNG and BMP, up to 20 MB; saved copies are resized. **Restore track artwork** returns to the automatic collage.
- Search SoundCloud at the top. One click plays a track; Enter plays the selected row. Old searches are canceled when the query changes.
- Clicking a playlist track prepares its playback queue. Search playback uses the current result page. Queues are capped at 1,000 tracks; larger playlists use a block of up to 1,000 around the selected page.
- The **⋮** track menu adds tracks to the queue or a playlist. Playlist/queue menus also remove and reorder entries.
- The bottom queue button toggles between the queue and the previous screen. **Clear** empties the queue without stopping the current song.
- The thicker seek bar supports click-and-drag anywhere in its hit area, with a preview time; audio seeks on release.
- The window's **—** button shows a top-center, always-on-top mini player with pause and previous/next. Double-click its title/artwork to restore the main window; **×** hides only the mini player.
- Minimizing from the Windows taskbar performs ordinary minimization without opening the mini player. Closing the main window hides it to the tray. Use **Quit** from the tray to exit.
- Import a **public** SoundCloud playlist by URL inside the app. Progress and unavailable tracks are reported; incomplete imports remain clearly marked.
- Local playlists, editing, deletion confirmations and import use the main window. Choosing a local image uses the Windows file picker.

## Limits and resource use

Not all tracks are playable outside SoundCloud. Region restrictions, paid access and removal are reported separately only when the API explicitly identifies them; generic blocks are not assigned an invented reason. Private playlists, access bypasses, AAC-only streams, encrypted HLS and live/master HLS are not supported.

Audio is streamed through bounded buffers: compressed segments up to 2 MiB and up to four seconds of PCM, without loading whole tracks into RAM. Track pages contain 25 rows with UI virtualization. Artwork has a 64-image memory cache; custom playlist covers have a separate bounded cache. There is no artwork disk cache. Main-window position updates run at 1 Hz only while visible; the mini player updates state at 1 Hz only while visible. No visualizers, blur or continuous animations.

[Performance measurements and test conditions](docs/validation.ru.md) include an earlier one-hour playback run. Those measurements predate this release's mini player and UI changes: **0.2.0 has not completed a new one-hour RAM/CPU test**. Sleep/resume and physical audio-device changes still need hardware testing.

## Build from source

Requires Windows and .NET 10 SDK:

```powershell
pwsh -File tools/Build.ps1 -WindowsRid win-x64
```

To build the installer, install Inno Setup 6 or 7, then run:

```powershell
pwsh -File tools/Build-Installer.ps1
# Or pass -Compiler C:\path\to\ISCC.exe
```

The application uses Segoe UI with heavier weights. The DIPlayer title optionally uses a user-local Dirty Stains demo font; that font is not distributed. Downloaded font experiments are not included in the EXE, installer or repository.

## Catalog and credentials

For a different catalog, use `--server https://your-server/`. For private testing only, `--credentials path-to-file` accepts `client id: ...` and `client secret: ...` and protects them for the current Windows user with DPAPI. Never publish credentials, tokens, SSH keys or local settings. The packaged application contains no API secret.

The backend source and deployment notes are in [docs/server.ru.md](docs/server.ru.md); the initial feasibility investigation is in [docs/feasibility.ru.md](docs/feasibility.ru.md). Client compatibility translates known responses from older catalog servers. Publishing source or this experimental build does not establish approval from SoundCloud; the API terms and permission for the intended public use still apply.

## Modules

`DIPlayer.Core` (catalog/search), `DIPlayer.Audio` (bounded streaming/native decode), `DIPlayer` (UI, import, queue, SQLite storage and Windows integration), `DIPlayer.Server` (catalog service), plus prototype and integration-check projects.

See [third-party notices](THIRD_PARTY_NOTICES.md) and [asset notices](src/DIPlayer/Assets/NOTICE.md). User-supplied background and logo artwork remain the property of their respective rights holders.
