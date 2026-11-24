using System;
using System.Linq;

namespace RiotAutoLogin.Services
{
    public static class RemoteRuneAssets
    {
        public static string ToDataDragonUrl(string? iconPath)
        {
            const string assetPrefix = "/lol-game-data/assets/v1/";
            string path = iconPath?.Trim().Replace('\\', '/') ?? string.Empty;
            if (path.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase))
                path = path[assetPrefix.Length..];

            if (!path.StartsWith("perk-images/", StringComparison.OrdinalIgnoreCase) ||
                !(path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
                return string.Empty;

            string[] segments = path.Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOfAny(new[] { '?', '#', ':' }) >= 0))
                return string.Empty;

            return "https://ddragon.leagueoflegends.com/cdn/img/" + string.Join("/", segments.Select(Uri.EscapeDataString));
        }
    }
}
