using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

public sealed partial class RemotePickServerService
{
    private readonly object _loadoutSync = new();
    private Task? _loadoutTask;
    private int _loadoutRevision;
    private sealed record LoadoutSnapshot(string Key, DateTime Expires, RemoteRunePageDto? Current,
        List<RemoteRunePageDto> Pages, List<RemoteRecommendedRunePageDto> Recommended);
    private LoadoutSnapshot? _loadoutSnapshot;

    private void InvalidateLoadout(bool keepPages = false)
    {
        lock (_loadoutSync)
        {
            _loadoutRevision++;
            _loadoutSnapshot = keepPages && _loadoutSnapshot is { } previous
                ? previous with { Expires = DateTime.MinValue } : null;
        }
    }
    private void AddCachedLoadout(RemotePickState state)
    {
        string key = $"{state.SelectedChampionId}:{state.PickIntentChampionId}:{state.AssignedPosition}:{state.MapId}";
        lock (_loadoutSync)
        {
            var cached = _loadoutSnapshot;
            if (cached != null)
            {
                state.CurrentRunePage = cached.Current; state.RunePages = cached.Pages;
                if (cached.Key == key)
                {
                    state.RecommendedRunePages = cached.Recommended; state.LoadoutReady = true;
                    if (cached.Expires > DateTime.UtcNow) return;
                }
            }
            if (_loadoutTask is { IsCompleted: false } || !IsRunning) return;
            int revision = _loadoutRevision;
            var sample = new RemotePickState { SelectedChampionId = state.SelectedChampionId, PickIntentChampionId = state.PickIntentChampionId,
                AssignedPosition = state.AssignedPosition, MapId = state.MapId, IsInChampSelect = true };
            _loadoutTask = Task.Run(() =>
            {
                try
                {
                    AddRunePages(sample);
                    if (!IsRunning) return;
                    AddRecommendedRunePages(sample);
                    lock (_loadoutSync)
                        if (revision == _loadoutRevision && IsRunning)
                            _loadoutSnapshot = new(key, DateTime.UtcNow.AddSeconds(5), sample.CurrentRunePage, sample.RunePages, sample.RecommendedRunePages);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Remote loadout refresh: {ex.Message}"); }
            });
        }
    }
}
