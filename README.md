# 🎮 RiotAutoLogin v1.4.0

[![Version](https://img.shields.io/badge/version-1.5.0-blue.svg)](https://github.com/KratosCube/RiotAutoLogin/releases)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A modern, feature-rich application for automating Riot Client logins with secure multi-account management, live stats integration, and automatic updates.

## ✨ Key Features

### 🚀 **Auto-Login System**
- **Quick Login Popup:** Lightning-fast account switching with ESC to close
- **Optimized Performance:** Login times reduced to ~2.5 seconds
- **Smart UI Detection:** Reliable credential filling using UI automation
- **Tray Recovery:** Restores a hidden Riot Client window before signing in

### 📱 **Enhanced User Interface**
- **Modern Dark Theme:** Sleek, eye-friendly design
- **System Tray Integration:** Minimize to tray with custom icon
- **Responsive Design:** Dynamic scaling for different screen sizes
- **Smooth Animations:** Polished hover effects and transitions

### 🔄 **Auto-Update System** *(New in v1.1.0)*
- **GitHub Integration:** Automatic update detection from releases
- **One-Click Updates:** Download and install with progress tracking
- **Version Management:** Smart version comparison and notifications
- **Startup Checks:** Detects releases in the background when the app starts
- **Notification Choice:** Release prompts can be disabled from the prompt or Settings
- **Quiet Releases:** Minor updates remain available through manual checks without startup prompts
- **Smaller Downloads:** Installed versions use compressed packages and binary deltas when possible

### ⚡ **Global Hotkeys** *(New in v1.1.0)*
- **Quick Access:** Customizable keyboard shortcuts
- **System-Wide:** Works even when app is minimized
- **Easy Configuration:** Set your preferred hotkey combinations

### 🔐 **Advanced Security**
- **DPAPI Encryption:** Windows Data Protection API with entropy
- **Local Storage:** All data stays on your machine
- **Secure Memory:** Protected credential handling

### 🎯 **Champion Select Automation**
- **Auto-Pick:** Automatically select your preferred champions
- **Pick-Turn Alert:** Notifies you when your champion pick becomes active
- **Remote Pick:** Enable the phone page in Settings and open its LAN link on the same Wi-Fi. From an eligible lobby, start matchmaking (or a custom game) and accept a found match. Queue time comes from the client. In draft, tap a champion to set your hover, then use the named Lock in or Ban action to confirm. A confirmed ban preview in a timed turn is locked near the end if you have not banned manually. Use the bottom Champions, Picks and Loadout tabs in either phone orientation. Visible teammates link to League of Graphs in Picks.
- **Per-Alert Sounds:** Assign a different MP4 or audio file to each alert
- **Flash Warning:** Warns when Flash is on the opposite preferred spell slot

### 📊 **Queue Statistics & Waiting Time**
- The queue switch shows the selected ladder's rank emblem, LP, wins and losses on each Quick Login card. Cards and the shortcut popup include a win-rate bar and percentage. An unknown record or zero games shows a neutral bar. The popup also shows the rank emblem and selected queue. Two full rows fit at the minimum window size. Additional accounts scroll above the statistics panel. [Preview with eight sample accounts](images/login_tab.png)
- **Sliding statistics:** Use the arrows beside the bottom metrics to slide between ranked totals and Queue / Champ select / Loading / Total waiting / In game. No extra tab or taller footer.
- **Today or all time:** On the time page, click Today / All time. Totals cover the selected ranked queue across accounts on this PC.
- **Local history:** Time is recorded while this app runs, including in the tray. It starts with this version's observations, cannot be backfilled from match history, and excludes sleep, client outages and time with the app closed.
- **Loading detection:** LCU game phases are combined with the Live Client game clock, so a mid-game attach or reconnect is not counted as an initial loading screen. Loading is saved once the game clock confirms the start; delayed clock reads subtract gameplay already elapsed. Unconfirmed loading spans are discarded on outages or shutdown. Queue time includes ready checks and time from dodged drafts is retained.
- **Greyscreen:** The selected queue's recent client history is shown separately from ranked-period W/L. A `~` marks estimates where Riot omits death duration; the tooltip explains the sample and estimate.

Ranked 5s uses `RANKED_PREMADE_5x5` (710), Solo/Duo uses `RANKED_SOLO_5x5` (420), and Flex uses `RANKED_FLEX_SR` (440). Refresh reads League-V4 with your configured key. If the weekend entry is missing or the public API is unavailable, an open League client on the saved account's region can look up its full Riot ID and supply its ranked statistics. Missing weekend data keeps the last known result. An account with no result shows dashes until a source reports it. [League client ranked schema](https://github.com/KebsCS/lcu-and-riotclient-api/blob/e8c7cf2ad885f6af82dfcad87be9afa5792dae66/lcu/swagger.json#L63666) · [Riot's Ranked 5s announcement](https://www.leagueoflegends.com/en-us/news/dev/dev-the-return-of-ranked-5s/)

The ten small rank emblems are bundled locally from Riot Games' ranked artwork. [Riot Developer Portal assets](https://developer.riotgames.com/docs/lol#ranked-info_icons-and-emblems)

Performance changes reuse local HTTP connections and per-process authentication, coalesce short gameflow reads, cache Data Dragon versions, bound concurrent rank updates, and avoid recreating account cards for value-only updates. Greyscreen refresh uses one match-history response rather than fetching each match separately.

Run the dependency-free behavioral checks with `dotnet run --project RiotAutoLogin.Tests/RiotAutoLogin.Tests.csproj`. The Windows CI workflow also compiles the WPF app. Live Riot-client timing and animation still need a Windows smoke test.


## Smaller updates and release notifications

In **Settings → Persistent Settings**, the app finds the League of Legends `Config` folder from a running client or a standard installation. You can choose the folder yourself for a custom installation. The toggle marks existing `PersistedSettings.json`, `game.cfg` and `input.ini` files read-only, without changing their contents or creating missing files. Turn it off before editing game settings; the folder and preference are remembered across app restarts. Riot documents these files in its [configuration guidance](https://support.riotgames.com/en-us/league-of-legends/performance/minimum-and-recommended-system-requirements) and [input settings guidance](https://support.riotgames.com/en-us/league-of-legends/performance/mouse-and-keyboard-issues).

For a new installation, download **RiotAutoLogin-Setup.zip**, extract it and run **RiotAutoLogin-Setup.exe**. Setup installs for the current Windows user, including the required .NET runtime. There is no separate runtime installation. Accounts, settings and statistics remain in the existing `%AppData%\\RiotClientAutoLogin` directory.

For an existing standalone EXE, **Update & Restart** downloads to a temporary folder under `%LocalAppData%\RiotClientAutoLogin\Updates`, checks its size and SHA-256 digest when provided, then closes the app and replaces the EXE at the exact path it was launched from. The helper restarts the updated app and deletes the staged download. It does not leave an additional copy in Downloads. If the original directory is not writable, the app reports that before closing; if replacement fails afterwards, it preserves the old EXE, restarts it and reports the error at next launch. Downloading an EXE manually in a browser still uses the browser's Downloads folder.

To use smaller future updates, choose **Settings → Enable smaller updates** separately. This performs a one-time setup, including when the version already matches the release. Use the new Riot Auto Login shortcut afterwards; the old standalone file is left in place because the user explicitly opted into a different install location. An existing Windows startup opt-in is moved to the new application location.

The installed version keeps the previous package and applies binary deltas. Unchanged runtime files do not need to be downloaded again. A missing base, an unavailable/corrupt delta, or a large gap between versions can require a full download. Actual sizes are reported in the release workflow summary. Packages are compressed for transport and extracted during installation, so the installed app does not decompress a giant EXE on every startup.

**RiotAutoLogin-Portable.zip** also supports the updater. Its first update may need a full package to seed the local cache. Keep the entire extracted directory. The versioned standalone `.exe` for older updaters stores its assemblies uncompressed to reduce launch time. This makes that download larger. Delta updates are available after migration.

### Publishing an announced or quiet release

1. Merge the changes to `master`.
2. Open **Actions → Build release packages → Run workflow**.
3. Select `master`, enter a new stable tag such as `v1.5.0`, and set **Show an update notification to users**:
   - checked: show a startup prompt once for this version;
   - unchecked: publish a quiet maintenance release, available through **Check for Updates**.
4. Run the workflow. It builds the exact existing tag, or creates the new tag from the selected branch after all packages are ready.

Quiet means **no automatic prompt**, not automatic installation. Downloads and installation still require the user's actions. Disabling release notifications in Settings suppresses all startup prompts; manual checks always work.
Startup checks run at most once per day by default. Manual checks bypass that interval.

For tag pushes, `release-policy.json` supplies the default. For releases created through GitHub's release editor, this marker in the release description overrides that default:

```html
<!-- riotautologin:notify=false -->
```

Use `true` for an announced release, or omit the marker to use the configured default. The marker is hidden in the app's changelog. Quiet releases are also marked as not-latest on GitHub to help older clients that only query `/releases/latest`. The per-release prompt policy itself requires this updater version.

The workflow downloads the previous stable package to generate a delta, publishes a full fallback and the new delta, and uploads the update feed last. Do not delete old full/delta assets if you want users who skip versions to retain smaller updates. The first packaged release cannot have a delta.

Setup is deliberately wrapped in a ZIP: old versions select the first `.exe` release asset and would otherwise overwrite themselves with the installer. Do not upload a separate Setup.exe alongside the compatibility EXE.

### Validating the updater

CI builds two real Windows packages, verifies that the delta reconstructs the full payload without downloading the full package, and checks damaged-delta fallback, missing-base fallback, cached-download reuse and cancellation. It also exercises notification selection, version ordering, same-version migration, checksums and safe installer extraction.

CI also checks the in-place EXE replacement, cleanup and failure recovery on Windows. Before shipping, smoke-test the one-time migration, Windows startup and **Update & Restart** in the app. CI does not exercise an interactive installation or a running Riot Client.
