using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace RiotAutoLogin.Services
{
    public static class AlertSoundService
    {
        private static readonly TimeSpan PlaybackTimeout = TimeSpan.FromSeconds(90);

        public static async Task<bool> TryPlayAsync(Dispatcher dispatcher, string? mediaPath)
        {
            if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
                return false;

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MediaPlayer? player = null;
            bool finished = false;

            await dispatcher.InvokeAsync(() =>
            {
                player = new MediaPlayer();

                void Finish(bool success)
                {
                    if (finished)
                        return;

                    finished = true;
                    try { player?.Close(); } catch { }
                    completion.TrySetResult(success);
                }

                player.MediaEnded += (_, _) => Finish(true);
                player.MediaFailed += (_, args) =>
                {
                    Debug.WriteLine($"Custom alert sound failed: {args.ErrorException?.Message}");
                    Finish(false);
                };

                try
                {
                    player.Open(new Uri(mediaPath, UriKind.Absolute));
                    player.Play();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not open custom alert sound: {ex.Message}");
                    Finish(false);
                }
            });

            Task completed = await Task.WhenAny(completion.Task, Task.Delay(PlaybackTimeout));
            if (completed == completion.Task)
                return await completion.Task;

            await dispatcher.InvokeAsync(() =>
            {
                if (finished)
                    return;

                finished = true;
                try { player?.Close(); } catch { }
            });

            Debug.WriteLine($"Custom alert sound exceeded {PlaybackTimeout.TotalSeconds:0} seconds and was stopped.");
            return true;
        }
    }
}
