namespace RiotAutoLogin.Models
{
    public class Summoner
    {
        public string id { get; set; } = string.Empty;
        public string accountId { get; set; } = string.Empty;
        public string puuid { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public long profileIconId { get; set; }
        public long revisionDate { get; set; }
        public long summonerLevel { get; set; }
    }

    public class LeagueEntry
    {
        public string leagueId { get; set; } = string.Empty;
        public string queueType { get; set; } = string.Empty;
        public string tier { get; set; } = string.Empty;
        public string rank { get; set; } = string.Empty;
        public int leaguePoints { get; set; }
        public int wins { get; set; }
        public int losses { get; set; }
    }

    public class SummonerDto
    {
        public string id { get; set; } = string.Empty;
        public string accountId { get; set; } = string.Empty;
        public string puuid { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public int profileIconId { get; set; }
        public long revisionDate { get; set; }
        public int summonerLevel { get; set; }
    }

    public class LeagueEntryDto
    {
        public string leagueId { get; set; } = string.Empty;
        public string queueType { get; set; } = string.Empty;
        public string tier { get; set; } = string.Empty;
        public string rank { get; set; } = string.Empty;
        public string summonerId { get; set; } = string.Empty;
        public string summonerName { get; set; } = string.Empty;
        public int leaguePoints { get; set; }
        public int wins { get; set; }
        public int losses { get; set; }
        public bool veteran { get; set; }
        public bool inactive { get; set; }
        public bool freshBlood { get; set; }
        public bool hotStreak { get; set; }
    }
}
