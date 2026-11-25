using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

internal static class RemoteLobbyData
{
    internal static bool CanChange(JsonElement lobby) => lobby.TryGetProperty("localMember", out var member) &&
        member.ValueKind == JsonValueKind.Object && !Flag(member, "isSpectator") &&
        (member.TryGetProperty("allowedChangeActivity", out var allowed) ? allowed.ValueKind == JsonValueKind.True : Flag(member, "isLeader")) &&
        (!lobby.TryGetProperty("gameConfig", out var config) || !Flag(config, "isCustom"));

    internal static List<RemoteLobbyMemberDto> Members(JsonElement lobby)
    {
        if (!lobby.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array) return new();
        lobby.TryGetProperty("localMember", out var local);
        string self = Text(local, "puuid"), selfId = Id(local, "summonerId");
        return members.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object).Select((m, i) => new RemoteLobbyMemberDto
        {
            PlayerName = Text(m, "gameName") is { Length: > 0 } game ? game : Text(m, "summonerName") is { Length: > 0 } name ? name : $"Player {i + 1}",
            TagLine = Text(m, "tagLine"), IsLeader = Flag(m, "isLeader"), IsSpectator = Flag(m, "isSpectator"),
            IsLocalPlayer = self.Length > 0 ? Text(m, "puuid") == self : selfId.Length > 0 && Id(m, "summonerId") == selfId,
            FirstPosition = Text(m, "firstPositionPreference"), SecondPosition = Text(m, "secondPositionPreference"),
            IconId = Number(m, "summonerIconId"), Puuid = Text(m, "puuid"), SummonerId = Id(m, "summonerId")
        }).ToList();
    }

    internal static List<RemoteQueueDto> Queues(JsonElement catalog, int partySize)
    {
        if (catalog.ValueKind != JsonValueKind.Array) return new();
        var result = new List<RemoteQueueDto>();
        foreach (var q in catalog.EnumerateArray().Where(q => q.ValueKind == JsonValueKind.Object))
        {
            int id = Number(q, "id"); string mode = Text(q, "gameMode");
            bool arena = mode.Equals("CHERRY", StringComparison.OrdinalIgnoreCase);
            bool aram = mode.Equals("ARAM", StringComparison.OrdinalIgnoreCase);
            if (!(id is 400 or 420 or 440 || arena || aram) || Flag(q, "isCustom")) continue;
            string name = id switch { 400 => "Normal Draft", 420 => "Solo / Duo", 440 => "Ranked Flex", 450 => "ARAM", _ => Text(q, "name") };
            if (name.Length == 0) name = arena ? "Arena" : "ARAM";
            string availability = Text(q, "queueAvailability");
            bool available = availability == "Available" && (!q.TryGetProperty("isEnabled", out var enabled) || enabled.ValueKind == JsonValueKind.True);
            string reason = available ? "" : availability == "DoesntMeetRequirements" ? "Account requirements not met" : "Currently unavailable";
            if (q.TryGetProperty("allowablePremadeSizes", out var sizes) && sizes.ValueKind == JsonValueKind.Array && sizes.GetArrayLength() > 0 &&
                !sizes.EnumerateArray().Any(size => size.ValueKind == JsonValueKind.Number && size.TryGetInt32(out int n) && n == Math.Max(1, partySize)))
            { available = false; reason = "Party size not supported"; }
            result.Add(new() { Id = id, Name = name, GameMode = mode, IsAvailable = available, UnavailableReason = reason });
        }
        return result.GroupBy(q => q.Id).Select(g => g.First()).OrderBy(q => q.Id is 420 ? 0 : q.Id is 400 ? 1 : q.Id is 440 ? 2 : q.GameMode == "ARAM" ? 3 : 4).ThenBy(q => q.Id).ToList();
    }

    internal static bool Flag(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.True;
    internal static string Text(JsonElement value, string field) => RiotLocalProtocol.Text(value, field);
    internal static int Number(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int n) ? n : 0;
    private static string Id(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var p) ? p.ToString() : "";
}
