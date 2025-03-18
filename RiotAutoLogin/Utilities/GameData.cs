// Add this new file: GameData.cs
using RiotAutoLogin.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RiotAutoLogin
{
    public static class GameData
    {
        public static List<ChampionModel> Champions { get; private set; } = new List<ChampionModel>();
        public static List<SummonerSpellModel> Spells { get; private set; } = new List<SummonerSpellModel>();

        private static bool _isLoading = false;
        private static bool _isLoaded = false;

        public static async Task PreloadAllDataAsync()
        {
            if (_isLoading || _isLoaded)
                return;

            _isLoading = true;

            try
            {
                Debug.WriteLine("Starting data preloading...");

                // CHANGE THIS PART - Use Data Dragon directly instead of LCU
                Debug.WriteLine("Loading all champions from Data Dragon...");
                var champions = await DataDragonService.GetAllChampionsAsync();

                if (champions.Count == 0)
                {
                    Debug.WriteLine("Fallback to hardcoded champion list");
                    champions = GetFallbackChampions();
                }

                Debug.WriteLine($"Loaded {champions.Count} champions total");

                // Make sure "None" is included
                if (!champions.Exists(c => c.Name == "None"))
                {
                    champions.Insert(0, new ChampionModel { Name = "None", Id = -1, IsAvailable = true });
                }

                // Load summoner spells
                var spells = await LCUService.GetSummonerSpellsAsync();
                if (spells.Count == 0)
                {
                    spells = GetFallbackSpells();
                }

                // Get latest Data Dragon version for images
                string version = await DataDragonService.GetLatestVersionAsync();
                Debug.WriteLine($"Using Data Dragon version: {version}");

                // Prepare champion image URLs
                List<string> championImageUrls = new List<string>();
                Dictionary<string, ChampionModel> championsByUrl = new Dictionary<string, ChampionModel>();

                foreach (var champion in champions)
                {
                    if (!string.IsNullOrEmpty(champion.Name) && champion.Name != "None")
                    {
                        string imageUrl = DataDragonService.GetChampionImageUrl(champion.Name, version);
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            champion.ImageUrl = imageUrl;
                            championImageUrls.Add(imageUrl);
                            championsByUrl[imageUrl] = champion;
                        }
                    }
                }

                // Prepare spell image URLs
                List<string> spellImageUrls = new List<string>();
                Dictionary<string, SummonerSpellModel> spellsByUrl = new Dictionary<string, SummonerSpellModel>();

                foreach (var spell in spells)
                {
                    if (!string.IsNullOrEmpty(spell.Name))
                    {
                        string normalizedName = NormalizeSpellName(spell.Name);
                        string imageUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/{normalizedName}.png";
                        spell.ImageUrl = imageUrl;
                        spellImageUrls.Add(imageUrl);
                        spellsByUrl[imageUrl] = spell;
                    }
                }

                // Batch load champion images
                Debug.WriteLine($"Batch loading {championImageUrls.Count} champion images...");
                var championImages = await DataDragonService.BatchDownloadImagesAsync(
                    championImageUrls,
                    (completed, total) => Debug.WriteLine($"Champion images: {completed}/{total}")
                );

                // Assign images to champions
                foreach (var kvp in championImages)
                {
                    if (championsByUrl.TryGetValue(kvp.Key, out var champion))
                    {
                        champion.Image = kvp.Value;
                    }
                }

                // Batch load spell images
                Debug.WriteLine($"Batch loading {spellImageUrls.Count} spell images...");
                var spellImages = await DataDragonService.BatchDownloadImagesAsync(
                    spellImageUrls,
                    (completed, total) => Debug.WriteLine($"Spell images: {completed}/{total}")
                );

                // Assign images to spells
                foreach (var kvp in spellImages)
                {
                    if (spellsByUrl.TryGetValue(kvp.Key, out var spell))
                    {
                        spell.Image = kvp.Value;
                    }
                }

                // Try alternative URLs for spells that failed
                List<string> altSpellImageUrls = new List<string>();
                Dictionary<string, SummonerSpellModel> altSpellsByUrl = new Dictionary<string, SummonerSpellModel>();

                foreach (var spell in spells)
                {
                    if (spell.Image == null && !string.IsNullOrEmpty(spell.Name))
                    {
                        string normalizedName = "Summoner" + spell.Name.Replace(" ", "");
                        string altImageUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/{normalizedName}.png";
                        spell.ImageUrl = altImageUrl;
                        altSpellImageUrls.Add(altImageUrl);
                        altSpellsByUrl[altImageUrl] = spell;
                    }
                }

                if (altSpellImageUrls.Count > 0)
                {
                    Debug.WriteLine($"Loading {altSpellImageUrls.Count} alternative spell images...");
                    var altSpellImages = await DataDragonService.BatchDownloadImagesAsync(altSpellImageUrls);

                    foreach (var kvp in altSpellImages)
                    {
                        if (altSpellsByUrl.TryGetValue(kvp.Key, out var spell))
                        {
                            spell.Image = kvp.Value;
                        }
                    }
                }

                // Save to static properties
                Champions = champions;
                Spells = spells;

                Debug.WriteLine($"Preloaded {Champions.Count} champions and {Spells.Count} spells");
                Debug.WriteLine($"Champion images loaded: {Champions.Count(c => c.Image != null)}");
                Debug.WriteLine($"Spell images loaded: {Spells.Count(s => s.Image != null)}");

                _isLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preloading data: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        private static List<ChampionModel> GetFallbackChampions()
        {
            return new List<ChampionModel>
            {
                new ChampionModel { Name = "Ahri", Id = 103, IsAvailable = true },
                new ChampionModel { Name = "Ashe", Id = 22, IsAvailable = true },
                new ChampionModel { Name = "Garen", Id = 86, IsAvailable = true },
                new ChampionModel { Name = "Annie", Id = 1, IsAvailable = true },
                new ChampionModel { Name = "Master Yi", Id = 11, IsAvailable = true },
                new ChampionModel { Name = "Lux", Id = 99, IsAvailable = true },
                new ChampionModel { Name = "Yasuo", Id = 157, IsAvailable = true },
                new ChampionModel { Name = "Zed", Id = 238, IsAvailable = true },
            };
        }

        private static List<SummonerSpellModel> GetFallbackSpells()
        {
            return new List<SummonerSpellModel>
            {
                new SummonerSpellModel { Name = "Flash", Id = 4, Description = "Teleports your champion a short distance." },
                new SummonerSpellModel { Name = "Ignite", Id = 14, Description = "Ignites target enemy champion, dealing true damage." },
                new SummonerSpellModel { Name = "Heal", Id = 7, Description = "Restores health to your champion and an ally." },
                new SummonerSpellModel { Name = "Teleport", Id = 12, Description = "Teleports to target allied structure or minion." },
                new SummonerSpellModel { Name = "Exhaust", Id = 3, Description = "Slows an enemy champion and reduces their damage." },
                new SummonerSpellModel { Name = "Barrier", Id = 21, Description = "Shields your champion from damage." },
                new SummonerSpellModel { Name = "Cleanse", Id = 1, Description = "Removes all disables and summoner spell debuffs." },
                new SummonerSpellModel { Name = "Smite", Id = 11, Description = "Deals true damage to target monster or minion." }
            };
        }

        private static string NormalizeSpellName(string spellName)
        {
            if (string.IsNullOrEmpty(spellName))
                return string.Empty;

            Dictionary<string, string> spellMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

            if (spellMapping.TryGetValue(spellName, out string normalizedName))
            {
                return normalizedName;
            }

            return "Summoner" + spellName.Replace(" ", "");
        }
    }
}