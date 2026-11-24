using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RiotAutoLogin.Models
{
    public class Account : INotifyPropertyChanged
    {
        private string _accountName = "", _gameName = "", _tagLine = "", _region = "", _avatarPath = "";
        public string AccountName { get => _accountName; set => SetDisplayField(ref _accountName, value); }
        public string GameName { get => _gameName; set => SetDisplayField(ref _gameName, value); }
        public string TagLine { get => _tagLine; set => SetDisplayField(ref _tagLine, value); }
        public string Region { get => _region; set => SetDisplayField(ref _region, value); }
        public string EncryptedPassword { get; set; } = string.Empty;
        public string RankInfo { get; set; } = string.Empty;
        public int LeaguePoints { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Greyscreens { get; set; }
        public long GreyscreenSeconds { get; set; }
        public string GreyscreensLastUpdatedUtc { get; set; } = string.Empty;
        public string AvatarPath { get => _avatarPath; set => SetDisplayField(ref _avatarPath, value); }
        public Dictionary<RankedQueue, RankData> QueueRanks { get; set; } = new();
        public Dictionary<RankedQueue, GreyscreenData> QueueGreyscreens { get; set; } = new();
        public DateTime? RanksUpdatedUtc { get; set; }

        [JsonIgnore] public RankedQueue SelectedQueue { get; set; }
        [JsonIgnore] public RankData? DisplayRank => QueueRanks.GetValueOrDefault(SelectedQueue);
        [JsonIgnore] public string DisplayRankInfo => DisplayRank?.Label ?? (SelectedQueue == RankedQueue.RankedFives ? "No Ranked 5s data" : "Not synced");
        [JsonIgnore] public string DisplayLeaguePoints => DisplayRank?.LeaguePoints.ToString() ?? "—";
        [JsonIgnore] public string DisplayWins => DisplayRank?.Wins.ToString() ?? "—";
        [JsonIgnore] public string DisplayLosses => DisplayRank?.Losses.ToString() ?? "—";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetDisplayField(ref string field, string value, [CallerMemberName] string? name = null)
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void RefreshRankDisplay()
        {
            foreach (string name in new[] { nameof(DisplayRank), nameof(DisplayRankInfo), nameof(DisplayLeaguePoints), nameof(DisplayWins), nameof(DisplayLosses) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void MigrateLegacyRank()
        {
            QueueRanks ??= new();
            QueueGreyscreens ??= new();
            // Older releases persisted this empty record when League-V4 omitted Ranked 5s.
            if (QueueRanks.GetValueOrDefault(RankedQueue.RankedFives) is
                { Tier: "", Rank: "", LegacyLabel: null, LeaguePoints: 0, Wins: 0, Losses: 0 })
                QueueRanks.Remove(RankedQueue.RankedFives);
            if (QueueRanks.Count == 0 && !string.IsNullOrWhiteSpace(RankInfo) && !RankInfo.StartsWith("Error:"))
                QueueRanks[RankedQueue.SoloDuo] = new RankData { LegacyLabel = RankInfo, LeaguePoints = LeaguePoints, Wins = Wins, Losses = Losses };
        }

        public void ApplyRanks(Dictionary<RankedQueue, RankData> fresh)
        {
            if (fresh.Count == 0) return;
            var merged = new Dictionary<RankedQueue, RankData>(QueueRanks);
            foreach (var (queue, rank) in fresh)
                merged[queue] = rank;
            QueueRanks = merged;
            RanksUpdatedUtc = DateTime.UtcNow;
            if (fresh.TryGetValue(RankedQueue.SoloDuo, out var solo))
            {
                RankInfo = solo.Label;
                LeaguePoints = solo.LeaguePoints;
                Wins = solo.Wins;
                Losses = solo.Losses;
            }
            RefreshRankDisplay();
        }

        public bool MatchesRiotId(string gameName, string tagLine, string region) =>
            string.Equals(GameName?.Trim(), gameName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(TagLine?.Trim(), tagLine?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Region?.Trim(), region?.Trim(), StringComparison.OrdinalIgnoreCase);

        public void UpdateRiotId(string gameName, string tagLine, string region)
        {
            bool samePlayer = MatchesRiotId(gameName, tagLine, region);
            GameName = gameName;
            TagLine = tagLine;
            Region = region;
            if (samePlayer) return;

            QueueRanks = new();
            RanksUpdatedUtc = null;
            RankInfo = string.Empty;
            LeaguePoints = Wins = Losses = 0;
            RefreshRankDisplay();
        }
    }

    public class GreyscreenData
    {
        public int Deaths { get; set; }
        public long Seconds { get; set; }
        public int Games { get; set; }
        public bool Estimated { get; set; }
    }

    public class RankData
    {
        public string Tier { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;
        public int LeaguePoints { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public string? LegacyLabel { get; set; }

        [JsonIgnore] public string Label => LegacyLabel ??
            (string.IsNullOrWhiteSpace(Tier) || Tier == "NONE" || Tier == "UNRANKED" ? "Unranked" :
            $"{Tier} {Rank}".Trim() + $" ({LeaguePoints} LP, {Wins}W/{Losses}L)");
    }
}
