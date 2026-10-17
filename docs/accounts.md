# SoundCloud account import

The import screen includes **Sign in with SoundCloud**. It opens the system browser, requests consent and imports local copies of the user's own playlists, liked playlists and a **Likes** playlist. It does not upload, like, delete or modify anything on SoundCloud.

This is a one-time import, not a permanently connected account. Repeating a completed import updates the matching local copy without creating duplicates and preserves its custom name and cover. An incomplete refresh does not replace a previously complete copy. Canceling retains received tracks in clearly incomplete lists.

## Deployment

The registered SoundCloud app must include this exact callback URL for the default deployment:

```
https://158-180-35-133.sslip.io/diplayer/auth/callback
```

Other deployments should set `DIPLAYER_REDIRECT_URI`. Keep `SOUNDCLOUD_CLIENT_ID` and `SOUNDCLOUD_CLIENT_SECRET` on the server. All SoundCloud clients currently require a secret, so the EXE exchanges no authorization codes directly with SoundCloud.

Keep access logging disabled for the OAuth routes; do not log query strings, authorization codes, bearer credentials or SoundCloud tokens. The callback sends `Referrer-Policy: no-referrer` and a restrictive content security policy. Use HTTPS.

## Temporary sessions

- Authorization Code + PKCE S256, unpredictable state, separate 256-bit desktop-session secret.
- SoundCloud tokens stay in server memory only; the desktop receives neither the user token nor the app secret.
- Ten minutes to authorize, then up to 55 minutes to finish importing. Expired sessions are removed by a one-minute cleanup timer.
- At most 32 sessions globally; existing per-IP request, concurrency and daily quotas still apply.
- Only fixed, read-only account-library endpoints are proxied. Pagination must remain on the same HTTPS SoundCloud API host and endpoint. Redirects are disabled.
- Token refresh is serialized because refresh tokens are single-use. After import/cancel, the client requests disconnection and the server attempts SoundCloud sign-out. If the connection fails, expiry still applies.

No passwords, profile records, personal playlists or user tokens are stored in a server database. Local SQLite stores playlist names, track references/order, custom covers, playlist artwork URLs, SoundCloud creation dates and an account-source identifier for duplicate prevention.

## Practical limits

Lists are fetched in pages of up to 200 with a request interval and rate-limit backoff. A run imports up to 500 playlists and 20,000 tracks per list. Canceled, blocked or incomplete requests are reported instead of claiming a full import.

Account import does not remove playback restrictions. Private or restricted track references may be imported but remain unplayable through the public catalog. AAC-only and other unsupported stream formats retain the normal player's limitations.

## Checks

`tools/Build.ps1` runs mock-transport OAuth contract checks and local-store refresh tests. Mock checks cover state, bearer isolation, a single code exchange, token refresh, rejected cross-host/cross-endpoint cursors, denied consent and disconnect. They do not replace a real account consent/import test.

Use `--selftest --visual-only --check-account-view --test-library PATH --evidence PATH` for an isolated UI check; it does not sign in or read a real account.


## Library display

Likes stays first. Newest/oldest sorting uses SoundCloud creation dates saved during import; existing lists need one repeat import to acquire the date. Lists without dates follow dated lists, using local insertion order as a fallback. Sorting itself makes no API requests.

Original playlist artwork takes priority over a single track-artwork fallback. A user-selected cover takes priority over both. Missing artwork on existing lists is filled when their library page opens.

The library heading mirrors import progress. Missing-track placeholders are skipped at import or hidden after a successful metadata response, without deleting their playlist.

SQLite commits use full synchronization. A separate nonempty library snapshot is saved during imports and normal exit; if the main database file is missing, the snapshot is restored on startup. This is a recovery measure, not a guarantee against every cause of data loss.
