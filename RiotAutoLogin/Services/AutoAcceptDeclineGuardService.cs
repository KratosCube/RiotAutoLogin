using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class AutoAcceptDeclineGuardService
    {
        private static readonly object SyncRoot = new();
        private static CancellationTokenSource? _cts;
        private static Task? _monitorTask;
        private static bool _manualDeclineActive;

        public static void Start()
        {
            lock (SyncRoot)
            {
                if (_cts != null)
                    return;

                _cts = new CancellationTokenSource();
                _monitorTask = Task.Run