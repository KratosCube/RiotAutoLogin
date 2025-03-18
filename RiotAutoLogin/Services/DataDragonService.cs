using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RiotAutoLogin.Services
{
    public static class DataDragonService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static string _currentVersion = string.Empty;
        private static Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>();
        private static string _cacheFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "ImageCache");
        static DataDragonService()
        {
            // Configure HttpClient
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
        public static async Task<string> GetLatestVersionAsync()
        {
            try
            {
                // Try to get the latest version
                var response = await _httpClient.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    _currentVersion = doc.RootElement[0].GetString();
                    return _currentVersion;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting latest version: {ex.Message}");
                return "13.24.1"; // Use a known working version
            }
        }

        public static async Task<BitmapImage> DownloadImageAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return null;

            try
            {
                // Check memory cache first
                if (_imageCache.TryGetValue(imageUrl, out BitmapImage cachedImage))
                {
                    Debug.WriteLine($"Retrieved image from memory cache: {imageUrl}");
                    return cachedImage;
                }

                // Check disk cache
                string cacheFileName = GetCacheFileName(imageUrl);
                string cachedFilePath = Path.Combine(_cacheFolderPath, cacheFileName);

                if (File.Exists(cachedFilePath))
                {
                    Debug.WriteLine($"Loading image from disk cache: {cachedFilePath}");
                    BitmapImage cachedBitmap = new BitmapImage();

                    using (var stream = new FileStream(cachedFilePath, FileMode.Open, FileAccess.Read))
                    {
                        cachedBitmap.BeginInit();
                        cachedBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        cachedBitmap.StreamSource = stream;
                        cachedBitmap.EndInit();
                        cachedBitmap.Freeze(); // Important for cross-thread access
                    }

                    // Add to memory cache
                    _imageCache[imageUrl] = cachedBitmap;
                    return cachedBitmap;
                }

                // Download if not in cache
                Debug.WriteLine($"Downloading image from: {imageUrl}");
                var bytes = await _httpClient.GetByteArrayAsync(imageUrl);

                // Save to disk cache
                Directory.CreateDirectory(_cacheFolderPath);
                File.WriteAllBytes(cachedFilePath, bytes);
                Debug.WriteLine($"Saved image to disk cache: {cachedFilePath}");

                // Create image from downloaded bytes
                BitmapImage downloadedImage = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    downloadedImage.BeginInit();
                    downloadedImage.CacheOption = BitmapCacheOption.OnLoad;
                    downloadedImage.StreamSource = ms;
                    downloadedImage.EndInit();
                    downloadedImage.Freeze(); // Important for cross-thread access
                }

                // Add to memory cache
                _imageCache[imageUrl] = downloadedImage;
                return downloadedImage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading/caching image: {ex.Message}");
                Debug.WriteLine($"Url: {imageUrl}");
                return null;
            }
        }
        public static async Task<Dictionary<string, BitmapImage>> BatchDownloadImagesAsync(List<string> imageUrls, Action<int, int> progressCallback = null)
        {
            Dictionary<string, BitmapImage> results = new Dictionary<string, BitmapImage>();
            int total = imageUrls.Count;
            int successful = 0;
            int failed = 0;

            // Use semaphore to limit concurrent downloads
            using (SemaphoreSlim semaphore = new SemaphoreSlim(5)) // Max 5 concurrent downloads
            {
                List<Task> downloadTasks = new List<Task>();

                foreach (string url in imageUrls)
                {
                    if (string.IsNullOrEmpty(url))
                        continue;

                    // Check memory cache first
                    if (_imageCache.TryGetValue(url, out BitmapImage cachedImage))
                    {
                        results[url] = cachedImage;
                        successful++;
                        progressCallback?.Invoke(successful + failed, total);
                        continue;
                    }

                    // Check disk cache
                    string cacheFileName = GetCacheFileName(url);
                    string cachedFilePath = Path.Combine(_cacheFolderPath, cacheFileName);

                    if (File.Exists(cachedFilePath))
                    {
                        try
                        {
                            BitmapImage image = new BitmapImage();
                            using (var stream = new FileStream(cachedFilePath, FileMode.Open, FileAccess.Read))
                            {
                                image.BeginInit();
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.StreamSource = stream;
                                image.EndInit();
                                image.Freeze();
                            }

                            _imageCache[url] = image;
                            results[url] = image;
                            successful++;
                            progressCallback?.Invoke(successful + failed, total);
                            continue;
                        }
                        catch
                        {
                            // If loading from cache fails, try downloading again
                        }
                    }

                    // Need to download - add to task list
                    downloadTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // Limit concurrent downloads with semaphore
                            await semaphore.WaitAsync();

                            try
                            {
                                // Add a small delay between requests to avoid rate limiting
                                await Task.Delay(200);

                                // Download the image
                                var bytes = await _httpClient.GetByteArrayAsync(url);

                                // Save to disk cache
                                Directory.CreateDirectory(_cacheFolderPath);
                                File.WriteAllBytes(cachedFilePath, bytes);

                                // Create and cache the image
                                BitmapImage image = new BitmapImage();
                                using (var ms = new MemoryStream(bytes))
                                {
                                    image.BeginInit();
                                    image.CacheOption = BitmapCacheOption.OnLoad;
                                    image.StreamSource = ms;
                                    image.EndInit();
                                    image.Freeze();
                                }

                                _imageCache[url] = image;
                                results[url] = image;
                                successful++;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to download image: {url} - {ex.Message}");
                                failed++;
                            }
                            finally
                            {
                                semaphore.Release();
                                progressCallback?.Invoke(successful + failed, total);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in download task: {ex.Message}");
                            failed++;
                            progressCallback?.Invoke(successful + failed, total);
                        }
                    }));
                }

                // Wait for all downloads to complete
                await Task.WhenAll(downloadTasks);
            }

            Debug.WriteLine($"Batch download complete: {successful} successful, {failed} failed out of {total}");
            return results;
        }
        private static string GetCacheFileName(string url)
        {
            // Create a valid filename from the URL
            string fileName = Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).Replace('/', '_').Replace('+', '-');

            // Extract the extension from the URL if possible
            string extension = ".png";
            if (url.Contains("."))
            {
                extension = url.Substring(url.LastIndexOf('.'));
                if (extension.Length > 5) extension = ".png"; // Fallback to png if extension looks wrong
            }

            return fileName + extension;
        }
        public static string GetChampionImageUrl(string championName, string version)
        {
            if (string.IsNullOrEmpty(championName))
                return null;

            // Normalize champion name for URL
            string normalizedName = NormalizeChampionName(championName);

            if (normalizedName == "None")
                return null;

            return $"https://ddragon.leagueoflegends.com/cdn/{version}/img/champion/{normalizedName}.png";
        }

        public static string GetSummonerSpellImageUrl(string spellName, string version)
        {
            if (string.IsNullOrEmpty(spellName))
                return null;

            // Normalize spell name for URL
            string normalizedName = NormalizeSpellName(spellName);

            return $"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/{normalizedName}.png";
        }

        private static string NormalizeChampionName(string championName)
        {
            if (string.IsNullOrEmpty(championName))
                return string.Empty;

            // Special cases
            switch (championName)
            {
                case "Nunu":
                case "Nunu & Willump":
                    return "Nunu";
                case "Wukong":
                    return "MonkeyKing";
                case "Renata Glasc":
                    return "Renata";
                case "None":
                    return "None"; // Handle the "None" case
                default:
                    break;
            }

            // Remove spaces and special characters
            return championName.Replace(" ", "").Replace("'", "").Replace(".", "");
        }

        // This method is used in other places, so we need to update it
        private static string NormalizeSpellName(string spellName)
        {
            return MapSpellToDataDragonId(spellName);
        }

        public static async Task<string> GetChampionImageUrlAsync(string championName)
        {
            if (string.IsNullOrEmpty(championName))
                return null;

            string version = await GetLatestVersionAsync();
            return GetChampionImageUrl(championName, version);
        }

        public static async Task<string> GetSummonerSpellImageUrlAsync(string spellName)
        {
            if (string.IsNullOrEmpty(spellName))
                return null;

            // Use the correct Data Dragon ID directly
            string datadragonId = MapSpellToDataDragonId(spellName);

            // Use a known working version
            return $"https://ddragon.leagueoflegends.com/cdn/13.24.1/img/spell/{datadragonId}.png";
        }

        public static async Task<List<ChampionModel>> GetAllChampionsAsync()
        {
            try
            {
                string version = await GetLatestVersionAsync();
                string url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/champion.json";

                Debug.WriteLine($"Fetching all champions from Data Dragon: {url}");
                string json = await _httpClient.GetStringAsync(url);

                List<ChampionModel> champions = new List<ChampionModel>();

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var champData = doc.RootElement.GetProperty("data");

                    foreach (var champProp in champData.EnumerateObject())
                    {
                        try
                        {
                            string id = champProp.Value.GetProperty("key").GetString();
                            string name = champProp.Value.GetProperty("name").GetString();
                            int championId = int.Parse(id);

                            champions.Add(new ChampionModel
                            {
                                Name = name,
                                Id = championId,
                                IsAvailable = true // All champions available in selection window
                            });

                            Debug.WriteLine($"Added champion: {name} (ID: {championId})");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error adding champion {champProp.Name}: {ex.Message}");
                        }
                    }
                }

                Debug.WriteLine($"Total champions parsed: {champions.Count}");
                return champions.OrderBy(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching all champions: {ex.Message}");
                return new List<ChampionModel>();
            }
        }

        public static async Task<BitmapImage> GetSpellImageAsync(string spellName)
        {
            try
            {
                // Map spell names to the correct Data Dragon ID
                string datadragonId = MapSpellToDataDragonId(spellName);

                // Hard-code the version and use direct ID mapping
                string imageUrl = $"https://ddragon.leagueoflegends.com/cdn/13.24.1/img/spell/{datadragonId}.png";

                Debug.WriteLine($"Attempting to download spell image with direct mapping: {imageUrl}");

                return await DownloadImageAsync(imageUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting spell image: {ex.Message}");
                return null;
            }
        }
        private static string MapSpellToDataDragonId(string spellName)
        {
            if (string.IsNullOrEmpty(spellName))
                return "SummonerFlash";

            // Direct mapping from any localized name to the proper Data Dragon ID
            Dictionary<string, string> spellMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // English names
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
        { "Mark", "SummonerSnowball" },
        
        // Czech names
        { "Skok", "SummonerFlash" },
        { "Bystrost", "SummonerHaste" },
        { "Duch", "SummonerHaste" },
        { "Bariéra", "SummonerBarrier" },
        { "Očista", "SummonerBoost" },
        { "Hodporem", "SummonerBoost" },
        { "Vyléčit", "SummonerHeal" },
        { "Vznítit", "SummonerDot" },
        { "Vyčerpat", "SummonerExhaust" },
        { "Udeřit", "SummonerSmite" },
        { "Útěk", "SummonerFlash" },
        { "Označení", "SummonerSnowball" },
        
        // Cover any cases where "Summoner" is already prefixed
        { "SummonerSkok", "SummonerFlash" },
        { "SummonerBystrost", "SummonerHaste" },
        { "SummonerDuch", "SummonerHaste" },
        { "SummonerBariéra", "SummonerBarrier" },
        { "SummonerOčista", "SummonerBoost" },
        { "SummonerHodporem", "SummonerBoost" },
        { "SummonerVyléčit", "SummonerHeal" },
        { "SummonerVznítit", "SummonerDot" },
        { "SummonerVyčerpat", "SummonerExhaust" },
        { "SummonerUdeřit", "SummonerSmite" },
        { "SummonerÚtěk", "SummonerFlash" },
        { "SummonerOznačení", "SummonerSnowball" }
    };

            // Try to get the mapping
            if (spellMapping.TryGetValue(spellName, out string mappedId))
                return mappedId;

            // Default to Flash if not found
            return "SummonerFlash";
        }
        private static readonly Dictionary<string, string> SpellImageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    // Map all localized names directly to their image files
    { "Flash", "SummonerFlash.png" },
    { "Ignite", "SummonerDot.png" },
    { "Heal", "SummonerHeal.png" },
    { "Teleport", "SummonerTeleport.png" },
    { "Exhaust", "SummonerExhaust.png" },
    { "Barrier", "SummonerBarrier.png" },
    { "Cleanse", "SummonerBoost.png" },
    { "Smite", "SummonerSmite.png" },
    { "Ghost", "SummonerHaste.png" },
    { "Clarity", "SummonerMana.png" },
    { "Mark", "SummonerSnowball.png" },
    

};
    }
}