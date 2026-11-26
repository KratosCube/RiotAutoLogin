using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using RiotAutoLogin.Services;

internal static class RemoteServerChecks
{
    internal static async Task RunAsync()
    {
        const string unicodeBody = "{\"message\":\"Ahoj, žluťoučký 😀\"}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(unicodeBody);
        using (var wire = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(unicodeBody + "NEXT")))
        using (var reader = new System.IO.StreamReader(wire, System.Text.Encoding.Latin1))
        {
            if (await RemotePickServerService.ReadBodyAsync(reader, bytes.Length, default) != unicodeBody || await reader.ReadToEndAsync() != "NEXT")
                throw new Exception("HTTP body reading did not respect UTF-8 byte lengths and request boundaries.");
        }
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
        using var unicodeRequest = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/chat");
        unicodeRequest.Headers.Add("X-Remote-Pick-Request", "1");
        unicodeRequest.Content = new StringContent("{\"message\":\"Ahoj, žluťoučký 😀 " + new string('x', 501) + "\"}", System.Text.Encoding.UTF8, "application/json");
        using var unicodeResponse = await http.SendAsync(unicodeRequest);
        if (!unicodeResponse.IsSuccessStatusCode || !(await unicodeResponse.Content.ReadAsStringAsync()).Contains("500 characters"))
            throw new Exception("A Unicode chat request did not finish with its expected validation result.");
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
        Console.WriteLine("Remote Pick Unicode HTTP framing, dual-stack HTTP, local-host validation, cross-origin protection and cancellable shutdown checks passed.");
    }
}
