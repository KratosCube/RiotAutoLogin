using System;
using System.Linq;
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

        public static void RefreshAccountLists(Window window, System.Collections.Generic.List<Models.Account> accounts)
        {
            if (window.FindName("lbAccounts") is ListBox lbAccounts)
            {
                lbAccounts.ItemsSource = null;
                lbAccounts.ItemsSource = accounts;
            }

            if (window.FindName("lbLoginAccounts") is ListBox lbLoginAccounts)
            {
                lbLoginAccounts.ItemsSource = null;
                lbLoginAccounts.ItemsSource = accounts;
            }

            if (window.FindName("icLoginAccounts") is ItemsControl icLoginAccounts)
            {
                icLoginAccounts.ItemsSource = null;
                icLoginAccounts.ItemsSource = accounts;
            }
        }

        public static void UpdateTotalGameStats(Window window, System.Collections.Generic.List<Models.Account> accounts)
        {
            var (totalGames, totalWins, totalLosses, winRate, totalGreyscreens, totalGreyscreenSeconds) = AccountService.CalculateStats(accounts);

            if (window.FindName("txtStatsGamesValue") is TextBlock txtStatsGamesValue)
                txtStatsGamesValue.Text = totalGames.ToString();

            if (window.FindName("txtStatsWinsValue") is TextBlock txtStatsWinsValue)
                txtStatsWinsValue.Text = totalWins.ToString();

            if (window.FindName("txtStatsLossesValue") is TextBlock txtStatsLossesValue)
                txtStatsLossesValue.Text = totalLosses.ToString();

            if (window.FindName("txtStatsWinRateValue") is TextBlock txtStatsWinRateValue)
                txtStatsWinRateValue.Text = $"{winRate:F1}%";

            TextBlock? txtStatsGreyscreensValue = window.FindName("txtStatsGreyscreenTimeValue") as TextBlock;
            if (txtStatsGreyscreensValue != null)
                txtStatsGreyscreensValue.Text = FormatGreyscreenDuration(totalGreyscreenSeconds);

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

                account.Greyscreens = result.Greyscreens;
                account.GreyscreenSeconds = result.GreyscreenSeconds;
                account.GreyscreensLastUpdatedUtc = DateTime.UtcNow.ToString("O");
                AccountService.SaveAccounts(accounts);
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
