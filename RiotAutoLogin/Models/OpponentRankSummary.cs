using System;
using System.Text.Json.Serialization;

namespace RiotAutoLogin.Models;

public sealed class OpponentRankSummary
{
    public const int SampleLimit = 10;
    private static readonly string[] Tiers = ["IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND"];
    private static readonly string[] Divisions = ["IV", "III", "II", "I"];

    public double? MeanRankPosition { get; set; }
    public int Games { get; set; }
    public int RankedOpponents { get; set; }
    public int TotalOpponents { get; set; }
    public DateTime UpdatedUtc { get; set; }

    [JsonIgnore]
    public string Label
    {
        get
        {
            if (MeanRankPosition is not { } position || !double.IsFinite(position) || position < 0)
                return "—";
            // Apex tiers share the same LP ladder. Their population-based thresholds cannot be inferred here.
            if (position >= 2800) return "MASTER+";
            int division = (int)(position / 100);
            return $"{Tiers[division / 4]} {Divisions[division % 4]}";
        }
    }

    [JsonIgnore]
    public string Tooltip =>
        $"Average current rank of {RankedOpponents}/{TotalOpponents} opponents across {Games} games in this queue. " +
        $"Up to {SampleLimit} recent completed games from the client's latest 100 history entries. " +
        "Remakes, unavailable and unranked opponents are excluded from the average. " +
        "Ranks reflect the latest lookup, cached for up to 15 minutes. Each division spans 100 LP and Master+ uses a shared LP ladder. " +
        $"Updated {UpdatedUtc.ToLocalTime():g}.";

    internal static double? Position(RankData rank)
    {
        string tier = rank.Tier.ToUpperInvariant();
        if (tier is "MASTER" or "GRANDMASTER" or "CHALLENGER")
            return 2800.0 + Math.Max(0, rank.LeaguePoints);
        int tierIndex = Array.IndexOf(Tiers, tier);
        int divisionIndex = Array.IndexOf(Divisions, rank.Rank.ToUpperInvariant());
        if (tierIndex < 0 || divisionIndex < 0) return null;
        return (tierIndex * 4 + divisionIndex) * 100.0 + Math.Clamp(rank.LeaguePoints, 0, 100);
    }
}
