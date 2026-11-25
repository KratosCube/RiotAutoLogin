using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

public sealed partial class RemotePickServerService
{
    private readonly SemaphoreSlim _skinGate = new(1, 1);
    private async Task<RemoteSkinGalleryDto> GetSkinsAsync()
    {
        var gallery = new RemoteSkinGalleryDto();
        if (await LCUService.GetCurrentGamePhaseAsync() != "ChampSelect") return gallery;
        using var session = await ReadClientObjectAsync("lol-champ-select/v1/session");
        using var info = await ReadClientObjectAsync("lol-champ-select/v1/skin-selector-info");
        if (session == null || info == null) return gallery;
        gallery.DraftId = RemoteDraftInteractions.DraftId(session.RootElement);
        gallery.ChampionId = RemoteLobbyData.Number(info.RootElement, "selectedChampionId");
        gallery.SelectedSkinId = RemoteLobbyData.Number(info.RootElement, "selectedSkinId");
        if (gallery.ChampionId <= 0) return gallery;
        using var carousel = await ReadClientObjectAsync("lol-champ-select/v1/skin-carousel-skins");
        using var pickable = await ReadClientObjectAsync("lol-champ-select/v1/pickable-skin-ids");
        if (carousel == null || pickable == null) return gallery;
        gallery.Skins = RemoteSkinData.Read(carousel.RootElement, pickable.RootElement, gallery.ChampionId, gallery.SelectedSkinId);
        gallery.CanSelect = RemoteLobbyData.Flag(session.RootElement, "allowSkinSelection") && RemoteLobbyData.Flag(info.RootElement, "showSkinSelector") &&
            !RemoteLobbyData.Flag(info.RootElement, "skinSelectionDisabled") && gallery.DraftId.Length > 0;
        return gallery;
    }
    private async Task<RemotePickActionResult> SelectSkinAsync(string body)
    {
        if (!await _skinGate.WaitAsync(0)) return Failure("A skin selection is already in progress.");
        try
        {
            using var request = JsonDocument.Parse(body);
            int id = RemoteLobbyData.Number(request.RootElement, "skinId"), champion = RemoteLobbyData.Number(request.RootElement, "championId");
            var gallery = await GetSkinsAsync();
            if (!gallery.CanSelect || gallery.ChampionId != champion || gallery.DraftId != RemoteLobbyData.Text(request.RootElement, "draftId") ||
                !gallery.Skins.Any(s => s.Id == id && s.IsAvailable)) return Failure("This skin is no longer available for your current champion.");
            var response = await LCUService.ClientRequestAsync("PATCH", "lol-champ-select/v1/session/my-selection", JsonSerializer.Serialize(new { selectedSkinId = id }));
            if (!response[0].StartsWith("2", StringComparison.Ordinal)) return Failure("League Client rejected the skin selection.");
            for (int attempt = 0; attempt < 5; attempt++)
            {
                using var info = await ReadClientObjectAsync("lol-champ-select/v1/skin-selector-info");
                LCUService.InvalidateCachedReads();
                using var draft = await ReadClientObjectAsync("lol-champ-select/v1/session");
                if (draft == null || RemoteDraftInteractions.DraftId(draft.RootElement) != gallery.DraftId) break;
                if (info != null && RemoteLobbyData.Number(info.RootElement, "selectedChampionId") == champion && RemoteLobbyData.Number(info.RootElement, "selectedSkinId") == id)
                    return new() { Success = true, Message = "Skin selected." };
                await Task.Delay(150);
            }
            return Failure("The client has not confirmed the skin change. Refresh and try again.");
        }
        catch (JsonException) { return Failure("Choose an available skin."); }
        finally { _skinGate.Release(); }
    }
    private static async Task AddSwapsAsync(JsonElement session, RemotePickState state)
    {
        if (state.IsRandomChampionMode) return;
        async Task<JsonDocument?> Fallback(string field, string kind) => session.TryGetProperty(field, out var list) && list.ValueKind == JsonValueKind.Array
            ? null : await ReadClientObjectAsync($"lol-champ-select/v1/session/{kind}-swaps");
        var orderTask = Fallback("pickOrderSwaps", "pick-order"); var positionTask = Fallback("positionSwaps", "position");
        await Task.WhenAll(orderTask, positionTask);
        using var order = await orderTask; using var position = await positionTask;
        state.Swaps = RemoteDraftInteractions.ReadSwaps(session, "pickOrderSwaps", "pick-order", order?.RootElement ?? default);
        state.Swaps.AddRange(RemoteDraftInteractions.ReadSwaps(session, "positionSwaps", "position", position?.RootElement ?? default));
    }
    private async Task<RemotePickActionResult> SwapDraftAsync(string body)
    {
        if (!await _lobbyActionGate.WaitAsync(0)) return Failure("Another lobby action is in progress.");
        try
        {
            using var request = JsonDocument.Parse(body);
            string kind = RemoteLobbyData.Text(request.RootElement, "kind"), id = RemoteLobbyData.Text(request.RootElement, "id"), action = RemoteLobbyData.Text(request.RootElement, "action");
            if (kind is not ("position" or "pick-order") || !RemoteDraftInteractions.ValidId(id) || action is not ("request" or "accept" or "decline" or "cancel")) return Failure("Invalid swap action.");
            var state = await GetTimerStateAsync();
            var swap = state.Swaps.FirstOrDefault(s => s.Kind == kind && s.Id == id && s.CellId == RemoteLobbyData.Number(request.RootElement, "cellId"));
            if (!state.IsInChampSelect || state.DraftId.Length == 0 || state.DraftId != RemoteLobbyData.Text(request.RootElement, "draftId") || swap == null || !RemoteDraftInteractions.Allows(swap, action))
                return Failure("That swap is no longer available. The draft has changed.");
            var reply = await LCUService.ClientRequestAsync("POST", $"lol-champ-select/v1/session/{kind}-swaps/{id}/{action}");
            return reply[0].StartsWith("2", StringComparison.Ordinal) ? new() { Success = true, Message = action == "request" ? "Swap requested." : action == "accept" ? "Swap accepted." : action == "decline" ? "Swap declined." : "Swap cancelled." } : Failure("League Client rejected the swap. Refresh and try again.");
        }
        catch (JsonException) { return Failure("Invalid swap action."); }
        finally { _lobbyActionGate.Release(); }
    }
}
