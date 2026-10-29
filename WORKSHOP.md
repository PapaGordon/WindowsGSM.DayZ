# DayZ and Arma 3 Workshop updates

Made by PapaGordon

WGSM v1.25.4.4 can check and download configured Workshop mods before each DayZ or Arma 3 start, including scheduled restarts. Steam subscriptions alone do not configure a dedicated server. This is separate from the **Update on start** setting for the server executable.

## Setup

1. Stop the server and keep a backup.
2. Set up the Steam account in WGSM. For encrypted passwords, follow [Steam credentials](https://github.com/PapaGordon/WindowsGSM/blob/main/STEAM-CREDENTIALS.md). The account needs access to the requested Workshop items. Steam Guard or missing access can stop unattended downloads.
3. Create `servers/<server ID>/configs/WorkshopMods.cfg` beside that server's other WGSM configuration files.
4. Add one individual Workshop item per line, in dependency/load order:

```text
# Replace these example IDs and folder names with your own mods.
123456789|@Dependency|mod
234567891|@Gameplay|mod
345678912|@AdminTools|servermod
```

The format is `item ID|@target folder|mod` or `item ID|@target folder|servermod`. Use the numeric `id=` from the mod's Workshop URL, not a collection ID. Folder names may contain letters, numbers, underscores and hyphens after `@`. Entries are merged into the existing `-mod` / `-serverMod` arguments. Manual entries and DayZActivatedMods.cfg remain supported; avoid specifying the same mod through two different paths.

Without this file, or with only comments, nothing is downloaded and the existing start behavior stays in place. Removing an entry stops managing that folder; it does not uninstall the mod or remove its manually configured startup argument.

## What gets replaced

Each listed target folder is managed as a complete Workshop copy. Its previous contents, including files removed by the mod author, are replaced. Keep persistent mod settings outside these folders, as the mod's own instructions require. **Unlisted mod folders and manual signing keys are kept.**

All listed downloads must finish successfully before activation. SteamCMD's exit code and a success receipt for every item are checked. Content is staged before any live mod folder is replaced. A failed activation restores previous folders and keys; a failed download cancels the start. Downloads have a 30-minute timeout and do not open an interactive Steam login window. Workshop jobs sharing the SteamCMD directory are serialized by a file lock; if another job holds it, this start fails and can be retried after that job finishes. Avoid concurrent manual SteamCMD operations in the same installation.

Included `.bikey` files are installed in `serverfiles/keys` with `wgsm_<game app ID>_<item ID>_` prefixes. These owned keys are tracked with SHA-256 hashes and rotated on subsequent updates. Modified owned keys cause a conflict instead of being overwritten. Existing unprefixed keys are retained; remove any obsolete manually installed keys yourself after checking which mods use them. Signature verification settings are never disabled.

DayZ downloads use Workshop app **221100**, Arma 3 uses **107410**. The dedicated server executable still uses its normal server app ID. No account subscription changes are made, collections are not expanded, and mod-specific installation scripts are not run.

## Backups and recovery

Previous files remain under `serverfiles/.wgsm-workshop-backups/<timestamp-id>/previous`. The `prepared` folder contains staged files that have not been activated. Backups are kept so you can decide when to remove them after checking the server.

If WGSM or the machine stops during activation, the next start refuses to proceed while `configs/workshop-state-<game app ID>/pending.txt` exists. It lists the transaction directory and destination paths in order. Numbered files/folders in `previous` correspond to that list starting at zero. While the server is stopped, restore the available previous entries to their listed destinations and check every destination before removing `pending.txt`. Entries without a previous copy may have been newly installed. Keep the whole transaction until recovery is confirmed. Do not delete the marker just to bypass recovery.

The standalone [DayZ](https://github.com/PapaGordon/WindowsGSM.DayZ) and [Arma 3](https://github.com/PapaGordon/WindowsGSM.ARMA3) plugins include the same updater. The SteamCMD credential format still depends on the host: MeFriendos WGSM v1.25.4.2 or newer understands its encrypted field; another fork needs its own support. Do not install these plugins on top of their built-in MeFriendos entries.

Checks use isolated download/process fixtures, synthetic encrypted credentials and filesystem rollback cases. Real Steam authentication, Steam Guard, downloads from an account and multiplayer joins still need checking on the target server.
