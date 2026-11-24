using System;

namespace RiotAutoLogin.Services
{
    public static class RemotePlayerProfileLinks
    {
        internal static string RegionSlug(string? region) => region?.Trim().ToUpperInvariant() switch
            {
                "EUW" or "EUW1" => "euw",
                "EUN" or "EUNE" or "EUN1" => "eune",
                "NA" or "NA1" => "na",
                "KR" => "kr",
                "BR" or "BR1" => "br",
                "JP" or "JP1" => "jp",
                "LA1" or "LAN" => "lan",
                "LA2" or "LAS" => "las",
                "OC1" or "OCE" => "oce",
                "TR" or "TR1" => "tr",
                "RU" => "ru",
                "PH2" or "PH" => "ph",
                "SG2" or "SG" => "sg",
                "TH2" or "TH" => "th",
                "TW2" or "TW" => "tw",
                "VN2" or "VN" => "vn",
                _ => string.Empty
            };

        public static string Create(string? region, string? gameName, string? tagLine)
        {
            string platform = RegionSlug(region);
            if (string.IsNullOrEmpty(platform) || string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine))
                return string.Empty;

            // League of Graphs uses a plus sign in the path for spaces in a Riot ID.
            string name = Uri.EscapeDataString(gameName.Trim().Replace(' ', '+'));
            string tag = Uri.EscapeDataString(tagLine.Trim());
            return $"https://www.leagueofgraphs.com/summoner/{platform}/{name}-{tag}";
        }
    }
}
