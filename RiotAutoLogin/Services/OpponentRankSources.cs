using RiotAutoLogin.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal sealed class OpponentRankSources(
    Func<string, string, string, RankedQueue, CancellationToken, Task<OpponentRankSummary?>> local,
    Func<string, string, string, RankedQueue, string, CancellationToken, Task<OpponentRankSummary?>> remote)
{
    internal async Task<(OpponentRankSummary? Summary, string Status)> ReadAsync(string name, string tag,
        string region, RankedQueue queue, bool clientOpen, string key, CancellationToken token)
    {
        if (clientOpen)
        {
            using var localTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            localTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                if (await local(name, tag, region, queue, localTimeout.Token).ConfigureAwait(false) is { } sample)
                    return (sample, "");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception) when (!token.IsCancellationRequested) { }
        }
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(key))
            return (null, "Open League Client in this account's region, or save a Riot API key in Settings to refresh without it.");
        try
        {
            var sample = await remote(name, tag, region, queue, key, token).ConfigureAwait(false);
            return (sample, sample == null ? "Riot API could not provide a complete sample for this account and queue. Saved data is retained." : "");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (RiotApiException ex) { return (null, ex.Message); }
        catch (Exception) { return (null, "Riot API is temporarily unavailable. Saved statistics are retained."); }
    }
}
