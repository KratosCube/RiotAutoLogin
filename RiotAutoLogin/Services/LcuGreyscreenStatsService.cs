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
        public long GreyscreenSeconds { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public static class LcuGreyscreenStatsService
    {
        private const long EstimatedSecondsPerDeath = 30;

        public static async Task<LcuGreyscreenStatsResult> GetCurrentAccountGreyscreensAsync()
        {
            await Task.Yield();

            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Error("League Client is not running or LCU is not available.");

            var summoner = ReadCurrentSummoner();
            if (!summoner.success)
                return Error(summoner.message);

            if (string.IsNullOrWhiteSpace(summoner.puuid) && string.IsNullOrWhiteSpace(summoner.accountId) && string.IsNullOrWhiteSpace(summoner.summonerId))
                return Error("LCU did not return a PUUID, accountId, or summonerId for the current summoner.");

            if (TryReadMatchHistoryStats(summoner.puuid, summoner.accountId, summoner.summonerId, out int deaths, out long deadSeconds, out string source))
            {
                bool estimated = false;
                if (deadSeconds <= 0 && deaths > 0)
                {
                    deadSeconds = EstimateDeadSeconds(deaths);
                    estimated = true;
                    source += " + estimated death time";
                }

                return new LcuGreyscreenStatsResult
                {
                    Success = true,
                    GameName = summoner.gameName,
                    TagLine = summoner.tagLine,
                    Puuid = summoner.puuid,
                    Greyscreens = deaths,
                    GreyscreenSeconds = deadSeconds,
                    Source = source,
                    Message = estimated
                        ? $"Estimated {FormatDuration(deadSeconds)} greyscreen time from {deaths} deaths because LCU did not expose exact timeSpentDead."
                        : $"Synced {FormatDuration(deadSeconds)} greyscreen time from LCU match history ({deaths} deaths)."
                };
            }

            return Error("Could not find the current player in LCU match history. Try opening your profile or match history in League Client once, then sync again.", summoner.gameName, summoner.tagLine, summoner.puuid);
        }

        private static long EstimateDeadSeconds(int deaths)
        {
            return Math.Max(0, deaths) * EstimatedSecondsPerDeath;
        }

        private static LcuGreyscreenStatsResult Error(string message, string gameName = "", string tagLine = "", string puuid = "") => new()
        {
            Success = false,
            Message = message,
            GameName = gameName,
            TagLine = tagLine,
            Puuid = puuid
        };

        private static (bool success, string message, string gameName, string tagLine, string puuid, string accountId, string summonerId) ReadCurrentSummoner()
        {
            string[] result = LCUService.ClientRequest("GET", "lol-summoner/v1/current-summoner");
            if (result[0] != "200")
                return (false, $"Could not read current summoner from LCU. Status: {result[0]}", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                JsonElement root = doc.RootElement;

                string gameName = GetString(root, "gameName", "riotIdGameName", "displayName", "name");
                string tagLine = GetString(root, "tagLine", "riotIdTagline", "riotIdTagLine");
                string puuid = GetString(root, "puuid", "puuId");
                string accountId = GetString(root, "accountId");
                string summonerId = GetString(root, "summonerId", "id");

                if (gameName.Contains('#') && string.IsNullOrWhiteSpace(tagLine))
                {
                    string[] parts = gameName.Split('#', 2);
                    gameName = parts[0];
                    tagLine = parts.Length > 1 ? parts[1] : string.Empty;
                }

                return (true, string.Empty, gameName, tagLine, puuid, accountId, summonerId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse current summoner for greyscreens: {ex.Message}");
                return (false, "Could not parse current summoner response from LCU.", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            }
        }

        private static bool TryReadMatchHistoryStats(string puuid, string accountId, string summonerId, out int deaths, out long deadSeconds, out string source)
        {
            string lookupId = !string.IsNullOrWhiteSpace(puuid) ? puuid : !string.IsNullOrWhiteSpace(accountId) ? accountId : summonerId;
            source = $"lol-match-history/v1/products/lol/{lookupId}/matches?begIndex=0&endIndex=100";
            string[] result = LCUService.ClientRequest("GET", source);
            if (!result[0].StartsWith("2") || string.IsNullOrWhiteSpace(result[1]))
            {
                deaths = 0;
                deadSeconds = 0;
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                return TrySumStatsForCurrentPlayer(doc.RootElement, puuid, accountId, summonerId, out deaths, out deadSeconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse match history deaths: {ex.Message}");
                deaths = 0;
                deadSeconds = 0;
                return false;
            }
        }

        private static bool TrySumStatsForCurrentPlayer(JsonElement root, string puuid, string accountId, string summonerId, out int totalDeaths, out long totalDeadSeconds)
        {
            totalDeaths = 0;
            totalDeadSeconds = 0;

            if (!TryFindGamesArray(root, out JsonElement games))
                return false;

            bool foundCurrentPlayerInAnyGame = false;
            foreach (JsonElement game in games.EnumerateArray())
            {
                if (!TryFindParticipantIdForCurrentPlayer(game, puuid, accountId, summonerId, out int participantId))
                    continue;

                if (TryFindParticipantStats(game, participantId, out JsonElement stats))
                {
                    foundCurrentPlayerInAnyGame = true;
                    if (TryReadDeaths(stats, out int deaths))
                        totalDeaths += deaths;

                    if (TryReadDeadSeconds(stats, out long seconds))
                    {
                        totalDeadSeconds += seconds;
                    }
                    else if (TryGetGameId(game, out long gameId) && TryReadDeadSecondsFromGameDetails(gameId, puuid, accountId, summonerId, out long detailedSeconds))
                    {
                        totalDeadSeconds += detailedSeconds;
                    }
                }
            }

            return foundCurrentPlayerInAnyGame;
        }

        private static bool TryReadDeadSecondsFromGameDetails(long gameId, string puuid, string accountId, string summonerId, out long seconds)
        {
            seconds = 0;
            string[] result = LCUService.ClientRequest("GET", $"lol-match-history/v1/games/{gameId}");
            if (!result[0].StartsWith("2") || string.IsNullOrWhiteSpace(result[1]))
                return false;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                if (!TryFindParticipantIdForCurrentPlayer(doc.RootElement, puuid, accountId, summonerId, out int participantId))
                    return false;

                if (!TryFindParticipantStats(doc.RootElement, participantId, out JsonElement stats))
                    return false;

                return TryReadDeadSeconds(stats, out seconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse match detail {gameId} for greyscreen time: {ex.Message}");
                return false;
            }
        }

        private static bool TryFindGamesArray(JsonElement element, out JsonElement games)
        {
            games = default;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals("games", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        games = property.Value;
                        return true;
                    }

                    if (TryFindGamesArray(property.Value, out games))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (TryFindGamesArray(item, out games))
                        return true;
                }
            }

            return false;
        }

        private static bool TryFindParticipantIdForCurrentPlayer(JsonElement game, string puuid, string accountId, string summonerId, out int participantId)
        {
            participantId = 0;
            if (!TryFindProperty(game, "participantIdentities", out JsonElement identities) || identities.ValueKind != JsonValueKind.Array)
                return false;

            foreach (JsonElement identity in identities.EnumerateArray())
            {
                JsonElement player = identity;
                if (identity.TryGetProperty("player", out JsonElement nestedPlayer))
                    player = nestedPlayer;

                if (!MatchesCurrentPlayer(player, puuid, accountId, summonerId))
                    continue;

                if (TryFindProperty(identity, "participantId", out JsonElement idElement) && TryGetInt(idElement, out participantId))
                    return participantId > 0;
            }

            return false;
        }

        private static bool MatchesCurrentPlayer(JsonElement player, string puuid, string accountId, string summonerId)
        {
            string playerPuuid = GetString(player, "puuid", "puuId");
            if (!string.IsNullOrWhiteSpace(puuid) && playerPuuid.Equals(puuid, StringComparison.OrdinalIgnoreCase))
                return true;

            string playerAccountId = GetString(player, "accountId");
            if (!string.IsNullOrWhiteSpace(accountId) && playerAccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase))
                return true;

            string playerSummonerId = GetString(player, "summonerId", "id");
            return !string.IsNullOrWhiteSpace(summonerId) && playerSummonerId.Equals(summonerId, StringComparison.OrdinalIgnoreCase);
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

        private static bool TryGetGameId(JsonElement game, out long gameId)
        {
            if (TryFindProperty(game, "gameId", out JsonElement gameIdElement) && TryGetLong(gameIdElement, out gameId))
                return true;

            gameId = 0;
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

        private static bool TryReadDeadSeconds(JsonElement stats, out long seconds)
        {
            foreach (string propertyName in new[] { "totalTimeSpentDead", "timeSpentDead", "totalTimeDead", "deadTime" })
            {
                if (TryFindProperty(stats, propertyName, out JsonElement value) && TryGetLong(value, out seconds))
                    return true;
            }

            seconds = 0;
            return false;
        }

        private static string FormatDuration(long seconds)
        {
            if (seconds < 60)
                return $"{seconds}s";

            long minutes = seconds / 60;
            if (minutes < 120)
                return $"{minutes} min";

            double hours = seconds / 3600.0;
            if (hours < 48)
                return $"{hours:F1} h";

            double days = seconds / 86400.0;
            return $"{days:F1} d";
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

        private static bool TryGetLong(JsonElement element, out long value)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetInt64(out value);
            if (element.ValueKind == JsonValueKind.String)
                return long.TryParse(element.GetString(), out value);

            value = 0;
            return false;
        }

        private static string GetString(JsonElement element, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out JsonElement value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? string.Empty;
                    if (value.ValueKind == JsonValueKind.Number)
                        return value.GetRawText();
                }
            }

            return string.Empty;
        }
    }
}
