using RiotAutoLogin.Services;

namespace RiotAutoLogin.Models
{
    public class HotkeySettings
    {
        public uint Modifier { get; set; } = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
        public uint VirtualKey { get; set; } = GlobalHotkeyService.VK_L;
        public string DisplayName { get; set; } = "Ctrl + Alt + L";
        public bool RunOnStartup { get; set; } = false;

        // New: persisted auto-accept preference
        public bool AutoAcceptEnabled { get; set; } = false;

        public bool GameStartAlertEnabled { get; set; } = false;
        public bool FlashSlotWarningEnabled { get; set; } = false;
        public int PreferredFlashSlot { get; set; } = 2;
    }
}