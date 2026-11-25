using System.Text.Json;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

public sealed partial class RemotePickServerService
{
    private static async Task<DraftExitProbe> ProbeDraftExitAsync()
    {
        LCUService.InvalidateCachedReads();
        string phase = await LCUService.GetCurrentGamePhaseAsync();
        var clientTask = LCUService.ClientRequestAsync("GET", "lol-champ-select/v1/session");
        var serverTask = LCUService.ClientRequestAsync("GET", "lol-lobby-team-builder/champ-select/v1/session");
        await Task.WhenAll(clientTask, serverTask);
        var client = await clientTask; var server = await serverTask;
        return new(phase, Identity(client), Active(client), Active(server));

        static bool? Active(string[] response)
        {
            if (response[0] is "404" or "204") return false;
            if (response[0] != "200") return null;
            try
            {
                using var doc = JsonDocument.Parse(response[1]);
                return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("myTeam", out var players) && players.ValueKind == JsonValueKind.Array
                    ? players.GetArrayLength() > 0 : null;
            }
            catch (JsonException) { return null; }
        }
        static string Identity(string[] response)
        {
            if (response[0] != "200") return "";
            try { using var doc = JsonDocument.Parse(response[1]); return RemoteDraftInteractions.DraftId(doc.RootElement); }
            catch (JsonException) { return ""; }
        }
    }
}
