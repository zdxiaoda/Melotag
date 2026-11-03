# Melotag Privacy Policy

Effective date: 2026-11-03

This policy explains how Melotag handles information in its Windows desktop edition. Melotag is a local music tag editor. It does not require an account and does not include advertising, analytics, or telemetry.

## Local files

Melotag reads only the music, lyric, and cover files you add or select, and writes to those files when you save, rename, or export. It does not upload audio content, embedded cover art, or full lyrics, and it does not send full file paths to any server. Online lookups send the text needed for the search, such as title, artist, album, or filename (without the path), plus identifiers returned by the selected service. As with any network request, they also include standard technical information such as your IP address and User-Agent. The local file itself is not transmitted.

## Data stored on your device

Settings are stored in `%APPDATA%\Melotag\settings.json`, and history is stored in `history.db` (SQLite) in the same folder. This data may include:

- recent and managed folders;
- paths relative to the volume in history (absolute paths themselves are not stored), along with file size and modification time;
- tag and lyric snapshots taken before a save or rename;
- online source order, language, theme, and lyrics/cover preferences;
- if you enter a Last.fm API key, it is stored in plain text in `settings.json`; when you use the Last.fm source, the key is sent with requests to Last.fm.

By default, these files stay on your device. Do not share `settings.json` or `history.db` with anyone you do not trust. If you set `MELOTAG_DATA_DIR`, settings and history are stored in that folder instead.

## Online lookups

Melotag connects to the network for online tag matching, cover search, and lyric search when you use those features. Online lookups send search text (usually title, artist, and album) to the third-party services you select. These may include Chinese music services (NetEase Cloud Music, QQ Music, Kugou, Kuwo) and international services (MusicBrainz, Cover Art Archive, iTunes Search API, Last.fm, LRCLIB). Requests go directly to those services' domains and are subject to their privacy policies and availability. Melotag does not control their responses and cannot guarantee that results are comprehensive or always available.

At startup, Melotag checks GitHub Releases for the latest version by default. That request includes standard network metadata such as your IP address and User-Agent. You can disable the startup check in Settings and run it manually from the Help menu.

## Accounts, ads, and analytics

Melotag has no account system, displays no ads, and does not include third-party analytics, crash reporting, or behavioral tracking SDKs. We do not sell, rent, or share your personal information. Because online lookups connect directly to third-party services, those services may log requests under their own policies.

## Your choices

- Clear your history from the History panel.
- Delete the `%APPDATA%\Melotag` folder to remove settings, history, and the local database. Exit the app first.
- Uninstalling the app does not delete the music files you edited. Removing files from the list does not delete them from disk.
- For data held by an online service, contact that service directly.

## Policy updates and contact

This policy may change as features evolve. Updated versions will carry a new effective date. For privacy questions, contact us at https://github.com/zdxiaoda/Melotag/issues.
