using System;
using System.Windows.Threading;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            Dispatcher.BeginInvoke(new Action(StartClientAlertsSettingsRetryTimer), DispatcherPriority.Loaded);
        }

        private void StartClientAlertsSettingsRetryTimer()
        {
            int attempts = 0;
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            timer.Tick += (_, _) =>
            {
                attempts++;

                EnsureClientAlertSettingsCard();
                UpdateClientAlertSettingsUi();

                if (attempts >= 30)
                    timer.Stop();
            };

            timer.Start();
        }
    }
}
