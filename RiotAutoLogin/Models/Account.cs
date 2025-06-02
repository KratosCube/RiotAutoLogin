using System;


namespace RiotAutoLogin.Models
{
    public class Account
    {
        public string AccountName { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string TagLine { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
        public string RankInfo { get; set; } = string.Empty;
        public int LeaguePoints { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public string AvatarPath { get; set; } = string.Empty;
    }

    public class RankData
    {
        public string Tier { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;
        public int LeaguePoints { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
    }
}
