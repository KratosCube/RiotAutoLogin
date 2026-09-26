using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private RankedQueue _selectedStatsQueue;
        private WaitingTimeMonitor? _waitingTimeMonitor;
        private DispatcherTimer? _waitingStatsTimer;
        private bool _showWaitingStats;
        private bool _statisticsAnimating;
        private bool _todayOnly = true;

        public RankedQueue SelectedStatsQueue
        {
            get => _selectedStatsQueue;
            set
            {
                if (_selectedStatsQueue == value || !Enum.IsDefined(value)) return;
                _selectedStatsQueue = value;
                _hotkeySettings.SelectedStatsQueue = value;
                OnPropertyChanged(nameof(SelectedStatsQueue));
                RefreshAccountLists();
                UpdateTotalGameStats();
                RefreshWaitingStats();
                if (_startupInitialized) SaveHotkeySettings();
            }
        }

        private void InitializeStatistics()
        {
            _selectedStatsQueue = Enum.IsDefined(_hotkeySettings.SelectedStatsQueue) ? _hotkeySettings.SelectedStatsQueue : RankedQueue.SoloDuo;
            OnPropertyChanged(nameof(SelectedStatsQueue));
        }

        private void StartWaitingTimeTracking()
        {
            _waitingTimeMonitor = new WaitingTimeMonitor();
            _waitingTimeMonitor.Start();
            _waitingStatsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _waitingStatsTimer.Tick += (_, _) => RefreshWaitingStats();
            IsVisibleChanged += (_, _) => UpdateWaitingStatsTimer();
            UpdateWaitingStatsTimer();
        }

        private void UpdateWaitingStatsTimer()
        {
            if (_showWaitingStats && IsVisible)
            {
                RefreshWaitingStats();
                _waitingStatsTimer?.Start();
            }
            else _waitingStatsTimer?.Stop();
        }

        private void PreviousStatistics_Click(object sender, RoutedEventArgs e) => SlideStatistics(-1);
        private void NextStatistics_Click(object sender, RoutedEventArgs e) => SlideStatistics(1);

        private void SlideStatistics(int direction)
        {
            if (_statisticsAnimating || statsViewport.ActualWidth <= 0) return;
            _statisticsAnimating = true;
            FrameworkElement outgoing = _showWaitingStats ? waitingStatsPage : rankStatsPage;
            FrameworkElement incoming = _showWaitingStats ? rankStatsPage : waitingStatsPage;
            double width = statsViewport.ActualWidth;
            _showWaitingStats = !_showWaitingStats;
            incoming.Visibility = Visibility.Visible;
            txtStatsPage.Text = _showWaitingStats ? "○ ●" : "● ○";
            btnRefreshQuickStats.Visibility = _showWaitingStats ? Visibility.Collapsed : Visibility.Visible;
            btnStatsScope.Visibility = _showWaitingStats ? Visibility.Visible : Visibility.Collapsed;
            UpdateWaitingStatsTimer();

            var from = new TranslateTransform();
            var to = new TranslateTransform(direction * width, 0);
            outgoing.RenderTransform = from;
            incoming.RenderTransform = to;
            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            from.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -direction * width, duration) { EasingFunction = ease });
            var enter = new DoubleAnimation(direction * width, 0, duration) { EasingFunction = ease };
            enter.Completed += (_, _) =>
            {
                outgoing.Visibility = Visibility.Collapsed;
                from.BeginAnimation(TranslateTransform.XProperty, null);
                to.BeginAnimation(TranslateTransform.XProperty, null);
                to.X = 0;
                _statisticsAnimating = false;
            };
            to.BeginAnimation(TranslateTransform.XProperty, enter);
        }

        private void StatisticsScope_Click(object sender, RoutedEventArgs e)
        {
            _todayOnly = !_todayOnly;
            btnStatsScope.Content = _todayOnly ? "Today" : "All time";
            RefreshWaitingStats();
        }

        private void RefreshWaitingStats()
        {
            if (txtQueueTimeValue == null) return;
            var totals = _waitingTimeMonitor?.GetTotals(SelectedStatsQueue, _todayOnly) ?? default;
            txtQueueTimeValue.Text = FormatTrackedTime(totals.QueueSeconds);
            txtChampSelectTimeValue.Text = FormatTrackedTime(totals.ChampSelectSeconds);
            txtLoadingTimeValue.Text = FormatTrackedTime(totals.LoadingSeconds);
            txtWaitingTimeValue.Text = FormatTrackedTime(totals.WaitingSeconds);
            txtInGameTimeValue.Text = FormatTrackedTime(totals.InGameSeconds);
            string period = _todayOnly ? "Today" : "All recorded time";
            string since = _waitingTimeMonitor?.FirstRecordedUtc?.ToLocalTime().ToString("g") ?? "your next game";
            waitingStatsPage.ToolTip = $"{period} · selected ranked queue · all accounts\n" +
                $"{_waitingTimeMonitor?.Status ?? "Starting tracking…"}\n" +
                $"Recorded on this PC since {since}. Includes time in the tray; excludes sleep and time with the app closed.\n" +
                "Queue includes ready checks. Loading is saved after the game clock confirms the start. Historical waiting time cannot be imported.";
            if (_waitingTimeMonitor?.PersistenceError is { } error) waitingStatsPage.ToolTip += "\n" + error;
        }

        private static string FormatTrackedTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (time.TotalHours >= 1) return $"{(long)time.TotalHours}h {time.Minutes:00}m";
            if (time.TotalMinutes >= 1) return $"{time.Minutes}m {time.Seconds:00}s";
            return $"{time.Seconds}s";
        }
    }
}
