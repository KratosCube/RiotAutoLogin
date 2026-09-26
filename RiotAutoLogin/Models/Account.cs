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
        [JsonIgnore] public string DisplayRankInfo => DisplayRank?.Label ?? "Not synced";
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
            if (QueueRanks.Count == 0 && !string.IsNullOrWhiteSpace(RankInfo) && !RankInfo.StartsWith("Error:"))
                QueueRanks[RankedQueue.SoloDuo] = new RankData { LegacyLabel = RankInfo, LeaguePoints = LeaguePoints, Wins = Wins, Losses = Losses };
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
