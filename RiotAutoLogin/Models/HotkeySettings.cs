using RiotAutoLogin.Services;

namespace RiotAutoLogin.Models
{
    public class HotkeySettings
    {
        public uint Modifier { get; set; } = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
        public uint VirtualKey { get; set; } = GlobalHotkeyService.VK_L; // Default to L
        public string DisplayName { get; set; } = "Ctrl + Alt + L"; // For UI display
        public bool RunOnStartup { get; set; } = false; // Default to false
    }
} 