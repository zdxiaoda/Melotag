# Microsoft Store Listing: English (en-US)

> Field headings are for editing and submission reference only. Copy only the body text into Partner Center.

## Product name

Melotag

## Short title

Melotag

## Short description (270 characters max)

Music tag editor on .NET 10 and Avalonia for Windows, Linux, Android: edit tags and art, clean LRC lyrics, rename with templates or regex, match online sources, convert Chinese scripts, restore local history. Store: Windows x64; Linux/Android from project Releases.

## Description

Melotag is a local music tag editor for Windows, Linux, and Android, built for tidying downloaded tracks, filling in album details, and keeping cover art and lyrics consistent across a library. Drag in files or folders, then edit title, artist, album, album artist, year, track, disc, genre, comment, composer, lyrics, and cover art one track at a time. The editor also shows format, duration, bitrate, sample rate, channels, and file size. When saving, you can choose ID3v2.3 or 2.4 for MP3, preserve the original file timestamp, write lyrics into the tag, and replace or clear embedded artwork.

Melotag is built with .NET 10, C#, and Avalonia 12. The Windows, Linux, and Android builds share one tag-processing core and one UI codebase, so the file list, editing flow, and settings stay consistent across platforms. The app does not use an Electron or Chromium shell: Avalonia draws the interface, and the logic runs in the same .NET process. Tags are read and written locally through TagLib#, and batch matching, renaming, and saving do not upload audio files, so batch work on a library does not wait on uploads. Online matching queries metadata, cover art, or lyrics only when you request it. The Microsoft Store package is the Windows x64 build; Linux and Android builds are available from the project's Releases page.

Online matching can query several music databases at once and present candidate tags, covers, and lyrics in one place. Apply a result to a single track, or use batch auto-match to choose the fields to import, the sources to use, and the overwrite behavior before processing each file. The lyrics panel searches for lyrics and can reflow multiple timestamps, remove time tags, delete blank lines and header tags, apply an offset, and import or export with UTF-8, UTF-16, GB18030, Big5, and other encodings. Cover tools can search online or use a local image, then resize automatically to fit size, width, height, and format limits.

Renaming supports @0–@8 field templates, the default Artist - Title pattern, and regular-expression capture groups, with a live preview and automatic numbering for duplicate names. Matching .lrc and cover files can be renamed alongside the audio file, and reverse mode writes filename information back into tags. Simplified/Traditional Chinese conversion covers both tags and filenames, including matching .lrc and cover files. Before each save or rename, a snapshot of the previous tags is stored in a local history database, which you can review, restore, or clear from the History panel.

The interface is available in English, Simplified Chinese, and Japanese, follows the system language on first launch, and switches immediately from Settings. You can also adjust the theme, use compact rows, reorder online sources, and set the search region. A silent update check runs at startup by default; it can be turned off in Settings or run manually from the Help menu.

Notes: DSF/DFF files are recognized, but writing has not yet been validated with real DSD samples. Files containing multiple audio tracks are skipped. Online matches, covers, and lyrics depend on the availability and responses of the third-party services you select, so results may be incomplete or temporarily unavailable. Keep backups of original files before batch changes.

## Product features (10 items)

1. Tag editing: title, artist, album, album artist, year, track, disc, genre, comment, composer, lyrics, and cover art; for MP3, choose ID3v2.3/2.4 and keep the original file timestamp.
2. Audio details and formats: recognizes MP3, FLAC, APE, WavPack, TTA, MPC, OGG, Opus, M4A, MP4, WAV, AIFF, WMA, DSF, and DFF; shows duration, bitrate, sample rate, channels, and file size.
3. Multi-source online matching: query several music databases and compare tag, cover, and lyric candidates; apply one result or run batch auto-match with chosen fields, sources, and overwrite behavior.
4. LRC lyric tools: search lyrics, reflow multiple timestamps, remove time tags, blank lines, and header tags, apply an offset, and import or export with UTF-8, UTF-16, GB18030, Big5, and more.
5. Cover art management: search online, choose a local image, export, remove, or extract covers in bulk; images are resized to fit file-size, width, height, and format limits, then written back on save.
6. Template and regex renaming: use @0–@8 fields, Artist - Title default, or regex capture groups, with live preview and automatic duplicate numbering; matching .lrc and cover files can be renamed too.
7. Filename-to-tag reverse mode: extract information from filenames or regular expressions and write it back into tags, useful for libraries from mixed sources.
8. Simplified/Traditional Chinese conversion: convert tags and filenames in either direction, including matching .lrc and cover files.
9. History and restore: save a tag snapshot to a local database before each save or rename, then review, restore a selected version, or clear entries from the History panel.
10. Cross-platform: Windows, Linux, and Android share .NET 10 and Avalonia 12 tag and UI code; the Store offers Windows x64, other platforms via Releases. Trilingual UI; settings and history stay local.

## Search keywords (7 items, 40 characters max each)

1. music tag editor
2. MP3 metadata
3. batch tag matching
4. lyrics editor
5. album art
6. audio file renamer
7. simplified traditional Chinese

## Copyright

Copyright (c) 2026 zdxiaoda. Licensed under the MIT License.

## Developed by

zdxiaoda

## Screenshot captions

- 01-main: Main window: file list and tag editor side by side. Drag in files or folders, then check status, format, and audio details.
- 02-lyrics: Lyrics panel: search LRC, clean up time tags, and preview the result; import or export lyric files.
- 03-rename: Rename: build new filenames with templates or regular expressions, preview duplicate handling, and optionally rename lyrics and covers too.
- 04-settings: Settings: organized into Search, Sources, Lyrics, Covers, Writing, and Interface; switch language and theme, or disable the startup update check.

## What's new

First submission: this field can be left blank.

Fallback update text (1.2.1 / 1.2.0): Melotag is now available on Microsoft Store. This release adds the English, Simplified Chinese, and Japanese interface; multi-select with batch removal from the list and copy; batch auto-match with selectable fields, sources, and overwrite behavior; lyrics returned with online tag matches; and version checks. It also improves the settings layout on narrow screens and the source-order list, and fixes dialog buttons, lyric result titles, the default rename pattern, numeric sorting, and source region selection.

## Additional system requirements

1. Windows 10 version 2004 (build 19041) or later; x64 processor.
2. The Microsoft Store package includes the components needed to run, so no separate .NET runtime installation is required.
3. An internet connection is needed for online tag, cover, and lyric lookups; local editing and saving work offline.

## Support URL

https://github.com/zdxiaoda/Melotag/issues

## Website

https://github.com/zdxiaoda/Melotag
