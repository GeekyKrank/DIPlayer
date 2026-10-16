<div align="center">

<img src="src/DIPlayer/Assets/bulldog-logo.png" width="76" alt="DIPlayer logo">

# DIPlayer

**Your SoundCloud tracks. Your playlists. Your desktop.**

A standalone Windows player with a dark interface, local playlists and a compact mini player.

[![Download for Windows](https://img.shields.io/badge/DOWNLOAD-WINDOWS_x64-f59152?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/GeekyKrank/DIPlayer/releases/download/v0.2.1-alpha/DIPlayer-Setup-0.2.1-alpha-x64.exe) [![Version](https://img.shields.io/badge/RELEASE-0.2.1_ALPHA-252525?style=for-the-badge)](https://github.com/GeekyKrank/DIPlayer/releases/tag/v0.2.1-alpha)

[Portable ZIP](https://github.com/GeekyKrank/DIPlayer/releases/download/v0.2.1-alpha/DIPlayer-win-x64.zip) · [All releases](https://github.com/GeekyKrank/DIPlayer/releases) · [Report an issue](https://github.com/GeekyKrank/DIPlayer/issues)

</div>

---

![DIPlayer playlist view](docs/screenshots/player.png)

### Made for listening

| | What you get |
| :--- | :--- |
| **Find & play** | SoundCloud search, single-click playback and precise seek/volume controls. |
| **Your library** | Local playlists, custom covers and public-playlist import by URL. |
| **Your queue** | Reorder, remove and queue tracks — or clear the list in one click. |
| **Stay compact** | A top-center mini player, tray playback and Windows media keys. |

<div align="center">
<img src="docs/screenshots/mini-player.png" width="400" alt="DIPlayer mini player with artwork and playback controls">
</div>

### Get started

1. **Install** using the EXE above, or extract the entire portable ZIP. No separate .NET installation needed.
2. **Search** for a track or import a public playlist. No listener login required with the default catalog.
3. **Listen.** Press **—** for the mini player; double-click its title to return. Use **Quit** in the tray to exit.

**Updating?** Quit DIPlayer, then run the new installer over the existing installation. Your playlists and covers stay saved.

### A few things to know

- Tested on **Windows 11 x64**. This is an unsigned alpha; automatic updates are not included.
- Internet and the shared catalog are required. Some SoundCloud tracks are unavailable to external players; private playlists and access restrictions are not bypassed.
- Audio uses bounded streaming buffers. Earlier measurements are documented; the current version has **not** completed a new one-hour RAM/CPU run.

### Explore the project

[Full guide & build instructions](docs/guide.md) · [Performance & validation](docs/validation.ru.md) · [Catalog server](docs/server.ru.md) · [API investigation](docs/feasibility.ru.md)

<details>
<summary><strong>Build it yourself</strong></summary>

Windows + .NET 10 SDK:

```powershell
pwsh -File tools/Build.ps1 -WindowsRid win-x64
```

For the EXE installer, install Inno Setup 6 or 7 and run `pwsh -File tools/Build-Installer.ps1`.

</details>

---

<sub>Independent project, not affiliated with SoundCloud. API terms and shared-service limits apply. [Third-party notices](THIRD_PARTY_NOTICES.md) · [Artwork & font credits](src/DIPlayer/Assets/NOTICE.md)</sub>
