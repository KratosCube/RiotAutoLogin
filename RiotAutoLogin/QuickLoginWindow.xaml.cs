using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;
using MouseControl = System.Windows.Forms.Control;

namespace RiotAutoLogin
{
    public partial class QuickLoginWindow : Window
    {
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        public event Action<Account>? AccountSelected;

        public QuickLoginWindow(IReadOnlyList<Account> accounts)
        {
            InitializeComponent();
            AccountList.ItemsSource = accounts;
        }

        public void ShowCenteredOnCursor()
        {
            var workArea = Screen.FromPoint(MouseControl.MousePosition).WorkingArea;
            var handle = new WindowInteropHelper(this).EnsureHandle();

            // Move the hidden HWND to the cursor's monitor before sizing it;
            // WPF can then apply that monitor's DPI. Window.Left/Top and the
            // old Popup offsets are DIPs, while the screen bounds are pixels.
            SetWindowPos(handle, IntPtr.Zero, workArea.Left + 12, workArea.Top + 12,
                0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
            Matrix fromDevice = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Width = Math.Min(460, (workArea.Width - 24) * fromDevice.M11);
            double availableHeight = (workArea.Height - 24) * fromDevice.M22;
            MaxHeight = Math.Max(120, availableHeight);
            AccountScrollViewer.MaxHeight = Math.Max(72, availableHeight - 180);

            Show();
            UpdateLayout();
            if (GetWindowRect(handle, out var bounds))
            {
                var center = ScreenPlacement.Center(workArea.Left, workArea.Top,
                    workArea.Width, workArea.Height, bounds.Width, bounds.Height);
                SetWindowPos(handle, IntPtr.Zero, center.X, center.Y, 0, 0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate);
            }
            Opacity = 1;
            Deactivated += (_, _) => Close();
            Activate();
        }

        private void Account_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: Account account }) return;
            Close();
            AccountSelected?.Invoke(account);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            base.OnPreviewKeyDown(e);
        }
    }
}
