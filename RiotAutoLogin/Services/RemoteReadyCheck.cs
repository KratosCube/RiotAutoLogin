using System;
using System.Text.Json;
namespace RiotAutoLogin.Services;
internal sealed class RemoteReadyCheck
{
    private readonly object _sync = new();
    private string _id = "", _response = "", _match = ""; private int _pid;
    internal string Observe(string phase, int pid, string response, string match = "")
    {
        lock (_sync)
        {
            if (phase != "ReadyCheck") { _id = _response = _match = ""; return ""; }
            if (_id.Length == 0 || pid != _pid || match.Length > 0 && _match.Length > 0 && match != _match ||
                response == "None" && (_response is "Accepted" or "Declined")) _id = Guid.NewGuid().ToString("N");
            _pid = pid; if (match.Length > 0) _match = match; _response = response;
            return _id;
        }
    }
    internal static string MatchId(JsonElement gameflow)
    {
        string id = gameflow.TryGetProperty("gameData", out var data) ? RemoteDraftInteractions.Scalar(data, "gameId") : "";
        return id == "0" ? "" : id;
    }
}
