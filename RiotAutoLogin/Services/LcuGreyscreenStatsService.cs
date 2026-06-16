using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public sealed class LcuGreyscreenStatsResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string TagLine { get; set; } = string.Empty;
        public string Puuid { get; set; } = string.Empty;
        public int Greyscreens { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public static class LcuGreyscreenStatsService
    {
        public static async Task<LcuGreyscreenStatsResult> GetCurrentAccountGreyscreensAsync()
        {
            await Task.Yield();

            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Error("League Client is not running or LCU is not available.");

            var summoner = ReadCurrentSummoner();
            if (!summoner.success)
                return Error(summoner.message);

            if (string.IsNullOrWhiteSpace(summoner.puuid))
                return Error("LCU did not return a PUUID for the current summoner.");

            if (TryReadMatchHistoryDeaths(summoner.puuid, out int deaths, out string source))
            {
                return new LcuGreyscreenStatsResult
                {
                    Success = true,
                    GameName = summoner.gameName,
                    TagLine = summoner.tagLine,
                    Puuid = summoner.puuid,
                    Greyscreens = deaths,
                    Source = source,
                    Message = $"Synced {deaths} greyscreens from LCU match history."
                };
            }

            return Error("Could not find deaths/greyscreens in LCU match history for the current account.", summoner.gameName, summoner.tagLine, summoner.puuid);
        }

        private static LcuGreyscreenStatsResult Error(string message, string gameName = "", string tagLine = "", string puuid = "") => new()
        {
            Success = false,
            Message = message,
            GameName = gameName,
            TagLine = tagLine,
            Puuid = puuid
        };

        private static (bool success, string message, string gameName, string tagLine, string puuid) ReadCurrentSummoner()
        {
            string[] result = LCUService.ClientRequest("GET", "lol-summoner/v1/current-summoner");
            if (result[0] != "200")
                return (false, $"Could not read current summoner from LCU. Status: {result[0]}", string.Empty, string.Empty, string.Empty);

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                JsonElement root = doc.RootElement;

                string gameName = GetString(root, "gameName", "riotIdGameName", "displayName", "name");
                string tagLine = GetString(root, "tagLine", "riotIdTagline", "riotIdTagLine");
                string puuid = GetString(root, "puuid", "puuId");

                if (gameName.Contains('#') && string.IsNullOrWhiteSpace(tagLine))
                {
                    string[] parts = gameName.Split('#', 2);
                    gameName = parts[0];
                    tagLine = parts.Length > 1 ? parts[1] : string.Empty;
                }

                return (true, string.Empty, gameName, tagLine, puuid);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse current summoner for greyscreens: {ex.Message}");
                return (false, "Could not parse current summoner response from LCU.", string.Empty, string.Empty, string.Empty);
            }
        }

        private static bool TryReadMatchHistoryDeaths(string puuid, out int deaths, out string source)
        {
            source = $"lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=100";
            string[] result = LCUService.ClientRequest("GET", source);
            if (!result[0].StartsWith("2") || string.IsNullOrWhiteSpace(result[1]))
            {
                deaths = 0;
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                deaths = SumDeathsForPuuid(doc.RootElement, puuid);
                return deaths > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse match history deaths: {ex.Message}");
                deaths = 0;
                return false;
            }
        }

        private static int SumDeathsForPuuid(JsonElement root, string puuid)
        {
            int total = 0;
            if (!TryFindProperty(root, "games", out JsonElement games) || games.ValueKind != JsonValueKind.Array)
                return 0;

            foreach (JsonElement game in games.EnumerateArray())
            {
                if (!TryFindParticipantIdForPuuid(game, puuid, out int participantId))
                    continue;

                if (TryFindParticipantStats(game, participantId, out JsonElement stats) && TryReadDeaths(stats, out int deaths))
                    total += deaths;
            }

            return total;
        }

        private static bool TryFindParticipantIdForPuuid(JsonElement game, string puuid, out int participantId)
        {
            participantId = 0;
            if (!TryFindProperty(game, "participantIdentities", out JsonElement identities) || identities.ValueKind != JsonValueKind.Array)
                return false;

            foreach (JsonElement identity in identities.EnumerateArray())
            {
                JsonElement player = identity;
                if (identity.TryGetProperty("player", out JsonElement nestedPlayer))
                    player = nestedPlayer;

                if (!GetString(player, "puuid", "puuId").Equals(puuid, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryFindProperty(identity, "participantId", out JsonElement idElement) && TryGetInt(idElement, out participantId))
                    return participantId > 0;
            }

            return false;
        }

        private static bool TryFindParticipantStats(JsonElement game, int participantId, out JsonElement stats)
        {
            stats = default;
            if (!TryFindProperty(game, "participants", out JsonElement participants) || participants.ValueKind != JsonValueKind.Array)
                return false;

            foreach (JsonElement participant in participants.EnumerateArray())
            {
                if (!TryFindProperty(participant, "participantId", out JsonElement idElement) || !TryGetInt(idElement, out int id) || id != participantId)
                    continue;

                if (TryFindProperty(participant, "stats", out stats))
                    return true;

                stats = participant;
                return true;
            }

            return false;
        }

        private static bool TryReadDeaths(JsonElement stats, out int deaths)
        {
            foreach (string propertyName in new[] { "deaths", "numDeaths", "totalDeaths" })
            {
                if (TryFindProperty(stats, propertyName, out JsonElement value) && TryGetInt(value, out deaths))
                    return true;
            }

            deaths = 0;
            return false;
        }

        private static bool TryFindProperty(JsonElement element, string propertyName, out JsonElement value)
        {
            value = default;
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }

                    if (TryFindProperty(property.Value, propertyName, out value))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (TryFindProperty(item, propertyName, out value))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetInt(JsonElement element, out int value)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetInt32(out value);
            if (element.ValueKind == JsonValueKind.String)
                return int.TryParse(element.GetString(), out value);

            value = 0;
            return false;
        }

        private static string GetString(JsonElement element, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
