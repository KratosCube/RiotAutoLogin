using System;
using System.Windows.Threading;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SettingsTab == null)
                    return;

                SettingsTab.Selected += (_, _) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureClientAlertSettingsCard();
                        UpdateClientAlertSettingsUi();
                    }), DispatcherPriority.Loaded);
                };
            }), DispatcherPriority.Loaded);
        }
    }
}
