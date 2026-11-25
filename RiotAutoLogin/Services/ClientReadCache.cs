using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

// Short live snapshots shared by independent monitors. Mutations discard pending as well as completed entries.
internal sealed class ClientReadCache(Func<long>? clock = null)
{
    private readonly object _sync = new();
    private readonly Dictionary<(object Scope, string Path), Entry> _values = new();
    private sealed class Entry
    {
        internal readonly TaskCompletionSource<string[]> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long Expires;
    }
    internal void Clear() { lock (_sync) _values.Clear(); }
    internal async Task<string[]> ReadAsync(object scope, string path, int lifetimeMs, Func<Task<string[]>> load, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Entry entry; bool start = false;
        long now = clock?.Invoke() ?? Environment.TickCount64;
        lock (_sync)
        {
            var key = (scope, path);
            if (!_values.TryGetValue(key, out entry!) || entry.Result.Task.IsCompleted && now >= entry.Expires)
            {
                if (_values.Count > 100) _values.Clear();
                entry = new(); _values[key] = entry; start = true;
            }
        }
        if (start) _ = CompleteAsync(entry);
        return await entry.Result.Task.WaitAsync(token).ConfigureAwait(false);

        async Task CompleteAsync(Entry target)
        {
            try
            {
                var response = await load().ConfigureAwait(false);
                target.Expires = response.Length >= 2 && response[0] == "200" ? (clock?.Invoke() ?? Environment.TickCount64) + lifetimeMs : long.MinValue;
                target.Result.TrySetResult(response);
            }
            catch (Exception ex) { target.Expires = long.MinValue; target.Result.TrySetException(ex); }
        }
    }
}
