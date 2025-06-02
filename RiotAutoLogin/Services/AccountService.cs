using RiotAutoLogin.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class AccountService
    {
        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "accounts.json");

        public static List<Account> LoadAccounts()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return new List<Account>();

                string json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading accounts: {ex.Message}");
                return new List<Account>();
            }
        }

        public static bool SaveAccounts(List<Account> accounts)
        {
            try
            {
                string directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory) && directory != null)
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving accounts: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> UpdateAccountRankAsync(Account account, string region)
        {
            try
            {
                Debug.WriteLine($"🔍 Fetching rank for {account.GameName}#{account.TagLine} in region {region}...");
                string rankResult = await RiotClientAutomationService.GetRankAsync(account.GameName, account.TagLine, region);
                
                if (rankResult.StartsWith("Error:"))
                {
                    Debug.WriteLine($"⚠️ Rank API error for {account.GameName}: {rankResult}");
                    // Don't overwrite existing rank info with error messages
                    return false;
                }
                
                UpdateAccountWithRankInfo(account, rankResult);
                Debug.WriteLine($"✅ Updated {account.GameName} rank: {rankResult}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Exception updating rank for {account.GameName}: {ex.Message}");
                return false;
            }
        }

        public static async Task UpdateAllAccountsAsync(List<Account> accounts)
        {
            Debug.WriteLine($"🔄 Updating ranks for {accounts.Count} accounts...");
            
            var updateTasks = accounts.Select(async account =>
            {
                try
                {
                    Debug.WriteLine($"📈 Updating rank for {account.GameName}#{account.TagLine}...");
                    bool success = await UpdateAccountRankAsync(account, account.Region);
                    if (success)
                    {
                        Debug.WriteLine($"✅ Successfully updated rank for {account.GameName}: {account.RankInfo}");
                    }
                    else
                    {
                        Debug.WriteLine($"❌ Failed to update rank for {account.GameName}");
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Exception updating rank for {account.GameName}: {ex.Message}");
                    return false;
                }
            });
            
            var results = await Task.WhenAll(updateTasks);
            
            int successCount = results.Count(r => r);
            int failCount = results.Count(r => !r);
            
            Debug.WriteLine($"📊 Rank update summary: {successCount} succeeded, {failCount} failed out of {accounts.Count} accounts");
        }

        private static void UpdateAccountWithRankInfo(Account account, string rankResult)
        {
            account.RankInfo = rankResult;

            if (rankResult.Contains("(") && rankResult.Contains(")"))
            {
                try
                {
                    string lpPart = rankResult.Split('(')[1].Split(')')[0];
                    string[] parts = lpPart.Split(',');

                    if (parts.Length >= 2)
                    {
                        string lpStr = parts[0].Trim().Replace(" LP", "");
                        if (int.TryParse(lpStr, out int lp))
                            account.LeaguePoints = lp;

                        string winLossPart = parts[1].Trim();
                        if (winLossPart.Contains("W/") && winLossPart.Contains("L"))
                        {
                            string[] winLoss = winLossPart.Split("W/");
                            if (winLoss.Length == 2)
                            {
                                if (int.TryParse(winLoss[0], out int wins))
                                    account.Wins = wins;
                                if (int.TryParse(winLoss[1].Replace("L", ""), out int losses))
                                    account.Losses = losses;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error parsing rank info: {ex.Message}");
                }
            }
        }

        public static (int totalGames, int totalWins, int totalLosses, double winRate) CalculateStats(List<Account> accounts)
        {
            int totalGames = accounts.Sum(a => a.Wins + a.Losses);
            int totalWins = accounts.Sum(a => a.Wins);
            int totalLosses = accounts.Sum(a => a.Losses);
            double winRate = totalGames > 0 ? (double)totalWins / totalGames * 100 : 0;

            return (totalGames, totalWins, totalLosses, winRate);
        }
    }
} 