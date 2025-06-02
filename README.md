# 🎮 RiotAutoLogin v1.1.0

[![Version](https://img.shields.io/badge/version-1.1.0-blue.svg)](https://github.com/KratosCube/RiotAutoLogin/releases)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A modern, feature-rich application for automating Riot Client logins with secure multi-account management, live stats integration, and automatic updates.

## ✨ Key Features

### 🚀 **Auto-Login System**
- **Quick Login Popup:** Lightning-fast account switching with ESC to close
- **Optimized Performance:** Login times reduced to ~2.5 seconds
- **Smart UI Detection:** Reliable credential filling using UI automation

### 📱 **Enhanced User Interface**
- **Modern Dark Theme:** Sleek, eye-friendly design
- **System Tray Integration:** Minimize to tray with custom icon
- **Responsive Design:** Dynamic scaling for different screen sizes
- **Smooth Animations:** Polished hover effects and transitions

### 🔄 **Auto-Update System** *(New in v1.1.0)*
- **GitHub Integration:** Automatic update detection from releases
- **One-Click Updates:** Download and install with progress tracking
- **Version Management:** Smart version comparison and notifications
- **Background Checking:** Configurable update intervals

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
- **Auto-Ban:** Ban unwanted champions instantly
- **Summoner Spells:** Auto-select your spell combinations
- **Queue Integration:** Smart detection of champion select phase

### 📊 **Live Stats Integration**
- **Real-time Rank Data:** Fetch current rank and LP from Riot API
- **Multiple Regions:** Support for all Riot regions
- **Account Verification:** Validate account details automatically
- **Stats Display:** Beautiful rank cards with current season data

## 🚀 Installation

### Option 1: Download Release (Recommended)
1. Go to [Releases](https://github.com/KratosCube/RiotAutoLogin/releases)
2. Download `RiotAutoLogin-v1.1.0-win-x86.exe` (requires .NET 8.0)
3. Run the executable - no installation needed!

### Option 2: Standalone Version
1. Download `RiotAutoLogin-v1.1.0-win-x86.zip`
2. Extract and run - includes all dependencies

### Requirements
- **OS:** Windows 10/11
- **.NET:** 8.0 Runtime (auto-installed with standalone version)
- **RAM:** 100MB minimum
- **Storage:** 50MB available space

## 📋 How to Use

### 🔑 **Quick Login**
1. **Add Accounts:** Go to "Manage Accounts" tab
2. **Fill Details:** Enter username, password, and optional game details
3. **Quick Access:** Double-click any account card for instant login
4. **Popup Mode:** Use quick login popup for fastest switching

### ⚙️ **Configuration**

#### 🔥 **Riot API Setup** (Optional but Recommended)
1. Visit [Riot Developer Portal](https://developer.riotgames.com/)
2. Register for a Personal API Key (free)
3. Enter API key in Settings → API Configuration
4. Enjoy live stats and rank display!

#### ⌨️ **Hotkey Setup**
1. Go to Settings → Hotkeys
2. Set your preferred key combinations
3. Enable global hotkeys for system-wide access

#### 🎮 **Auto-Pick Configuration**
1. Open Settings → Auto-Pick
2. Select primary/secondary champions
3. Choose ban champion and summoner spells
4. Enable desired automation features

### 🎯 **Champion Select Features**
- **Auto-Pick:** Automatically locks your selected champion
- **Auto-Ban:** Bans your specified champion during ban phase
- **Spell Selection:** Auto-selects your preferred summoner spells
- **Backup Picks:** Falls back to secondary champion if primary unavailable

## 🔧 Advanced Features

### 🔄 **Auto-Update System**
- **Automatic Checking:** Checks for updates every 24 hours
- **Manual Check:** Click "Check for Updates" in settings
- **Secure Downloads:** Verified downloads from GitHub releases
- **Smart Installation:** Self-replacing executable with restart

### 📱 **System Tray**
- **Minimize to Tray:** Keep app running in background
- **Quick Access:** Right-click for context menu
- **Custom Icon:** Beautiful tray icon integration

### ⚡ **Performance Optimizations**
- **Fast Startup:** Optimized initialization
- **Memory Efficient:** Minimal resource usage
- **Smart Caching:** Cached champion and spell data
- **Background Operations:** Non-blocking UI operations

## 🛠️ Troubleshooting

### Common Issues

**Login Not Working:**
- Ensure Riot Client is installed and up-to-date
- Check Windows UI Automation is enabled
- Try running as administrator

**API Key Issues:**
- Verify API key is valid and active
- Check internet connection
- Ensure correct region selection

**Update Problems:**
- Check internet connectivity
- Disable antivirus temporarily
- Run as administrator for updates

**Hotkeys Not Working:**
- Check for conflicting applications
- Try different key combinations
- Restart application after hotkey changes

## 🤝 Contributing

We welcome contributions! Here's how you can help:

1. **Fork** the repository
2. **Create** a feature branch (`git checkout -b feature/amazing-feature`)
3. **Commit** your changes (`git commit -m 'Add amazing feature'`)
4. **Push** to the branch (`git push origin feature/amazing-feature`)
5. **Open** a Pull Request

### Development Setup
```bash
git clone https://github.com/KratosCube/RiotAutoLogin.git
cd RiotAutoLogin
dotnet build
```

## 📜 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **Riot Games** for the League of Legends API
- **FlaUI** for UI automation capabilities
- **Community contributors** for feedback and suggestions

## 📞 Support

- **Issues:** [GitHub Issues](https://github.com/KratosCube/RiotAutoLogin/issues)
- **Discussions:** [GitHub Discussions](https://github.com/KratosCube/RiotAutoLogin/discussions)
- **Updates:** Watch this repository for latest releases

---

<div align="center">

**⭐ If you find this project helpful, please give it a star! ⭐**

[Download Latest Release](https://github.com/KratosCube/RiotAutoLogin/releases) • [Report Bug](https://github.com/KratosCube/RiotAutoLogin/issues) • [Request Feature](https://github.com/KratosCube/RiotAutoLogin/issues)

</div>
