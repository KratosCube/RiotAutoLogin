using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class AccountService
    {
        private static readonly string ConfigFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BuildChannel.DataDirectoryName, "accounts.json");
        private static readonly SemaphoreSlim RefreshGate = new(1, 1);
        private static readonly object SaveGate = new();
        private static bool _csMigrationReady = true;

        public static List<Account> LoadAccounts()
        {
            try
            {
                if (!File.Exists(ConfigFilePath)) return new();
                string json = File.ReadAllText(ConfigFilePath);
                _csMigrationReady = File.Exists(AnnualCsStore.DefaultPath) || AnnualCsStore.PreserveLegacy(json, AnnualCsStore.DefaultLegacyPath);
                var accounts = JsonSerializer.Deserialize(json, StartupJson.Default.ListAccount) ?? new();
                foreach (Account account in accounts) account.MigrateLegacyRank();
                return accounts;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading accounts: {ex.Message}");
                return new();
            }
        }

        public static bool SaveAccounts(List<Account> accounts)
        {
            if (!_csMigrationReady) return false; // Preserve the previous history if its migration snapshot could not be saved.
            lock (SaveGate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
                    string temporary = ConfigFilePath + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(accounts, StartupJson.Default.ListAccount));
                    File.Move(temporary, ConfigFilePath, true);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error saving accounts: {ex.Message}");
                    return false;
                }
            }
        }

        public static async Task<bool> UpdateAccountRankAsync(Account account, string region)
        {
            try
            {
                string name = account.GameName, tag = account.TagLine;
                var ranks = await RiotClientAutomationService.GetRanksAsync(name, tag, region);
                if (ranks.Count == 0 || !account.MatchesRiotId(name, tag, region))
                    return false;
                account.ApplyRanks(ranks);
                return true;
            }
            catch (Exception ex)
            {
                // An unavailable/expired API must not erase the last valid stats.
                Debug.WriteLine($"Rank refresh failed: {ex.Message}");
                return false;
            }
        }

        public static async Task UpdateAllAccountsAsync(List<Account> accounts, bool onlyStale = false)
        {
            if (!await RefreshGate.WaitAsync(0)) return;
            try
            {
                using var concurrency = new SemaphoreSlim(2, 2);
                var pending = accounts.Where(a => !onlyStale || !a.QueueRanks.ContainsKey(RankedQueue.RankedFives) ||
                    !a.RanksUpdatedUtc.HasValue || DateTime.UtcNow - a.RanksUpdatedUtc.Value > TimeSpan.FromMinutes(10)).ToArray();
                await Task.WhenAll(pending.Select(async account =>
                {
                    await concurrency.WaitAsync();
                    try { await UpdateAccountRankAsync(account, account.Region); }
                    finally { concurrency.Release(); }
                }));
            }
            finally { RefreshGate.Release(); }
        }

        public static (int totalGames, int totalWins, int totalLosses, double winRate, int totalGreyscreens, long totalGreyscreenSeconds) CalculateStats(List<Account> accounts, RankedQueue queue = RankedQueue.SoloDuo)
        {
            var ranks = accounts.Select(a => a.QueueRanks.GetValueOrDefault(queue)).Where(r => r != null).ToArray();
            int wins = ranks.Sum(r => r!.Wins), losses = ranks.Sum(r => r!.Losses);
            var greyscreens = accounts.Select(a => a.QueueGreyscreens.GetValueOrDefault(queue)).Where(g => g != null).ToArray();
            return (wins + losses, wins, losses, wins + losses > 0 ? 100.0 * wins / (wins + losses) : 0,
                greyscreens.Sum(g => g!.Deaths), greyscreens.Sum(g => g!.Seconds));
        }
    }
}
