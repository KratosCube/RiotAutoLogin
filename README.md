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
- **Remote Pick:** Enable the phone page in Settings and open its LAN link on the same Wi-Fi. From an eligible lobby, start matchmaking (or a custom game) and accept a found match. Queue time comes from the client. In draft, tap a champion to set your hover, then use the named Lock in or Ban action to confirm. A confirmed ban preview in a timed turn is locked near the end if you have not banned manually. Use the bottom Champions, Picks and Loadout tabs in either phone orientation. Visible teammates link to League of Graphs in Picks. The position selector filters Top / Jungle / Mid / Bot / Support using the client's recommended champion positions. Multi-position champions appear in every matching filter. Unlisted shows champions missing position data; All roles always keeps the full roster available. If position recommendations are unavailable, the filter is disabled without restricting the grid.
- **Per-Alert Sounds:** Assign a different MP4 or audio file to each alert
- **Flash Warning:** Warns when Flash is on the opposite preferred spell slot

### 📊 **Queue Statistics & Waiting Time**
- The queue switch shows the selected ladder's rank emblem, LP, wins and losses on each Quick Login card. Cards and the shortcut popup include a win-rate bar and percentage. An unknown record or zero games shows a neutral bar. The popup also shows the rank emblem and selected queue. Two full rows fit at the minimum window size. Additional accounts scroll above the statistics panel. [Preview with eight sample accounts](images/login_tab.png) · [Shortcut popup preview](images/quick_login.png)
- **Opponent average:** Cards and the shortcut popup show the average visible rank of opponents from up to ten completed games in the selected queue. An open League client in the account's region is preferred. When it is closed, in another region or unavailable, the app uses the Riot API key saved in Settings for match history and current enemy ranks. EUW and EUNE accounts can therefore refresh in the same batch. The client searches its latest 100 history entries; the API searches its latest 100 entries in the selected queue. Remakes and unranked opponents are excluded. Tooltips show ranked-opponent coverage, source, refresh time and any recovery action needed. Saved summaries survive restart and service failures. Completed API matches are cached for the session; enemy ranks are cached for 15 minutes. API requests share a budget with own-rank refresh and respect server cooldowns, so large batches may take a few minutes. Saving a new API key starts a fresh background refresh. Development keys expire after 24 hours. Master+ uses its shared LP ladder; Riot's hidden MMR remains private. [Riot API keys and limits](https://developer.riotgames.com/docs/portal) · [Riot's explanation of MMR and visible rank](https://support.riotgames.com/en-us/league-of-legends/gameplay/mmr-rank-and-lp)
- **Statistics panel:** Direct Results / Farming / Time controls replace the small arrow navigation. Results show current ranked-period totals. Time shows locally recorded waiting and gameplay. Farming covers all accounts and queues since January 1 of the current calendar year.
- **Annual CS:** In Farming, choose Sync history with a valid Riot API key. Lane minions and jungle monsters count together from January 1 in the PC's local time zone. The counter switches to the new calendar year automatically, including when Farming stays open across midnight. Match history is stored separately and loaded only when Farming opens to keep startup lightweight. Available matches are imported in the background with saved checkpoints; Stop sync pauses the import and the next sync resumes. Duplicate games and duplicate saved Riot IDs do not inflate totals. The + suffix marks an incomplete, stale or partially unavailable import. Riot may not retain every game from the year, so even a finished scan covers available history. CS/min uses imported CS divided by total imported game duration.
- **Today or all time:** On the time page, click Today / All time. Totals cover the selected ranked queue across accounts on this PC.
- **Local history:** Time is recorded while this app runs, including in the tray. It starts with this version's observations, cannot be backfilled from match history, and excludes sleep, client outages and time with the app closed.
- **Loading detection:** LCU game phases are combined with the Live Client game clock, so a mid-game attach or reconnect is not counted as an initial loading screen. Loading is saved once the game clock confirms the start; delayed clock reads subtract gameplay already elapsed. Unconfirmed loading spans are discarded on outages or shutdown. Queue time includes ready checks and time from dodged drafts is retained.
- **Greyscreen:** The selected queue's recent client history is shown separately from ranked-period W/L. A `~` marks estimates where Riot omits death duration; the tooltip explains the sample and estimate.

Ranked 5s uses `RANKED_PREMADE_5x5` (710), Solo/Duo uses `RANKED_SOLO_5x5` (420), and Flex uses `RANKED_FLEX_SR` (440). Refresh reads League-V4 with your configured key. If the weekend entry is missing or the public API is unavailable, an open League client on the saved account's region can look up its full Riot ID and supply its ranked statistics. Missing weekend data keeps the last known result. An account with no result shows dashes until a source reports it. [League client ranked schema](https://github.com/KebsCS/lcu-and-riotclient-api/blob/e8c7cf2ad885f6af82dfcad87be9afa5792dae66/lcu/swagger.json#L63666) · [Riot's Ranked 5s announcement](https://www.leagueoflegends.com/en-us/news/dev/dev-the-return-of-ranked-5s/)

The ten small rank emblems are bundled locally from Riot Games' ranked artwork. [Riot Developer Portal assets](https://developer.riotgames.com/docs/lol#ranked-info_icons-and-emblems)

Performance changes reuse local HTTP connections and per-process authentication, coalesce short gameflow reads, cache Data Dragon versions, bound concurrent rank updates, and avoid recreating account cards for value-only updates. Greyscreen refresh uses one match-history response rather than fetching each match separately.

Windows previews with sample data: [Results](images/login_tab.png) · [Annual farming](images/farming.png) · [Accounts editor](images/accounts_editor.png) · [Update dialog](images/update_dialog.png).

Run the dependency-free behavioral checks with `dotnet run --project RiotAutoLogin.Tests/RiotAutoLogin.Tests.csproj`. The Windows CI workflow also compiles the WPF app. Live Riot-client timing and animation still need a Windows smoke test.


## Smaller updates and release notifications

In **Settings → Persistent Settings**, the app finds the League of Legends `Config` folder from a running client or a standard installation. You can choose the folder yourself for a custom installation. The toggle marks existing `PersistedSettings.json`, `game.cfg` and `input.ini` files read-only, without changing their contents or creating missing files. Turn it off before editing game settings; the folder and preference are remembered across app restarts. Riot documents these files in its [configuration guidance](https://support.riotgames.com/en-us/league-of-legends/performance/minimum-and-recommended-system-requirements) and [input settings guidance](https://support.riotgames.com/en-us/league-of-legends/performance/mouse-and-keyboard-issues).

For a new installation, download **RiotAutoLogin-Setup.zip**, extract it and run **RiotAutoLogin-Setup.exe**. Setup installs for the current Windows user, including the required .NET runtime. There is no separate runtime installation. Accounts, settings and statistics remain in the existing `%AppData%\\RiotClientAutoLogin` directory.

For an existing standalone EXE, **Update & Restart** downloads to a temporary folder under `%LocalAppData%\RiotClientAutoLogin\Updates`, checks its size and SHA-256 digest when provided, then closes the app and replaces the EXE at the exact path it was launched from. The helper restarts the updated app and deletes the staged download. It does not leave an additional copy in Downloads. If the original directory is not writable, the app reports that before closing; if replacement fails afterwards, it preserves the old EXE, restarts it and reports the error at next launch. Downloading an EXE manually in a browser still uses the browser's Downloads folder.

To use smaller future updates, choose **Settings → Enable smaller updates** separately. This performs a one-time setup, including when the version already matches the release. Use the new Riot Auto Login shortcut afterwards; the old standalone file is left in place because the user explicitly opted into a different install location. An existing Windows startup opt-in is moved to the new application location.

The installed version keeps the previous package and applies binary deltas. Unchanged runtime files do not need to be downloaded again. A missing base, an unavailable/corrupt delta, or a large gap between versions can require a full download. Actual sizes are reported in the release workflow summary. Packages are compressed for transport and extracted during installation, so the installed app does not decompress a giant EXE on every startup.

**RiotAutoLogin-Portable.zip** also supports the updater. Its first update may need a full package to seed the local cache. Keep the entire extracted directory. The versioned standalone `.exe` for older updaters stores its assemblies uncompressed to reduce launch time. This makes that download larger. Delta updates are available after migration.

### Developer previews from a feature branch

After this workflow setup is merged once, open **Actions → Build developer preview → Run workflow**. Enter the feature branch or commit in **source** and write the features to test. The workflow runs checks, builds a self-contained portable app and publishes a `dev-…` GitHub prerelease with `RiotAutoLogin-Developer.zip`. Future feature branches do not need to be merged to produce a test build. Before the initial setup is merged, PR Build checks also retain a developer-preview artifact with the same portable ZIP for testing. The ZIP includes the exact source commit in BUILD.txt.

Extract the whole archive and run RiotAutoLogin.exe. The title is marked DEV. Test builds use `%AppData%\RiotClientAutoLogin-Dev` and a separate startup registry entry; they start with their own empty accounts/settings. Exit the stable app before testing client automation or the shared default hotkey. Developer builds are updated by downloading another developer ZIP. They do not migrate through the public updater. Public clients ignore prereleases and the development workflow never marks its release as Latest.

After testing and refining the feature, merge the desired code and publish a normal stable release with the existing release workflow below. A developer ZIP is not automatically promoted to a public update.

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
