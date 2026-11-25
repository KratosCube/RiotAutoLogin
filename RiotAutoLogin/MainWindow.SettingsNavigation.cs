using System.Windows;
using System.Windows.Controls;

namespace RiotAutoLogin;

public partial class MainWindow
{
    private void SettingsCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == settingsNav) UpdateSettingsCategory();
    }

    private void UpdateSettingsCategory()
    {
        if (settingsGeneral == null || settingsUpdates == null) return;
        FrameworkElement[] pages = [settingsGeneral, settingsGameplay, settingsPhone, settingsAlerts, settingsData, settingsUpdates];
        for (int i = 0; i < pages.Length; i++) pages[i].Visibility = settingsNav.SelectedIndex == i ? Visibility.Visible : Visibility.Collapsed;
        settingsScroll.ScrollToTop();
        if (settingsNav.SelectedIndex == 3)
        {
            EnsureClientAlertSettingsCard();
            UpdateClientAlertSettingsUi();
        }
    }
}
