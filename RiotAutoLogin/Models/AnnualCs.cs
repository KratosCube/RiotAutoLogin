using System;
using System.Collections.Generic;
using System.Linq;

namespace RiotAutoLogin.Models;

public sealed class CsMatch
{
    public long Started { get; set; }
    public int Cs { get; set; }
    public int Seconds { get; set; }
}

public sealed class AnnualCs
{
    public Dictionary<string, CsMatch> Matches { get; set; } = new();
    public List<string> Pending { get; set; } = new();
    public HashSet<string> Unavailable { get; set; } = new();
    public long ScanFrom { get; set; }
    public long ScanUntil { get; set; }
    public long CompletedUntil { get; set; }
    public int Offset { get; set; }
    public bool Scanning { get; set; }

    public AnnualCs Copy() => new()
    {
        Matches = new(Matches), Pending = new(Pending), Unavailable = new(Unavailable),
        ScanFrom = ScanFrom, ScanUntil = ScanUntil, CompletedUntil = CompletedUntil, Offset = Offset, Scanning = Scanning
    };

    public static (long Cs, int Games, double CsPerMinute, bool Partial) Totals(IEnumerable<Account> accounts, DateTimeOffset now)
    {
        long since = now.AddYears(-1).ToUnixTimeSeconds();
        var unique = accounts.GroupBy(a => $"{a.Region.Trim().ToUpperInvariant()}/{a.GameName.Trim().ToUpperInvariant()}#{a.TagLine.Trim().ToUpperInvariant()}").ToArray();
        var matches = unique.SelectMany(group => group.SelectMany(a => a.AnnualCs.Matches)
            .GroupBy(pair => pair.Key).Select(g => g.First().Value)).Where(m => m.Started >= since && m.Started <= now.ToUnixTimeSeconds()).ToArray();
        long cs = matches.Sum(m => (long)m.Cs), seconds = matches.Sum(m => (long)m.Seconds);
        return (cs, matches.Length, seconds > 0 ? cs * 60.0 / seconds : 0,
            unique.Any(group => !group.Any(a => a.AnnualCs.CompletedUntil >= now.AddDays(-1).ToUnixTimeSeconds() &&
                !a.AnnualCs.Scanning && a.AnnualCs.Unavailable.Count == 0)));
    }
}
