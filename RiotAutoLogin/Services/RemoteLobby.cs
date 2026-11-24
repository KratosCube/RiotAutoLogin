using System;
using System.Text.Json;

namespace RiotAutoLogin.Services
{
    internal sealed record RemoteLobby(int QueueId, bool IsCustom, bool CanStart, int PartySize, string StartHint)
    {
        public static RemoteLobby? Read(JsonElement lobby)
        {
            if (lobby.ValueKind != JsonValueKind.Object ||
                !lobby.TryGetProperty("gameConfig", out var config) || config.ValueKind != JsonValueKind.Object ||
                !lobby.TryGetProperty("localMember", out var member) || member.ValueKind != JsonValueKind.Object)
                return null;

            int queue = config.TryGetProperty("queueId", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int q) ? q : 0;
            bool custom = Flag(config, "isCustom");
            bool allowed = member.TryGetProperty("allowedStartActivity", out var permission)
                ? permission.ValueKind == JsonValueKind.True : Flag(member, "isLeader");
            bool canStart = (custom || queue > 0) && allowed && !Flag(member, "isSpectator") && Flag(lobby, "canStartActivity");
            int count = lobby.TryGetProperty("members", out var members) && members.ValueKind == JsonValueKind.Array
                ? members.GetArrayLength() : 0;
            string hint = canStart ? string.Empty : !allowed ? "The party leader can start the game." : "Finish setting up the lobby in League Client.";
            return new RemoteLobby(queue, custom, canStart, count, hint);
        }

        public static double QueueSeconds(JsonElement search)
        {
            if (search.ValueKind != JsonValueKind.Object ||
                search.TryGetProperty("isCurrentlyInQueue", out var active) && active.ValueKind == JsonValueKind.False ||
                !search.TryGetProperty("timeInQueue", out var time) || time.ValueKind != JsonValueKind.Number ||
                !time.TryGetDouble(out double seconds) || !double.IsFinite(seconds) || seconds < 0)
                return -1;
            return seconds;
        }

        private static bool Flag(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}
