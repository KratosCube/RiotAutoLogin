using System;
using System.Threading.Tasks;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

internal sealed record DraftExitProbe(string Phase, string DraftId, bool? ClientActive, bool? ServerActive);
internal static class RemoteDraftExit
{
    // This is the server-backed quit call. Gameflow's dodge endpoint only reports an event locally.
    internal static string QuitEndpoint => "lol-login/v1/session/invoke?destination=lcdsServiceProxy&method=call&args=" + Uri.EscapeDataString("[\"\",\"teambuilder-draft\",\"quitV2\",\"\"]");
    internal static async Task<RemotePickActionResult> RunAsync(string expectedDraft, Func<Task<DraftExitProbe>> probe,
        Func<string, Task<string[]>> post, Func<int, Task> delay, bool custom)
    {
        var before = await probe();
        if (expectedDraft.Length == 0 || before.Phase != "ChampSelect" || before.DraftId != expectedDraft) return Failure("The draft has changed. Dodge was not sent.");
        var response = await post(custom ? "lol-lobby-team-builder/champ-select/v1/session/quit" : QuitEndpoint);
        if (!custom && (response[0] is "404" or "405"))
        {
            before = await probe();
            if (before.Phase != "ChampSelect" || before.DraftId != expectedDraft) return Failure("The draft has changed. Check the client.");
            response = await post("lol-lobby-team-builder/champ-select/v1/session/quit");
        }
        if (!response[0].StartsWith("2", StringComparison.Ordinal)) return Failure("The client could not send dodge. Check League Client before the timer ends.");
        int clearSamples = 0; long deadline = Environment.TickCount64 + 6000;
        for (int sample = 0; sample < 20 && Environment.TickCount64 < deadline; sample++)
        {
            await delay(250);
            var current = await probe();
            if (current.Phase is "GameStart" or "InProgress" or "Reconnect") return Failure("The game has already started. Dodge was not confirmed.");
            if (current.DraftId.Length > 0 && current.DraftId != expectedDraft) return Failure("The draft has changed. Check League Client.");
            bool gone = (current.Phase is "Lobby" or "None") && current.ClientActive == false && current.ServerActive == false;
            clearSamples = gone ? clearSamples + 1 : 0;
            if (clearSamples >= 8) return new() { Success = true, Message = "Champion select left." };
        }
        return Failure("Dodge could not be confirmed. Check League Client before the timer ends.");
    }
    private static RemotePickActionResult Failure(string message) => new() { Success = false, Message = message };
}
