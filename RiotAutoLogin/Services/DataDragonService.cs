using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services
{
    public static class DataDragonService
    {
        private static readonly HttpClient _httpClient = new();
        private static readonly Dictionary<string, BitmapImage> _imageCache = new();
        private static readonly Dictionary<string, string> _championNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Nunu & Willump", "Nunu" },
            { "Wukong", "MonkeyKing" },
            { "Renata Glasc", "Renata" },
            { "Vel'Koz", "Velkoz" },
            { "Cho'Gath", "Chogath" },
            { "Kai'Sa", "Kaisa" },
            { "Kha'Zix", "Khazix" },
        };

        private static readonly Dictionary<string, string> _spellNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Flash", "SummonerFlash" },
            { "Ignite", "SummonerDot" },
            { "Heal", "SummonerHeal" },
            { "Teleport", "SummonerTeleport" },
            { "Exhaust", "SummonerExhaust" },
            { "Barrier", "SummonerBarrier" },
            { "Cleanse", "SummonerBoost" },
            { "Smite", "SummonerSmite" },
            { "Ghost", "SummonerHaste" },
            { "Clarity", "SummonerMana" },
            { "Mark", "SummonerSnowball" }
        };

        private static readonly string _cacheFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "ImageCache");

        private static string _currentVersion = "14.1.1"; // Default fallback version

        static DataDragonService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public static async Task<string> GetLatestVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
                using var doc = JsonDocument.Parse(response);
                _currentVersion = doc.RootElement[0].GetString() ?? _currentVersion;
                    return _currentVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting latest version: {ex.Message}");
                return _currentVersion;
            }
        }

        public static async Task<BitmapImage> DownloadImageAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return null!;

            // Check memory cache
            if (_imageCache.TryGetValue(imageUrl, out var cachedImage))
                return cachedImage;

            try
            {
                // Check disk cache
                var cacheFileName = GetCacheFileName(imageUrl);
                var cachedFilePath = Path.Combine(_cacheFolderPath, cacheFileName);

                if (File.Exists(cachedFilePath))
                {
                    var image = LoadImageFromFile(cachedFilePath);
                    if (image != null)
                    {
                        _imageCache[imageUrl] = image;
                        return image;
                    }
                }

                // Download and cache
                var bytes = await _httpClient.GetByteArrayAsync(imageUrl);
                Directory.CreateDirectory(_cacheFolderPath);
                await File.WriteAllBytesAsync(cachedFilePath, bytes);

                var downloadedImage = CreateImageFromBytes(bytes);
                if (downloadedImage != null)
                _imageCache[imageUrl] = downloadedImage;

                return downloadedImage!;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading image {imageUrl}: {ex.Message}");
                return null;
            }
        }

        public static async Task<Dictionary<string, BitmapImage>> BatchDownloadImagesAsync(
            List<string> imageUrls, Action<int, int>? progressCallback = null)
        {
            var results = new Dictionary<string, BitmapImage>();
            var semaphore = new SemaphoreSlim(5); // Limit concurrent downloads
            var completed = 0;

            var tasks = imageUrls.Where(url => !string.IsNullOrEmpty(url)).Select(async url =>
            {
                    // Check memory cache first
                if (_imageCache.TryGetValue(url, out var cached))
                    {
                    results[url] = cached;
                    Interlocked.Increment(ref completed);
                    progressCallback?.Invoke(completed, imageUrls.Count);
                    return;
                }

                            await semaphore.WaitAsync();
                            try
                            {
                    await Task.Delay(100); // Rate limiting
                    var image = await DownloadImageAsync(url);
                    if (image != null)
                                results[url] = image;
                            }
                            finally
                            {
                                semaphore.Release();
                    Interlocked.Increment(ref completed);
                    progressCallback?.Invoke(completed, imageUrls.Count);
                        }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        public static string GetChampionImageUrl(string championName, string? version = null)
        {
            if (string.IsNullOrEmpty(championName)) return string.Empty;
            var normalized = NormalizeChampionName(championName);
            var ver = version ?? _currentVersion;
            return $"https://ddragon.leagueoflegends.com/cdn/{ver}/img/champion/{normalized}.png";
        }

        public static string GetSummonerSpellImageUrl(string spellName, string? version = null)
        {
            if (string.IsNullOrEmpty(spellName)) return string.Empty;
            var normalized = NormalizeSpellName(spellName);
            var ver = version ?? _currentVersion;
            return $"https://ddragon.leagueoflegends.com/cdn/{ver}/img/spell/{normalized}.png";
        }

        public static async Task<string> GetChampionImageUrlAsync(string championName)
        {
            await GetLatestVersionAsync();
            return GetChampionImageUrl(championName, _currentVersion);
        }

        public static async Task<string> GetSummonerSpellImageUrlAsync(string spellName)
        {
            await GetLatestVersionAsync();
            return GetSummonerSpellImageUrl(spellName, _currentVersion);
        }

        public static async Task<List<ChampionModel>> GetAllChampionsAsync()
        {
            try
            {
                var version = await GetLatestVersionAsync();
                var response = await _httpClient.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/champion.json");

                using var doc = JsonDocument.Parse(response);
                var champions = new List<ChampionModel>();

                if (doc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    foreach (var championProperty in dataElement.EnumerateObject())
                    {
                        var champion = championProperty.Value;
                        if (champion.TryGetProperty("name", out var nameElement) &&
                            champion.TryGetProperty("key", out var keyElement))
                        {
                            var name = nameElement.GetString();
                            var key = keyElement.GetString();
                            
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(key))
                            {
                                champions.Add(new ChampionModel
                                {
                                    Name = name,
                                    Id = int.Parse(key),
                                    IsAvailable = true
                                });
                            }
                        }
                    }
                }

                return champions;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting champions: {ex.Message}");
                return new List<ChampionModel>();
            }
        }

        private static string NormalizeChampionName(string championName)
        {
            if (string.IsNullOrEmpty(championName) || championName == "None")
                return championName;

            if (_championNameMap.TryGetValue(championName, out var mapped))
                return mapped;

            return championName.Replace(" ", "").Replace("'", "").Replace(".", "").Replace(":", "");
        }

        private static string NormalizeSpellName(string spellName)
        {
            if (string.IsNullOrEmpty(spellName))
                return string.Empty;

            return _spellNameMap.TryGetValue(spellName, out var mapped) 
                ? mapped 
                : "Summoner" + spellName.Replace(" ", "");
        }

        private static string GetCacheFileName(string url)
        {
            var fileName = Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
                .Replace('/', '_').Replace('+', '-');
            var extension = url.Contains('.') && url.LastIndexOf('.') > url.LastIndexOf('/') 
                ? url[url.LastIndexOf('.')..] 
                : ".png";
            return fileName + (extension.Length > 5 ? ".png" : extension);
        }

        private static BitmapImage? LoadImageFromFile(string filePath)
        {
            try
            {
                var image = new BitmapImage();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage? CreateImageFromBytes(byte[] bytes)
    {
            try
            {
                var image = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}