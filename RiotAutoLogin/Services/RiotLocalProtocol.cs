using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RiotAutoLogin.Services;

internal sealed record RiotLocalConnection(int Port, string Token)
{
    public override string ToString() => $"RiotLocalConnection(Port = {Port})";
}
internal static class RiotLocalProtocol
{
    internal static RiotLocalConnection? Connection(string commandLine, bool leagueProcess)
    {
        string port = Flag(commandLine, "riotclient-app-port"), token = Flag(commandLine, "riotclient-auth-token");
        if (port.Length == 0 && !leagueProcess)
        {
            port = Flag(commandLine, "app-port"); token = Flag(commandLine, "remoting-auth-token");
        }
        return int.TryParse(port, out int number) && number is > 0 and <= 65535 && token.Length > 0
            ? new(number, token) : null;
    }

    internal static RiotAuthState Auth(string status, string body)
    {
        if (status != "200") return RiotAuthState.Unknown;
        try
        {
            using var json = JsonDocument.Parse(body);
            return Text(json.RootElement, "type").ToLowerInvariant() switch
            {
                "authenticated" => RiotAuthState.SignedIn,
                "auth" or "unauthenticated" or "none" => RiotAuthState.SignedOut,
                "multifactor" or "reauth" => RiotAuthState.Verification,
                _ => RiotAuthState.Unknown
            };
        }
        catch (JsonException) { return RiotAuthState.Unknown; }
    }

    internal static string Text(JsonElement root, string field) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Flag(string commandLine, string flag)
    {
        var match = Regex.Match(commandLine, "(?:^|\\s)\"?--" + Regex.Escape(flag) + "=(?:\"([^\"]+)\"|([^\\s\"]+))");
        return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : "";
    }
}
