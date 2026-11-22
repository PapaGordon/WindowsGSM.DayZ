# Workshop mods

The plugin can keep your DayZ server's Workshop mods up to date. Before every start, including scheduled restarts, it asks Steam for new versions of the mods you listed and installs them into your server folder. Subscribing to mods in Steam doesn't do anything for a dedicated server, the list below is what counts. The DayZ server itself is still updated through **Update on start**.

The built-in DayZ server in [MeFriendos WindowsGSM](https://github.com/PapaGordon/WindowsGSM) works exactly the same way.

## Set it up

1. Stop the server and make a backup.
2. Save a Steam account in WindowsGSM that can download the mods. For encrypted passwords, see [Steam account and password](https://github.com/PapaGordon/WindowsGSM/blob/main/docs/operations/steam-credentials.md).
3. Create the file `servers\<server ID>\configs\WorkshopMods.cfg`, next to the server's other WindowsGSM settings.
4. Add one line for each mod and start the server.

```text
# Workshop ID | folder on the server | mod or servermod
1559212036|@CF|mod
2545327648|@Dabs_Framework|mod
1828439124|@VPPAdminTools|mod
```

Each line has three parts:

- **Workshop ID:** the number after `id=` in the mod's Workshop link. Use the ID of the mod itself, not of a collection.
- **Folder:** the name the mod gets in your server folder. You choose it. It has to start with `@` and may contain letters, numbers, `_` and `-`.
- **`mod` or `servermod`:** `mod` for mods players also need, `servermod` for mods that only run on the server.

Mods load in the order of the list, so put dependencies like CF above the mods that need them. Lines starting with `#` are comments. Without this file, or with only comments in it, nothing is downloaded and the server starts as before.

The folder name is what links a mod to your server. Steam only knows the Workshop ID, so the plugin always installs that ID into the folder from your list, no matter what the mod is called on the Workshop. If you rename a mod folder, change it in the list too. Otherwise the next start creates a new folder and the old one stays behind unused.

The listed folders are added to `-mod` and `-serverMod` for you. Mods you manage by hand can stay in your start parameters or in `DayZActivatedMods.cfg`. Just don't list the same mod in two places. When you remove a line, the plugin stops updating that mod. The folder and its start parameter stay until you remove them yourself.

## What happens on every start

1. The plugin signs in to Steam with the saved login and asks for all listed mods. Steam only downloads what changed since the last time.
2. It compares each downloaded mod with the folder on your server. Mods that didn't change are left alone, so a restart without mod updates copies nothing.
3. Changed mods replace their folder completely, so files the mod author removed disappear too. Their `.bikey` signing keys are copied to `serverfiles\keys`.
4. The server starts with the updated mods.

Keep mod settings that you edit yourself outside the mod folders, as the mod authors describe. Anything inside a listed folder is replaced on the next update. Folders and keys that are not in your list are never touched.

## If something goes wrong

If Steam can't sign in, a download fails or a mod can't be replaced, the server still starts with the mods it already has. The WindowsGSM log then shows a line starting with `[Notice]` that says why. If an update you expected is missing, look there first. A common cause is a Steam Guard request nobody confirmed.

The start is only cancelled when a mod from the list has never been installed, the list has a mistake in it, or an earlier update was interrupted (see below).

A few more things to know:

- The plugin keeps at least 5 GB free on the drive. With less space it skips the update, because SteamCMD can lose its saved Steam login on a full drive.
- Downloads stop after 30 minutes. If SteamCMD quits before it even reaches the Steam login, the plugin tries once more.
- Only one Workshop update runs at a time per SteamCMD folder. A second server waits up to ten minutes and then starts with its installed mods. Don't run SteamCMD by hand while servers are starting.
- The downloads are cached in `configs\workshop-state-221100\download`. Leave that folder in place, otherwise Steam has to download every mod again.

## Backups

When mods are replaced, the old versions go to `serverfiles\.wgsm-workshop-backups`. Only the latest backup is kept. The previous one is deleted after the next successful update, so the folder can't fill up your drive.

If WindowsGSM or the machine stops in the middle of replacing mods, the next start is blocked and you find `configs\workshop-state-221100\pending.txt`. It lists the backup folder and every path that was being replaced, in order. The numbered entries in the backup's `previous` folder belong to that list, starting at 0. With the server stopped, copy them back to their paths, check that the mods are complete and then delete `pending.txt`. Entries without a backup copy were new installs. Please don't delete `pending.txt` just to get the server running, or you may start with half-copied mods.

## Details

- Signing keys get the prefix `wgsm_221100_<Workshop ID>_`, so the plugin knows which keys it manages. If you edit one of them, the update stops with a conflict instead of overwriting your change. Keys you added yourself stay. Remove old ones only when no mod needs them anymore.
- DayZ mods come from Workshop app **221100**. The server itself keeps its app ID `223350`.
- The plugin doesn't change your Steam subscriptions, doesn't expand collections and doesn't run install scripts that some mods ship with.
- Don't install this plugin on top of the built-in DayZ server in MeFriendos WindowsGSM. Use one or the other.
