using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;
internal static class RemoteChatData
{
    internal static string FindRoom(JsonElement session, JsonElement conversations)
    {
        if (!session.TryGetProperty("chatDetails", out var chat) || chat.ValueKind != JsonValueKind.Object || conversations.ValueKind != JsonValueKind.Array) return "";
        string direct = RemoteLobbyData.Text(chat, "multiUserChatId"), claim = "", domain = "";
        if (chat.TryGetProperty("mucJwtDto", out var muc)) { claim = RemoteLobbyData.Text(muc, "channelClaim"); domain = RemoteLobbyData.Text(muc, "domain"); }
        var matches = conversations.EnumerateArray().Where(c => RemoteLobbyData.Text(c, "type").Equals("championSelect", StringComparison.OrdinalIgnoreCase)).Where(c =>
        {
            string id = RemoteLobbyData.Text(c, "id"), pid = RemoteLobbyData.Text(c, "pid");
            if (id.Length == 0) return false;
            if (Matches(direct, id) || Matches(direct, pid)) return true;
            if (Matches(claim, id) || Matches(claim, pid)) return true;
            if (claim.Length == 0 || !c.TryGetProperty("mucJwtDto", out var cm)) return false;
            return RemoteLobbyData.Text(cm, "channelClaim") == claim && (domain.Length == 0 || RemoteLobbyData.Text(cm, "domain") == domain);
        }).Select(c => RemoteLobbyData.Text(c, "id")).Distinct().ToArray();
        static bool Matches(string key, string candidate) => key.Length > 0 && (candidate == key || !key.Contains('@') && candidate.StartsWith(key + "@", StringComparison.Ordinal));
        // Never guess a room from its type alone: an old draft room may still exist.
        return matches.Length == 1 ? matches[0] : "";
    }
    internal static bool ValidBody(string body) => !string.IsNullOrWhiteSpace(body) && body.Length <= 500 && !body.Any(char.IsControl);
    internal static List<RemoteChatMessageDto> Messages(JsonElement messages, IReadOnlyList<RemoteTeamSlotDto> team)
    {
        if (messages.ValueKind != JsonValueKind.Array) return new();
        return messages.EnumerateArray().Where(m => RemoteLobbyData.Text(m, "type") is "chat" or "groupchat")
            .Select((m, index) =>
            {
                string puuid = RemoteLobbyData.Text(m, "fromPuuid"), summoner = RemoteDraftInteractions.Scalar(m, "fromSummonerId");
                var player = team.FirstOrDefault(p => puuid.Length > 0 && p.PlayerPuuid == puuid || summoner.Length > 0 && summoner != "0" && p.PlayerSummonerId == summoner);
                string author = player?.IsLocalPlayer == true ? "You" : player?.IsPlayerNameVisible == true ?
                    (player.PlayerName.Length > 0 ? player.PlayerName : player.RiotGameName.Length > 0 ? player.RiotGameName : "Teammate") : player != null ? $"Ally {team.ToList().IndexOf(player) + 1}" : "Teammate";
                string body = RemoteLobbyData.Text(m, "body"); if (body.Length > 2000) body = body[..2000];
                return new RemoteChatMessageDto { Id = RemoteLobbyData.Text(m, "id") is { Length: > 0 } id ? id : index.ToString(CultureInfo.InvariantCulture),
                    Body = body, Timestamp = RemoteLobbyData.Text(m, "timestamp"), Author = author, IsLocalPlayer = player?.IsLocalPlayer == true };
            }).Where(m => m.Body.Length > 0).OrderBy(m => DateTimeOffset.TryParse(m.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var stamp) ? stamp : DateTimeOffset.MinValue)
            .TakeLast(100).ToList();
    }
}
