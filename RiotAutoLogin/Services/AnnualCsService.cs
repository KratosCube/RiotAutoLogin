using RiotAutoLogin.Models;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal sealed class AnnualCsService(Func<string, string, CancellationToken, Task<JsonElement?>> get)
{
    // One bounded checkpoint. The fixed endTime prevents new games shifting pagination during an import.
    internal async Task<AnnualCs> StepAsync(string name, string tag, string region, string key, AnnualCs previous,
        DateTimeOffset now, CancellationToken token)
    {
        if (PublicOpponentRankService.Route(region) is not { } route) throw new InvalidOperationException("Unknown account region.");
        string host = $"https://{route.Cluster}.api.riotgames.com";
        var identity = await get($"{host}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(name.Trim())}/{Uri.EscapeDataString(tag.Trim())}", key, token).ConfigureAwait(false);
        if (identity is not { } player || !string.Equals(Text(player, "gameName"), name.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Text(player, "tagLine"), tag.Trim(), StringComparison.OrdinalIgnoreCase) || Text(player, "puuid").Length == 0)
            throw new InvalidOperationException("Could not resolve this account's full Riot ID.");
        string puuid = Text(player, "puuid");
        var state = previous.Copy();
        long cutoff = now.AddYears(-1).ToUnixTimeSeconds();
        foreach (string id in state.Matches.Where(m => m.Value.Started < cutoff).Select(m => m.Key).ToArray()) state.Matches.Remove(id);
        if (!state.Scanning)
        {
            state.ScanFrom = Math.Max(cutoff, state.CompletedUntil - 86400);
            state.ScanUntil = now.ToUnixTimeSeconds();
            state.Offset = 0; state.Scanning = true;
            state.Pending = state.Unavailable.ToList(); state.Unavailable.Clear();
        }
        if (state.Pending.Count == 0)
        {
            var page = await get($"{host}/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids?start={state.Offset}&count=100&startTime={state.ScanFrom}&endTime={state.ScanUntil}", key, token).ConfigureAwait(false);
            if (page is not { ValueKind: JsonValueKind.Array } ids || ids.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                throw new InvalidOperationException("Match history is temporarily unavailable.");
            var names = ids.EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (names.Length == 0)
            {
                state.Scanning = false; state.CompletedUntil = state.ScanUntil;
                return state;
            }
            state.Offset += names.Length;
            state.Pending = names.Distinct().Where(id => !state.Matches.ContainsKey(id)).ToList();
        }
        foreach (string id in state.Pending.Take(25).ToArray())
        {
            token.ThrowIfCancellationRequested();
            var match = await get($"{host}/lol/match/v5/matches/{Uri.EscapeDataString(id)}", key, token).ConfigureAwait(false);
            if (match is not { } game || !game.TryGetProperty("metadata", out var metadata) || Text(metadata, "matchId") != id ||
                !game.TryGetProperty("info", out var info) || !info.TryGetProperty("participants", out var people) || people.ValueKind != JsonValueKind.Array)
                state.Unavailable.Add(id);
            else
            {
                var own = people.EnumerateArray().Where(p => Text(p, "puuid") == puuid).ToArray();
                if (own.Length != 1 || Number(info, "gameDuration") <= 0) state.Unavailable.Add(id);
                else
                {
                    long started = Number(info, "gameCreation") / 1000;
                    if (started >= cutoff && started <= state.ScanUntil)
                        state.Matches[id] = new CsMatch
                        {
                            Started = started, Seconds = (int)Math.Min(int.MaxValue, Number(info, "gameDuration")),
                            Cs = (int)Math.Min(int.MaxValue, Math.Max(0, Number(own[0], "totalMinionsKilled")) + Math.Max(0, Number(own[0], "neutralMinionsKilled")))
                        };
                }
            }
            state.Pending.Remove(id);
        }
        return state;
    }

    private static string Text(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static long Number(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out long n) ? n : 0;
}
