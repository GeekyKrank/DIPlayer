# Discord listening status

DIPlayer shares the current SoundCloud or YouTube Music track with the Discord desktop client through local Rich Presence IPC. The default application is DIPlayer (`1561330201911103572`); users do not need to create an application or provide account tokens.

In **Settings → Discord**, use the sharing switch; changes are saved automatically. Discord must be running and its activity privacy settings must allow sharing activity.

While audio plays, the activity contains the track title, artist, public track artwork and playback timestamps. Pausing, stopping, disabling sharing or quitting clears the activity. YouTube advertisements are excluded. Updates run every five seconds, including while DIPlayer is minimized; unavailable Discord connections are retried without interrupting playback.

Protocol reference: https://discord.com/developers/docs/topics/rpc
