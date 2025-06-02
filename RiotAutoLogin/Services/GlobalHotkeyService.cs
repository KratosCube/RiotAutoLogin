using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RiotAutoLogin.Services
{
    public class GlobalHotkeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly Window _window;
        private HwndSource? _source;
        private int _hotkeyId = 1;

        public event Action? HotkeyPressed;

        // Modifiers:
        public const uint MOD_NONE = 0x0000; //(none)
        public const uint MOD_ALT = 0x0001; //ALT
        public const uint MOD_CONTROL = 0x0002; //CTRL
        public const uint MOD_SHIFT = 0x0004; //SHIFT
        public const uint MOD_WIN = 0x0008; //WINDOWS

        // Virtual Keys (subset, add more as needed from https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes)
        public const uint VK_F1 = 0x70;
        public const uint VK_F2 = 0x71;
        // ... add other F keys
        public const uint VK_L = 0x4C; // L key
        public const uint VK_Q = 0x51; // Q key

        // Add more virtual key codes as needed, especially for 0-9
        public const uint VK_0 = 0x30;
        public const uint VK_1 = 0x31;
        public const uint VK_2 = 0x32;
        public const uint VK_3 = 0x33;
        public const uint VK_4 = 0x34;
        public const uint VK_5 = 0x35;
        public const uint VK_6 = 0x36;
        public const uint VK_7 = 0x37;
        public const uint VK_8 = 0x38;
        public const uint VK_9 = 0x39;

        private IntPtr _windowHandle;

        public GlobalHotkeyService(System.Windows.Window window)
        {
            _window = window;
            _windowHandle = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);
        }

        public bool Register(uint modifier, uint virtualKey)
        {
            try
            {
                return RegisterHotKey(_windowHandle, _hotkeyId, modifier, virtualKey);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register hotkey: {ex.Message}");
                return false;
            }
        }

        public void Unregister()
        {
            try
            {
                UnregisterHotKey(_windowHandle, _hotkeyId);
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Failed to unregister hotkey: {ex.Message}");
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
            {
                HotkeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _source?.RemoveHook(HwndHook);
            _source?.Dispose();
            Unregister();
            GC.SuppressFinalize(this);
        }

        // Helper method to convert a character to its virtual key code
        public static uint GetVirtualKeyCode(char keyChar)
        {
            keyChar = char.ToUpper(keyChar);
            if (keyChar >= 'A' && keyChar <= 'Z')
            {
                return (uint)keyChar;
            }
            if (keyChar >= '0' && keyChar <= '9')
            {
                return (uint)keyChar;
            }
            // Add more mappings if needed (e.g., for F keys, numpad, etc.)
            // For simplicity, this example only handles A-Z and 0-9 directly.
            // You might need a more robust solution for other keys, potentially P/Invoking VkKeyScan or similar.
            
            // Fallback for common keys already defined
            switch (keyChar)
            {
                case 'L': return VK_L;
                case 'Q': return VK_Q;
                // Add other specific character mappings here if they don't fit A-Z, 0-9
            }
            
            Debug.WriteLine($"Warning: Could not map character '{keyChar}' to a known Virtual Key Code. Defaulting to 0.");
            return 0; // Or throw an exception, or return a default like VK_L
        }

        // Helper method to convert a virtual key code back to its character representation (simplified)
        public static string GetKeyFromVirtualCode(uint virtualKey)
        {
            if (virtualKey >= 0x41 && virtualKey <= 0x5A) // A-Z
            {
                return ((char)virtualKey).ToString();
            }
            if (virtualKey >= 0x30 && virtualKey <= 0x39) // 0-9
            {
                return ((char)virtualKey).ToString();
            }
            // Add specific mappings for other VK codes if needed
            switch (virtualKey)
            {
                case VK_L: return "L";
                case VK_Q: return "Q";
                // Map other specific VK_ codes back to their string representation
            }
            Debug.WriteLine($"Warning: Could not map Virtual Key Code {virtualKey:X} to a character. Returning empty string.");
            return ""; // Or a default like "L"
        }
    }
} 