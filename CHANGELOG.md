# Changelog

## 1.1.0 — 2026-11-06

### Added

- Browse the cached catalogue directly in Add games, with sorting by first-seen date or name.
- Track newly discovered games with NEW markers and the New since last refresh filter. Refreshes with no additions keep the previous batch, even after restarting.
- Playable only toggle, enabled when opening Add games, to hide entries without a supported Windows executable.
- A 10-game preview for faster list display. Scroll to Show all after the tenth game to load the rest while keeping filters, selection and scroll position.
- A matching dark sort dropdown in Library. Games default to date added, newest first; name and oldest-first sorting are also available.
- A green Playing indicator beside Stop, with elapsed time that counts up even when the optional countdown is off. It resets for each game and hides during playlist pauses or after Stop.

### Improved

- Rounded dark dropdowns with checkmarks for the active sort option.
- Catalogue rows show when a game was first seen locally, availability and its executable path.
- Sorting preserves selected Library games. Show all is excluded from game selection and add actions.
- Added regression checks for catalogue tracking, filters, sorting, list expansion and playback status.

First-seen dates describe when this app discovered a game, rather than its release date or when Discord originally added it.

## 1.0.0 — 2026-10-26

- Initial public release with a searchable offline catalogue, manual Library, timed playlists, shuffle, repeat, random durations, pauses, background mode and game-list import/export.
