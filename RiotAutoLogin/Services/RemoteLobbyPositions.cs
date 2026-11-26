using System;
using System.Text.Json;
namespace RiotAutoLogin.Services;
internal static class RemoteLobbyPositions
{
    internal static bool CanSelect(JsonElement lobby) => lobby.ValueKind == JsonValueKind.Object &&
        lobby.TryGetProperty("gameConfig", out var config) && RemoteLobbyData.Flag(config, "showPositionSelector") &&
        !RemoteLobbyData.Flag(config, "isCustom") && !RemoteLobbyData.Flag(config, "showQuickPlaySlotSelection") &&
        lobby.TryGetProperty("localMember", out var local) && local.ValueKind == JsonValueKind.Object && !RemoteLobbyData.Flag(local, "isSpectator");
    internal static bool Valid(string first, string second) => IsRole(first) &&
        (first == "FILL" ? second is "UNSELECTED" or "FILL" : IsRole(second) && second != "FILL" && first != second);
    internal static bool IsRole(string role) => role is "TOP" or "JUNGLE" or "MIDDLE" or "BOTTOM" or "UTILITY" or "FILL";
}
