using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RiotAutoLogin.Models;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class UIService
    {
        private static readonly SemaphoreSlim GreyscreenSyncGate = new(1, 1);
        private static DateTime _lastGreyscreenSyncUtc = DateTime.MinValue;

        private static readonly ConditionalWeakTable<ItemsControl, List<Account>> BoundAccounts = new();

        public static void RefreshAccountLists(Window window, List<Account> accounts)
        {
            foreach (string name in new[] { "lbAccounts", "lbLoginAccounts", "icLoginAccounts" })
            {
                if (window.FindName(name) is not ItemsControl control) continue;
                if (BoundAccounts.TryGetValue(control, out var previous) && previous.SequenceEqual(accounts)) continue;
                if (!ReferenceEquals(control.ItemsSource, accounts)) control.ItemsSource = accounts;
                else control.Items.Refresh();
                BoundAccounts.Remove(control);
                BoundAccounts.Add(control, accounts.ToList());
            }
        }

        public static void UpdateTotalGameStats(Window window, System.Collections.Generic.List<Models.Account> accounts, RankedQueue queue = RankedQueue.SoloDuo)
        {
            var (totalGames, totalWins, totalLosses, winRate, totalGreyscreens, totalGreyscreenSeconds) = AccountService.CalculateStats(accounts, queue);
            bool anyRank = accounts.Any(a => a.QueueRanks.ContainsKey(queue));

            if (window.FindName("txtStatsGamesValue") is TextBlock txtStatsGamesValue)
                txtStatsGamesValue.Text = anyRank ? totalGames.ToString() : "—";

            if (window.FindName("txtStatsWinsValue") is TextBlock txtStatsWinsValue)
                txtStatsWinsValue.Text = anyRank ? totalWins.ToString() : "—";

            if (window.FindName("txtStatsLossesValue") is TextBlock txtStatsLossesValue)
                txtStatsLossesValue.Text = anyRank ? totalLosses.ToString() : "—";

            if (window.FindName("txtStatsWinRateValue") is TextBlock txtStatsWinRateValue)
                txtStatsWinRateValue.Text = anyRank ? $"{winRate:F1}%" : "—";

            TextBlock? txtStatsGreyscreensValue = window.FindName("txtStatsGreyscreenTimeValue") as TextBlock;
            if (txtStatsGreyscreensValue != null)
            {
                var data = accounts.Select(a => a.QueueGreyscreens.GetValueOrDefault(queue)).Where(g => g != null).ToArray();
                bool estimated = data.Any(g => g!.Estimated);
                txtStatsGreyscreensValue.Text = data.Length == 0 ? "—" : (estimated ? "~" : "") + FormatGreyscreenDuration(totalGreyscreenSeconds);
                txtStatsGreyscreensValue.ToolTip = $"Recent client history · {data.Sum(g => g!.Games)} games in this queue. " +
                    (estimated ? "Missing death time is estimated at 30 seconds per death." : "Reported time spent dead.");
            }

            if (window.FindName("txtTotalGames") is TextBlock txtTotalGames)
                txtTotalGames.Text = $"Total Games: {totalGames} | Wins: {totalWins} | Losses: {totalLosses} | Win Rate: {winRate:F1}% | Greyscreen Time: {FormatGreyscreenDuration(totalGreyscreenSeconds)} | Deaths: {totalGreyscreens}";
        }

        public static async Task<bool> TrySyncGreyscreenStatsAsync(
            System.Collections.Generic.List<Models.Account> accounts,
            CancellationToken cancellationToken = default)
        {
            if ((DateTime.UtcNow - _lastGreyscreenSyncUtc).TotalSeconds < 5)
                return false;

            if (!await GreyscreenSyncGate.WaitAsync(0, cancellationToken))
                return false;

            try
            {
                LcuGreyscreenStatsResult result = await Task.Run(
                    () => LcuGreyscreenStatsService.GetCurrentAccountGreyscreensAsync(),
                    cancellationToken);

                _lastGreyscreenSyncUtc = DateTime.UtcNow;

                if (!result.Success)
                    return false;

                Models.Account? account = FindSavedAccountForGreyscreenSync(accounts, result);
                if (account == null)
                    return false;

                account.QueueGreyscreens = result.ByQueue;
                account.Greyscreens = result.Greyscreens;
                account.GreyscreenSeconds = result.GreyscreenSeconds;
                account.GreyscreensLastUpdatedUtc = DateTime.UtcNow.ToString("O");
                return true;
            }
            catch
            {
                _lastGreyscreenSyncUtc = DateTime.UtcNow;
                return false;
            }
            finally
            {
                GreyscreenSyncGate.Release();
            }
        }

        private static string FormatGreyscreenDuration(long seconds)
        {
            if (seconds <= 0)
                return "0 min";

            if (seconds < 60)
                return $"{seconds}s";

            long minutes = seconds / 60;
            if (minutes < 120)
                return $"{minutes} min";

            double hours = seconds / 3600.0;
            if (hours < 48)
                return $"{hours:F1} h";

            double days = seconds / 86400.0;
            return $"{days:F1} d";
        }

        private static Models.Account? FindSavedAccountForGreyscreenSync(System.Collections.Generic.List<Models.Account> accounts, LcuGreyscreenStatsResult result)
        {
            string gameName = NormalizeRiotIdPart(result.GameName);
            string tagLine = NormalizeRiotIdPart(result.TagLine);

            Models.Account? exact = accounts.FirstOrDefault(account =>
                NormalizeRiotIdPart(account.GameName) == gameName &&
                (string.IsNullOrEmpty(tagLine) || NormalizeRiotIdPart(account.TagLine) == tagLine));

            if (exact != null)
                return exact;

            return accounts.FirstOrDefault(account => NormalizeRiotIdPart(account.GameName) == gameName);
        }

        private static string NormalizeRiotIdPart(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

    }
}
