using System.Diagnostics;
using System.IO;
using RiotAutoLogin.Services;

internal static class ProcessSwitchChecks
{
    // Disposable apphost copies simulate only the process names, never a signed-in Riot session.
    internal static async Task RunAsync()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return;
        if (RiotLocalSession.AnyClientOpen()) throw new Exception("The isolated process test requires no existing Riot clients.");
        string own = Environment.ProcessPath!;
        string folder = Path.GetDirectoryName(own)!;
        var started = new List<Process>();
        var files = new List<string>();
        try
        {
            var riot = await Start("RiotClientUx");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (!await new RiotLocalSession().StopClientsAsync(timeout.Token) || !riot.HasExited)
                throw new Exception("The restart fallback did not terminate the disposable Riot client without auth credentials.");
            var guardedRiot = await Start("RiotClientUx");
            var game = await Start("League of Legends");
            if (await new RiotLocalSession().StopClientsAsync(timeout.Token) || guardedRiot.HasExited || game.HasExited)
                throw new Exception("The restart fallback interrupted a running game.");
            Console.WriteLine("Windows process checks passed: unknown-auth Riot client is stopped; a running game prevents shutdown.");
        }
        finally
        {
            foreach (var process in started)
            {
                try { if (!process.HasExited) process.Kill(); process.WaitForExit(3000); }
                finally { process.Dispose(); }
            }
            foreach (string file in files.Distinct()) if (File.Exists(file)) File.Delete(file);
        }
        async Task<Process> Start(string name)
        {
            string path = Path.Combine(folder, name + ".exe");
            if (!files.Contains(path))
            {
                if (File.Exists(path)) throw new Exception("Refusing to overwrite an existing process fixture.");
                File.Copy(own, path); files.Add(path);
            }
            string ready = Path.Combine(folder, Guid.NewGuid() + ".ready"); files.Add(ready);
            var info = new ProcessStartInfo(path) { UseShellExecute = false };
            info.ArgumentList.Add("--client-stub"); info.ArgumentList.Add(ready);
            var process = Process.Start(info)!; started.Add(process);
            for (int i = 0; i < 200 && !File.Exists(ready) && !process.HasExited; i++) await Task.Delay(50);
            if (!File.Exists(ready)) throw new Exception("The process fixture did not start.");
            return process;
        }
    }
}
