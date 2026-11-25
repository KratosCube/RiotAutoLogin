using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

// Use Windows' existing mDNS responder. No extra service, package or polling thread is installed.
internal sealed class RemotePickDiscovery : IDisposable
{
    private const uint Pending = 9506;
    private readonly object _sync = new();
    private IntPtr _instance, _request;
    private GCHandle _root;
    private bool _pending, _registered, _disposed, _released;
    private static readonly Completion RegisteredCallback = Registered;
    private static readonly Completion RemovedCallback = Removed;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string HostName { get; } = Environment.MachineName.TrimEnd('.').Split('.')[0].ToLowerInvariant() + ".local";
    internal bool IsRegistered { get { lock (_sync) return _registered && !_disposed; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct Request
    {
        internal uint Version, InterfaceIndex;
        internal IntPtr Instance, Callback, Context, Credentials;
        internal int Unicast;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Completion(uint status, IntPtr context, IntPtr instance);
    [DllImport("dnsapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(string service, string host, IntPtr ip4, IntPtr ip6, ushort port, ushort priority, ushort weight, uint count, IntPtr keys, IntPtr values);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);
    [DllImport("dnsapi.dll", ExactSpelling = true)] private static extern void DnsServiceFreeInstance(IntPtr instance);

    internal async Task<bool> StartAsync(int port)
    {
        if (!OperatingSystem.IsWindows()) return false;
        lock (_sync)
        {
            if (_disposed) return false;
            try
            {
                // Windows advertises address records for its own hostname; arbitrary custom aliases are not reliable.
                _instance = DnsServiceConstructInstance("Remote Pick " + HostName[..^6] + "._http._tcp.local", HostName,
                    IntPtr.Zero, IntPtr.Zero, checked((ushort)port), 0, 0, 0, IntPtr.Zero, IntPtr.Zero);
                if (_instance == IntPtr.Zero) return false;
                _root = GCHandle.Alloc(this);
                _request = Marshal.AllocHGlobal(Marshal.SizeOf<Request>());
                WriteRequest(RegisteredCallback);
                _pending = true;
                if (DnsServiceRegister(_request, IntPtr.Zero) != Pending)
                { _pending = false; Release(); _ready.TrySetResult(false); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
            { _pending = false; Release(); _ready.TrySetResult(false); }
        }
        try { return await _ready.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { Dispose(); return false; }
    }

    private void WriteRequest(Completion callback) => Marshal.StructureToPtr(new Request
    {
        Version = 1, Instance = _instance, Callback = Marshal.GetFunctionPointerForDelegate(callback), Context = GCHandle.ToIntPtr(_root)
    }, _request, false);

    private static void Registered(uint status, IntPtr context, IntPtr instance)
    {
        var self = (RemotePickDiscovery)GCHandle.FromIntPtr(context).Target!;
        lock (self._sync)
        {
            if (instance != IntPtr.Zero && instance != self._instance) DnsServiceFreeInstance(instance);
            self._pending = false; self._registered = status == 0;
            self._ready.TrySetResult(status == 0 && !self._disposed);
            if (self._disposed || status != 0) self.RemoveOrRelease();
        }
    }
    private static void Removed(uint status, IntPtr context, IntPtr instance)
    {
        var self = (RemotePickDiscovery)GCHandle.FromIntPtr(context).Target!;
        lock (self._sync)
        {
            if (instance != IntPtr.Zero && instance != self._instance) DnsServiceFreeInstance(instance);
            self._registered = false; self.Release();
        }
    }
    private void RemoveOrRelease()
    {
        if (_pending) return;
        if (_registered)
        {
            WriteRequest(RemovedCallback);
            if (DnsServiceDeRegister(_request, IntPtr.Zero) == Pending) return;
        }
        Release();
    }
    private void Release()
    {
        if (_released) return;
        _released = true;
        if (_request != IntPtr.Zero) { Marshal.FreeHGlobal(_request); _request = IntPtr.Zero; }
        if (_instance != IntPtr.Zero) { DnsServiceFreeInstance(_instance); _instance = IntPtr.Zero; }
        if (_root.IsAllocated) _root.Free();
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _ready.TrySetResult(false);
            RemoveOrRelease();
        }
    }
}
