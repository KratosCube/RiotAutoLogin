using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace RiotAutoLogin.Services
{
    public static class RemoteChampionAvailabilityService
    {
        private static readonly object _lock = new();
        private static DateTime _lastRefreshUtc = DateTime.MinValue;
        private static HashSet<int> _ownedOrFreeChampionIds = new();
        public static void Reset()
        {
            lock (_lock) { _lastRefreshUtc = DateTime.MinValue; _ownedOrFreeChampionIds = new(); }
        }
        public static bool CanUseChampion(int championId)
        {
            lock (_lock) return championId > 0 && (_ownedOrFreeChampionIds.Count == 0 || _ownedOrFreeChampionIds.Contains(championId));
        }
        public static void Refresh()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _lastRefreshUtc < TimeSpan.FromSeconds(30)) return;
                _lastRefreshUtc = DateTime.UtcNow;
                _ownedOrFreeChampionIds = new();
                if (LCUService.CheckIfLeagueClientIsOpen()) RefreshOwnedOrFreeChampions();
            }
        }

        private static void RefreshOwnedOrFreeChampions()
        {
            string[] result = LCUService.ClientRequest("GET", "lol-champions/v1/owned-champions-minimal");
            if (result[0] != "200")
                return;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return;

                foreach (JsonElement champion in doc.RootElement.EnumerateArray())
                {
                    if (!TryGetIntProperty(champion, "id", out int championId) || championId <= 0)
                        continue;

                    bool isOwnedOrFree = false;
                    if (champion.TryGetProperty("ownership", out JsonElement ownership) && ownership.ValueKind == JsonValueKind.Object)
                    {
                        isOwnedOrFree = GetBoolProperty(ownership, "owned") ||
                                        GetBoolProperty(ownership, "freeToPlay") ||
                                        GetBoolProperty(ownership, "freeToPlayReward") ||
                                        GetBoolProperty(ownership, "freeToPlayForQueue") ||
                                        GetBoolProperty(ownership, "rental");
                    }
                    else
                    {
                        isOwnedOrFree = GetBoolProperty(champion, "owned") ||
                                        GetBoolProperty(champion, "freeToPlay") ||
                                        GetBoolProperty(champion, "freeToPlayForQueue");
                    }

                    bool isDisabled = GetBoolProperty(champion, "disabled") || GetBoolProperty(champion, "inactive");
                    if (isOwnedOrFree && !isDisabled)
                        _ownedOrFreeChampionIds.Add(championId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse owned champions for Remote Pick: {ex.Message}");
            }
        }

        private static bool TryGetIntProperty(JsonElement element, string propertyName, out int value)
        {
            if (element.TryGetProperty(propertyName, out JsonElement property))
            {
                if (property.ValueKind == JsonValueKind.Number)
                    return property.TryGetInt32(out value);
                if (property.ValueKind == JsonValueKind.String)
                    return int.TryParse(property.GetString(), out value);
            }

            value = 0;
            return false;
        }

        private static bool GetBoolProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property))
                return false;

            if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
                return property.GetBoolean();
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number))
                return number != 0;
            if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out bool boolean))
                return boolean;

            return false;
        }

        private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String)
                    return property.GetString();
            }

            return null;
        }
    }
}
