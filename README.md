 # GameSpoof for Discord
<div align="center">
    <img src="/Screenshot1.png" width="1000px"</img> 
</div>

a simple program to spoof playing games on discord. with playlist functionality.

Search Discord’s game catalogue, add games to your library, and create playlists with custom playtimes. Includes shuffle, repeat, random durations, pauses between games, background mode, and game list import/export.

Run `GameSpoof\GameSpoof.exe`. Keep the runtime folder together in a writable location. The app stores its library, playlist, window-size settings, and cached game files there.

## Offline catalog and Refresh

Download the Windows ZIP from [Releases](https://github.com/StriK3FoRC3/GameSpoof/releases/latest), extract it, and run the executable. Windows with .NET Framework 4.8 is recommended.

Library contains only games you explicitly add. The complete named application catalog is downloaded separately for searching and adding games in **Add games**. Click **Refresh Game Catalogue** beside the search field in **Add games** to download updates. The last successful update is shown in that tab. The downloaded catalog lives in `GameSpoof/catalog-cache.json` (about 2 MB for 10,390 supported entries in the October 25, 2026 snapshot), so the library works offline. Refresh validates the complete response and saves it atomically; failures leave the previous catalog and timestamp intact.

The first launch with no cache downloads it automatically. Older caches that excluded unavailable titles are upgraded automatically once. With a saved cache, no download is required until Refresh Game Catalogue is clicked. Entries without a supported Windows executable remain searchable in Add games with an unavailable label and a disabled add action. Your added games and custom paths in `games.tsv`, including FC 27's test path, are preserved. Refresh never adds games to Library automatically. Refresh does not alter playlist snapshots or their durations. Removed games stay hidden across refreshes and can be restored through Add games or import. Hidden entries are stored in `library-preferences.json`.


## Library

Select a game and click **Play now**. The small switch beside the duration enables the manual countdown; it defaults off on a fresh installation. The countdown and Play in background switches are remembered in `app-settings.xml` across restarts. With the timer off, the game keeps running until stopped. Turning the timer off during manual playback pauses its remaining time.

Duration inputs accept a number followed by **s**, **m**, or **h**: `30s`, `5m`, `2h`. Bare numbers automatically gain an **m** suffix while typing; entering **s** or **h** replaces that automatic suffix. Press **Enter** to apply a valid value: update selected playlist entries, save timing ranges, or set the Library duration (including the total duration of an active manual timer). Durations support one second through seven days. Invalid input turns red and cannot start playback or be applied with Enter.

Hover a library row to reveal its playlist and trash icons. If the hovered game belongs to a multi-selection, its action applies to all selected games. Otherwise, the action applies to that game. Use Ctrl/Shift or Ctrl+A for multi-selection. Removing games requires a dark confirmation dialog and preserves existing playlist entries. A game cannot be added to the playlist more than once; names and executable paths are compared without case sensitivity.

**Play in background**, at the top of the launcher, moves the game process window off-screen while keeping the window open and its taskbar entry available. Switching it off restores its previous position and window state. The same game executable keeps running throughout; Discord recognition still depends on the game's detection rules.

## Playlist

Each playlist entry always uses a timer, independently of the Library switch. Select entries to batch-change durations with **Apply**, reorder them, or remove them using **Remove** or **Delete**. Press **Ctrl+A** to select every entry. **Next** skips ahead, **Repeat** loops the list, and **Stop** cancels playback. The tab shows the number of unique entries.

The **shuffle icon** just left of Remove toggles shuffled order. Turn it off to restore the previous order; removed games stay removed and newly added games appear at the end of that original order. Each game's duration and your selection are preserved. Shuffle state and the original order survive restarting the app. Order changes during playback apply on the next playlist start.

The range row above the editing buttons has two options:

- **Random**: choose minimum and maximum playtime. Each game draws a fresh duration in that range instead of using its entry's duration.
- **Pause**: choose minimum and maximum pause duration. Between games, the old process stops and no game process runs until the pause ends. Pauses also occur when Repeat wraps to the first game. There is no pause after a non-repeating playlist finishes. **Next** during a pause starts the pending game immediately. Set pause minimum and maximum to `0s` for no delay.

The ranges use the same `s`/`m`/`h` inputs. Minimum must not exceed maximum. Playlist entries, durations, Repeat, and range options save in `playlist.xml`. Timing changes apply the next time a playlist starts. Existing duplicate entries in older playlists are collapsed on load/import, keeping the first entry.

## Add games

Enter a title, or several comma-separated titles. Results update automatically as you type using the saved catalog. **Refresh Game Catalogue** beside the field downloads updates. The app downloads the catalog automatically on first launch if no cache exists; if that fails, the refresh button retries it. Select matches and click **Add selected games** to save them to Library. Existing games are skipped; removed entries can be restored. Games with no supported Windows executable cannot be added. Such rows remain visible in search so their unavailable status is clear.

**Add EVERYTHING to playlist** adds every supported game in the complete downloaded catalogue directly to Playlist, independently of the search filter. New entries start at 5 seconds. Existing entries keep their durations; duplicates and unavailable games are skipped. This does not add games to Library or change its files.

Search supports GTA abbreviations such as GTA5 and GTA IV, Roman/numeric sequels, Discord aliases, and partial title words. Library search also understands GTA abbreviations. No AI prompt or account token is needed.

Games without a supported Windows executable have an info button at the right of their row. Click it (or select the game and press F1) for a dark explanation dialog: the game may use another detection method or have missing catalogue details, and GameSpoof currently cannot spoof it.

## Import and export

Use the buttons at the bottom of **Add games**. Game-list exports are UTF-8 TSV files containing only the game name and relative executable path, separated by a literal tab. The executable path includes its filename, for example:

```text
Crab Game	crab game/crab game.exe
```

Exports contain no playlist durations, ranges, repeat options, or dates. Imports also accept the older optional third date-added column (`yyyy-MM-dd`). New entries get the import date. Identical entries are skipped; corrected paths can update existing entries. Empty executable fields cannot add games or overwrite working matches. The complete file is validated before changes are saved.

Older XML playlist files remain importable for compatibility. The app stores timing internally but the export dialog only exports game lists.

## Detection and cached game files

The source for automatic game lookup and refresh is Discord's public application catalog: https://discord.com/api/v9/applications/detectable. Filename and relative folder suffix can both matter. GameSpoof creates idle copies of itself; it does not launch real games, modify Discord, or use account credentials. Enable Discord activity sharing. Rich Presence is not implemented. Local launch checks do not guarantee Discord recognition.

Generated files live under `GameSpoof/SpoofedGames`. They are created when a game is first played and remain after Stop, playlist completion, and launcher shutdown. Later sessions reuse matching files. When the app changes, cached executables are checked against the current build and refreshed on their next launch. Normal shutdown still stops the running game process; the child also exits if its parent disappears. Renaming the folder and caching files do not guarantee antivirus acceptance.

GameSpoof is an unofficial project and is not affiliated with Discord or Steam.

## Project and development

Create a portable release with `powershell -ExecutionPolicy Bypass -File .\release.ps1 -Version 1.0.0`. Release packaging includes only the executable and README. Local libraries, playlists, settings, imports, generated game files, and test output are excluded from both Git and the release ZIP.

- `GameSpoof/`: portable runtime, library, saved playlist, and generated window-size settings.
- `src/`: application source, cached catalog storage, and custom controls.
- `tools/IconBuilder.cs`: builds the embedded icon from the drawing in `src/Theme.cs`.
- `tests/`: catalog and isolated integration checks.
- `build/`: disposable build output, test data, and previews, created when building/testing.

The launcher defaults to a 560 × 661 client area. It saves normal dimensions and maximized state in `window-size.txt`; minimizing does not replace those settings. Invalid settings fall back to defaults.

Build and test on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\test.ps1
```

Uses the Windows .NET Framework compiler without additional packages. Builds update only the runtime executable and preserve library and playlist files. Integration tests use separate folders under `build/qa-output`, covering duration units, unique playlists, hover controls, import/export, background windows, cached executable reuse across sessions, randomized playback and pauses, repeat, persistence, and confirmations. Catalog tests run offline by default; `build\CatalogTests.exe --live` additionally checks the public endpoint.
