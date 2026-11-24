using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace RiotAutoLogin.Models;

public partial class Account
{
    private readonly object _thumbnailLock = new();
    private string? _thumbnailPath;
    private DateTime _thumbnailModified;
    private BitmapImage? _thumbnail;

    // WPF reads this property through an asynchronous binding on every account surface.
    [JsonIgnore]
    public BitmapImage? AvatarThumbnail
    {
        get
        {
            string path = AvatarPath;
            if (string.IsNullOrWhiteSpace(path)) return null;
            lock (_thumbnailLock)
            {
                try
                {
                    var modified = File.GetLastWriteTimeUtc(path);
                    if (_thumbnailPath == path && _thumbnailModified == modified) return _thumbnail;
                    using var file = File.OpenRead(path);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 192;
                    bitmap.StreamSource = file;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _thumbnailPath = path;
                    _thumbnailModified = modified;
                    return _thumbnail = bitmap;
                }
                catch (Exception) { return null; } // Keep the placeholder for missing or invalid images.
            }
        }
    }

    partial void AvatarChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarThumbnail)));
}
