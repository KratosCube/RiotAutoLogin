using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

// Local authenticated client only. Never send account passwords to these endpoints or log their response bodies.
internal sealed class RiotLocalSession
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        UseProxy = false, AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    }) { Timeout = TimeSpan.FromSeconds(2) };
    private RiotLocalConnection? _connection;
    private bool _discovered;
    private DateTime _nextDiscovery;
    private static readonly string[] ClientNames = ["RiotClientUxRender", "RiotClientUx", "Riot Client", "RiotClientCrashHandler", "LeagueClientUxRender", "LeagueClientUx", "LeagueClient", "RiotClientServices"];

    internal static bool AnyClientOpen() => ClientNames.Any(Running);
    private static bool Running(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    internal async Task<RiotSessionSnapshot> ReadAsync(CancellationToken token)
    {
        bool game = Running("League of Legends") || Running("VALORANT-Win64-Shipping");
        bool league = await Task.Run(LCUService.CheckIfLeagueClientIsOpen, token).ConfigureAwait(false);
        string phase = "", region = "", name = "", tag = "";
        if (league)
        {
            var phaseRead = ReadField("lol-gameflow/v1/session", "phase", token);
            var regionRead = ReadField("riotclient/region-locale", "region", token);
            var summonerRead = LCUService.ClientRequestAsync("GET", "lol-summoner/v1/current-summoner", cancellationToken: token);
            await Task.WhenAll(phaseRead, regionRead, summonerRead).ConfigureAwait(false);
            phase = await phaseRead.ConfigureAwait(false);
            region = await regionRead.ConfigureAwait(false);
            var summoner = await summonerRead.ConfigureAwait(false);
            if (summoner[0] == "200")
            {
                try
                {
                    using var json = JsonDocument.Parse(summoner[1]);
                    name = RiotLocalProtocol.Text(json.RootElement, "gameName");
                    tag = RiotLocalProtocol.Text(json.RootElement, "tagLine");
                }
                catch (JsonException) { }
            }
        }
        if (!_discovered || (_connection == null && DateTime.UtcNow >= _nextDiscovery))
        {
            _nextDiscovery = DateTime.UtcNow.AddSeconds(3);
            _connection = await Task.Run(Discover, token).ConfigureAwait(false);
            _discovered = true;
        }
        var response = await Request("GET", "rso-auth/v1/session", token).ConfigureAwait(false);
        var auth = RiotLocalProtocol.Auth(response[0], response[1]);
        if (_connection == null && !AnyClientOpen()) auth = RiotAuthState.SignedOut;
        if (region.Length == 0 && auth == RiotAuthState.SignedIn)
        {
            var authorization = await Request("GET", "rso-auth/v1/authorization", token).ConfigureAwait(false);
            if (authorization[0] == "200")
            {
                try { using var json = JsonDocument.Parse(authorization[1]); region = RiotLocalProtocol.Text(json.RootElement, "currentPlatformId"); }
                catch (JsonException) { }
            }
        }
        return new(auth, region, name, tag, phase, game, league);
    }

    internal async Task<bool> LogoutAsync(CancellationToken token)
    {
        var current = await ReadAsync(token).ConfigureAwait(false);
        if (!current.CanLeave) return false;
        if (current.LeagueOpen && await ReadField("lol-gameflow/v1/session", "phase", token).ConfigureAwait(false) is not ("None" or "Lobby" or "EndOfGame"))
            return false;
        var response = await Request("DELETE", "rso-auth/v1/session", token).ConfigureAwait(false);
        if (response[0].StartsWith("2", StringComparison.Ordinal)) return true;
        if (current.LeagueOpen)
        {
            var local = await LCUService.ClientRequestAsync("DELETE", "lol-login/v1/session", cancellationToken: token).ConfigureAwait(false);
            return local[0].StartsWith("2", StringComparison.Ordinal);
        }
        return false;
    }

    internal async Task<bool> StopClientsAsync(CancellationToken token)
    {
        // Restart must also work when the auth endpoint is unsupported. Check the game and phase independently.
        bool league = Running("LeagueClientUx") || Running("LeagueClient");
        var state = new RiotSessionSnapshot(RiotAuthState.Unknown,
            Phase: league ? await ReadField("lol-gameflow/v1/session", "phase", token).ConfigureAwait(false) : "",
            GameRunning: Running("League of Legends") || Running("VALORANT-Win64-Shipping"), LeagueOpen: league);
        if (!state.CanRestart) return false;
        List<(int Id, long Started)> CaptureClients()
        {
            var identities = new List<(int Id, long Started)>();
            foreach (string name in ClientNames)
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try { identities.Add((process.Id, process.StartTime.ToUniversalTime().Ticks)); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            return identities;
        }
        var captured = CaptureClients();
        if (state.LeagueOpen) await LCUService.ClientRequestAsync("POST", "process-control/v1/process/quit", cancellationToken: token).ConfigureAwait(false);
        await Request("POST", "process-control/v1/process/quit", token).ConfigureAwait(false);
        for (int i = 0; i < 8 && captured.Any(Alive); i++) await Task.Delay(250, token).ConfigureAwait(false);
        if (Running("League of Legends") || Running("VALORANT-Win64-Shipping")) return false;
        for (int pass = 0; pass < 3; pass++)
        {
            // Services can briefly respawn a renderer during shutdown. Re-capture only known client names.
            if (pass > 0) captured = CaptureClients();
            foreach (var identity in captured.Where(Alive))
            {
                token.ThrowIfCancellationRequested();
                if (Running("League of Legends") || Running("VALORANT-Win64-Shipping")) return false;
                try
                {
                    using var process = Process.GetProcessById(identity.Id);
                    if (process.StartTime.ToUniversalTime().Ticks == identity.Started) process.Kill();
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { return false; }
            }
            for (int i = 0; i < 6 && captured.Any(Alive); i++) await Task.Delay(250, token).ConfigureAwait(false);
            if (!AnyClientOpen()) break;
            await Task.Delay(250, token).ConfigureAwait(false);
        }
        _connection = null; _discovered = false;
        return !captured.Any(Alive) && !AnyClientOpen();
    }

    private static bool Alive((int Id, long Started) identity)
    {
        try { using var p = Process.GetProcessById(identity.Id); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == identity.Started; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    private async Task<string[]> Request(string method, string path, CancellationToken token)
    {
        if (_connection == null) return ["999", ""];
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"https://127.0.0.1:{_connection.Port}/{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + _connection.Token)));
            using var response = await Client.SendAsync(request, token).ConfigureAwait(false);
            return [((int)response.StatusCode).ToString(), await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return ["999", ""]; }
        catch (OperationCanceledException) { return ["999", ""]; }
    }

    private static RiotLocalConnection? Discover()
    {
        foreach (string name in new[] { "LeagueClientUx", "RiotClientUx", "RiotClientServices", "Riot Client" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id);
                        searcher.Options.Timeout = TimeSpan.FromSeconds(2);
                        using var rows = searcher.Get();
                        foreach (ManagementObject row in rows)
                        {
                            using (row)
                            {
                                var connection = RiotLocalProtocol.Connection(row["CommandLine"]?.ToString() ?? "", name == "LeagueClientUx");
                                if (connection != null) return connection;
                            }
                        }
                    }
                    catch (ManagementException) { }
                    catch (System.Runtime.InteropServices.COMException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return null;
    }

    private static async Task<string> ReadField(string path, string field, CancellationToken token)
    {
        var response = await LCUService.ClientRequestAsync("GET", path, cancellationToken: token).ConfigureAwait(false);
        if (response[0] != "200") return "";
        try { using var json = JsonDocument.Parse(response[1]); return RiotLocalProtocol.Text(json.RootElement, field); }
        catch (JsonException) { return ""; }
    }
}
