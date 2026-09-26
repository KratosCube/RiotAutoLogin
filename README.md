# 🎮 RiotAutoLogin v1.4.0

[![Version](https://img.shields.io/badge/version-1.4.0-blue.svg)](https://github.com/KratosCube/RiotAutoLogin/releases)
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
- **Per-Alert Sounds:** Assign a different MP4 or audio file to each alert
- **Flash Warning:** Warns when Flash is on the opposite preferred spell slot

### 📊 **Queue Statistics & Waiting Time**
- **Queue switch:** Solo/Duo, Ranked 5s (the separate weekend ladder), and Flex have their own rank, LP, wins and losses. One refresh retrieves all three; switching uses saved data immediately.
- **Sliding statistics:** Use the arrows beside the bottom metrics to slide between ranked totals and Queue / Champ select / Loading / Total waiting / In game. No extra tab or taller footer.
- **Today or all time:** On the time page, click Today / All time. Totals cover the selected ranked queue across accounts on this PC.
- **Local history:** Time is recorded while this app runs, including in the tray. It starts with this version's observations, cannot be backfilled from match history, and excludes sleep, client outages and time with the app closed.
- **Loading detection:** LCU game phases are combined with the Live Client game clock, so a mid-game attach or reconnect is not counted as an initial loading screen. Loading is saved once the game clock confirms the start; delayed clock reads subtract gameplay already elapsed. Unconfirmed loading spans are discarded on outages or shutdown. Queue time includes ready checks and time from dodged drafts is retained.
- **Greyscreen:** The selected queue's recent client history is shown separately from ranked-period W/L. A `~` marks estimates where Riot omits death duration; the tooltip explains the sample and estimate.

Ranked queues map to `RANKED_SOLO_5x5` / 420, `RANKED_TEAM_5x5` / 710 and `RANKED_FLEX_SR` / 440. Ranked 5s is separate from Clash / 700 and the retired team queue / 42. The client's queue type takes priority when present. See [Riot's Ranked 5s announcement](https://www.leagueoflegends.com/en-us/news/dev/dev-the-return-of-ranked-5s/) and [current queue mapping in Scout](https://github.com/shepherdjerred/monorepo/blob/main/packages/scout-for-lol/packages/data/src/model/core/state.ts).

Performance changes reuse local HTTP connections and per-process authentication, coalesce short gameflow reads, cache Data Dragon versions, bound concurrent rank updates, and avoid recreating account cards for value-only updates. Greyscreen refresh uses one match-history response rather than fetching each match separately.

Run the dependency-free behavioral checks with `dotnet run --project RiotAutoLogin.Tests/RiotAutoLogin.Tests.csproj`. The Windows CI workflow also compiles the WPF app. Live Riot-client timing and animation still need a Windows smoke test.
