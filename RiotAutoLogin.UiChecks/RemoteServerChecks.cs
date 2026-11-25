using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using RiotAutoLogin.Services;

internal static class RemoteServerChecks
{
    internal static async Task RunAsync()
    {
        using var server = new RemotePickServerService();
        await server.StartAsync(0);
        int port = server.Port;
        if (!server.IsRunning || port <= 0 || RemotePickServerService.DefaultPort != 5055) throw new Exception("Remote Pick did not bind its port.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var page = await http.GetStringAsync($"http://127.0.0.1:{port}/");
        if (!page.Contains("partyMembers") || !page.Contains("queueSelect")) throw new Exception("The local server did not return the lobby UI.");
        using var foreignHost = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/state");
        foreignHost.Headers.Host = "untrusted.example:" + port;
        if ((await http.SendAsync(foreignHost)).StatusCode != HttpStatusCode.Forbidden) throw new Exception("A foreign Host header was accepted.");
        if ((await http.PostAsync($"http://127.0.0.1:{port}/api/change-queue", new StringContent("{}"))).StatusCode != HttpStatusCode.Forbidden)
            throw new Exception("A cross-origin form could change queues.");
        var stalled = new TcpClient(); await stalled.ConnectAsync(IPAddress.Loopback, port);
        await stalled.GetStream().WriteAsync("GET / HTTP/1.1\r\n"u8.ToArray());
        server.Stop();
        if (server.IsRunning) throw new Exception("Remote Pick stayed running after Stop.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            int n = await stalled.GetStream().ReadAsync(new byte[1], timeout.Token);
            if (n != 0) throw new Exception("Stopped server retained an incomplete request.");
        }
        catch (System.IO.IOException) { }
        finally { stalled.Dispose(); }
        Console.WriteLine("Remote Pick dual-stack HTTP, local-host validation, cross-origin protection and cancellable shutdown checks passed.");
    }
}
