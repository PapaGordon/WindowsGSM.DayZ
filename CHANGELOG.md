# Changelog

## 0.1.3 - 2026-11-22

- Starts the server with the installed Workshop mods when SteamCMD cannot log in, download or confirm an update, for example while Steam Guard waits for confirmation. The skipped update is logged as a notice.
- Only replaces mods whose files changed and keeps only the latest backup in `.wgsm-workshop-backups`. Previously every start copied all mods into a new backup and could fill the drive.
- Leaves at least 5 GB free and skips the update otherwise, so SteamCMD does not lose its saved login on a full drive.
- Retries the download once when SteamCMD exits before the login. Parallel Workshop jobs wait up to ten minutes for each other.

## 0.1.2 - 2026-10-29

- Adds optional Workshop updates before server start, staged mod deployment, managed signing keys and rollback. See WORKSHOP.md.

- Refreshes the README links, current WindowsGSM guidance and Steam credential compatibility notes.
- Uses the current canonical repository URL.


## 0.1.1 - 2026-10-24

- Adds RPT output to Embed Console, using the configured profile folder.
- Follows partial writes, UTF-8 text, log rotation and final shutdown lines.
- Fixes native window discovery and synchronizes the Toggle Console handle.

## 0.1.0 - 2026-10-24

- Adds local DayZ defaults and synchronization of the WGSM name, slots and query port.
- Uses the configured game port, IP and mission, supports custom config/profile paths and merges mod lists.
- Preserves custom settings, backs up config changes and tries a shutdown signal before host termination.
- Keeps firewall handling in WindowsGSM and remains available separately from the MeFriendos fork.
- Includes the MeFriendos author logo, DayZ list icon and setup documentation.
