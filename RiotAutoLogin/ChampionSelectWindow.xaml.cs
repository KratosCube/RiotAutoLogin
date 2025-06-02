using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using RiotAutoLogin.Utilities;

namespace RiotAutoLogin
{
    public partial class ChampionSelectWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly bool _isChampionSelect;
        public dynamic? SelectedItem { get; private set; }

        private List<dynamic> _allItems = new List<dynamic>();
        private List<dynamic> _filteredItems = new List<dynamic>();
        private bool _showAvailableOnly = true;

        public ChampionSelectWindow(bool isChampionSelect, string title)
        {
            InitializeComponent();
            DataContext = this;

            _isChampionSelect = isChampionSelect;
            Title = title;

            Loaded += ChampionSelectWindow_Loaded;
        }

        private async void ChampionSelectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isChampionSelect)
            {
                await LoadChampionsAsync();
            }
            else
            {
                await LoadSummonerSpellsAsync();
            }
        }

        private async Task LoadChampionsAsync()
        {
            try
            {
                Debug.WriteLine("Loading champions for selection window...");

                // Use preloaded data if available
                if (GameData.Champions.Count > 0)
                {
                    Debug.WriteLine($"Using {GameData.Champions.Count} preloaded champions");
                    _allItems = GameData.Champions.Cast<dynamic>().ToList();
                    Debug.WriteLine($"Added {_allItems.Count} champions to all items");
                }
                else
                {
                    // Original loading logic as fallback
                    Debug.WriteLine("No preloaded champions, loading from Data Dragon directly...");

                    // Use Data Dragon directly
                    var champions = await DataDragonService.GetAllChampionsAsync();
                    Debug.WriteLine($"Loaded {champions.Count} champions from Data Dragon");

                    if (champions.Count == 0)
                    {
                        champions = GetFallbackChampions();
                        Debug.WriteLine($"Using fallback list with {champions.Count} champions");
                    }

                    // Add "None" option
                    if (!champions.Exists(c => c.Name == "None"))
                    {
                        champions.Insert(0, new ChampionModel { Name = "None", Id = -1, IsAvailable = true });
                    }

                    // Get latest version
                    string version = await DataDragonService.GetLatestVersionAsync();
                    Debug.WriteLine($"Using Data Dragon version: {version}");

                    // Load images for champions
                    foreach (var champion in champions)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(champion.Name) && champion.Name != "None")
                            {
                                string imageUrl = DataDragonService.GetChampionImageUrl(champion.Name, version);
                                if (!string.IsNullOrEmpty(imageUrl))
                                {
                                    champion.ImageUrl = imageUrl;
                                    champion.Image = await DataDragonService.DownloadImageAsync(imageUrl);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading image for champion {champion.Name}: {ex.Message}");
                        }
                    }

                    _allItems = champions.Cast<dynamic>().ToList();
                }

                FilterItems();
                Debug.WriteLine("Finished loading champions");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadChampionsAsync: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to load champions: {ex.Message}", "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }
        private string NormalizeChampionNameForDataDragon(string championName)
        {
            if (string.IsNullOrEmpty(championName))
                return string.Empty;

            // Special cases
            switch (championName)
            {
                case "Nunu & Willump":
                case "Nunu":
                    return "Nunu";
                case "Wukong":
                    return "MonkeyKing";
                case "Renata Glasc":
                    return "Renata";
                case "None":
                    return "None";
                    // Add more special cases as needed
            }

            // Remove spaces, apostrophes, dots and other special characters
            string normalized = championName
                .Replace(" ", "")
                .Replace("'", "")
                .Replace(".", "")
                .Replace(":", "");

            // Handle other special transformations
            if (normalized.Contains("Vel"))
                normalized = "Velkoz";
            if (normalized.Contains("Cho"))
                normalized = "Chogath";
            if (normalized.Contains("Kai"))
                normalized = "Kaisa";
            if (normalized.Contains("Kha"))
                normalized = "Khazix";

            return normalized;
        }
        private async Task LoadSummonerSpellsAsync()
        {
            try
            {
                Debug.WriteLine("Loading summoner spells for selection window...");

                // Use preloaded data if available
                if (GameData.Spells.Count > 0)
                {
                    Debug.WriteLine($"Using {GameData.Spells.Count} preloaded summoner spells");
                    _allItems = GameData.Spells.Cast<dynamic>().ToList();
                }
                else
                {
                    // Original loading logic as fallback
                    Debug.WriteLine("No preloaded spells, loading on demand...");
                    List<SummonerSpellModel> spells;

                    try
                    {
                        spells = await LCUService.GetSummonerSpellsAsync();
                        Debug.WriteLine($"Loaded {spells.Count} spells from LCU");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to get spells from LCU: {ex.Message}");
                        spells = GetFallbackSpells();
                        Debug.WriteLine($"Using fallback with {spells.Count} spells");
                    }

                    // Get the latest Data Dragon version
                    string ddVersion = await DataDragonService.GetLatestVersionAsync();
                    Debug.WriteLine($"Using Data Dragon version for spells: {ddVersion}");

                    // Process images from Data Dragon
                    foreach (var spell in spells)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(spell.Name))
                            {
                                string normalizedName = NormalizeSpellName(spell.Name);
                                string imageUrl = $"https://ddragon.leagueoflegends.com/cdn/{ddVersion}/img/spell/{normalizedName}.png";
                                spell.ImageUrl = imageUrl;
                                spell.Image = await DataDragonService.DownloadImageAsync(imageUrl);

                                if (spell.Image == null)
                                {
                                    normalizedName = "Summoner" + spell.Name.Replace(" ", "");
                                    imageUrl = $"https://ddragon.leagueoflegends.com/cdn/{ddVersion}/img/spell/{normalizedName}.png";
                                    spell.ImageUrl = imageUrl;
                                    spell.Image = await DataDragonService.DownloadImageAsync(imageUrl);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading image for spell {spell.Name}: {ex.Message}");
                        }
                    }

                    _allItems = spells.Cast<dynamic>().ToList();
                }

                FilterItems();
                Debug.WriteLine($"Finished loading summoner spells");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadSummonerSpellsAsync: {ex.Message}");
                MessageBox.Show($"Failed to load summoner spells. Please check your internet connection.\n\nError: {ex.Message}",
                    "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private string NormalizeChampionName(string championName)
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

        // Helper method for spell name normalization
        private string NormalizeSpellName(string spellName)
        {
            if (string.IsNullOrEmpty(spellName))
                return string.Empty;

            // More comprehensive mapping table
            Dictionary<string, string> spellMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Heal", "SummonerHeal" },
        { "Ghost", "SummonerHaste" },
        { "Barrier", "SummonerBarrier" },
        { "Exhaust", "SummonerExhaust" },
        { "Flash", "SummonerFlash" },
        { "Teleport", "SummonerTeleport" },
        { "Cleanse", "SummonerBoost" },
        { "Ignite", "SummonerDot" },
        { "Smite", "SummonerSmite" },
        { "Mark", "SummonerSnowball" }, // For ARAM
        { "Clarity", "SummonerMana" },
        { "Clairvoyance", "SummonerClairvoyance" },
        { "Garrison", "SummonerPoroRecall" },
        { "Poro Toss", "SummonerPoroThrow" },
        { "To the King!", "SummonerSiegeChampSelect" }
    };

            if (spellMapping.TryGetValue(spellName, out string normalizedName))
            {
                return normalizedName;
            }

            // If no exact match, try to remove spaces and special characters
            string simplified = spellName.Replace(" ", "").Replace("'", "").Replace(".", "");

            // Check if any key contains our simplified name
            foreach (var kvp in spellMapping)
            {
                if (kvp.Key.Replace(" ", "").Equals(simplified, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            // If all else fails, return the original but with "Summoner" prefix if missing
            return spellName.StartsWith("Summoner", StringComparison.OrdinalIgnoreCase)
                ? spellName
                : "Summoner" + spellName;
        }

        // Fallback methods in case LCU connection fails
        private List<ChampionModel> GetFallbackChampions()
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
                // Add more common champions here
            };
        }

        private List<SummonerSpellModel> GetFallbackSpells()
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

        private void FilterItems()
        {
            string searchText = txtSearch.Text.ToLower();
            Debug.WriteLine($"Filtering items with search: '{searchText}', available only: {_showAvailableOnly}");
            Debug.WriteLine($"All items count before filter: {_allItems.Count}");

            try
            {
                _filteredItems = _allItems
                    .Where(item =>
                    {
                        try
                        {
                            bool matchesSearch = string.IsNullOrEmpty(searchText) ||
                                              item.Name.ToLower().Contains(searchText);

                            bool isAvailable = true;
                            // IMPORTANT CHANGE: Don't filter unavailable champions by default
                            if (_isChampionSelect && _showAvailableOnly && item is ChampionModel champion)
                            {
                                isAvailable = true; // Consider all champions available
                            }

                            return matchesSearch && isAvailable;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error filtering item: {ex.Message}");
                            return true; // Include items that cause errors
                        }
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FilterItems: {ex.Message}");
                _filteredItems = _allItems.ToList(); // Fallback to showing all
            }

            Debug.WriteLine($"Filtered items count: {_filteredItems.Count}");
            icItems.ItemsSource = _filteredItems;
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterItems();
        }

        private void tglAvailableOnly_Click(object sender, RoutedEventArgs e)
        {
            _showAvailableOnly = tglAvailableOnly.IsChecked ?? false;
            FilterItems();
        }

        private void Item_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag != null)
            {
                var item = element.Tag;
                SelectedItem = item;

                // Log selection for debugging - with type checking
                string itemName = "Unknown";
                bool hasImage = false;

                // Safely check properties based on type
                if (item is ChampionModel champion)
                {
                    itemName = champion.Name;
                    hasImage = champion.Image != null;
                }
                else if (item is SummonerSpellModel spell)
                {
                    itemName = spell.Name;
                    hasImage = spell.Image != null;
                }

                Debug.WriteLine($"Selected item: {itemName}, Has image: {hasImage}");

                // Clear highlighting from all border elements
                var allBorders = FindVisualChildren<Border>(icItems);
                foreach (var containerBorder in allBorders)
                {
                    // Only reset borders that have a Tag (our item containers)
                    if (containerBorder.Tag != null)
                    {
                        containerBorder.BorderBrush = null;
                        containerBorder.BorderThickness = new Thickness(0);
                    }
                }

                // Highlight the selected border
                if (element is Border selectedBorder)
                {
                    selectedBorder.BorderBrush = new SolidColorBrush(
                        Color.FromRgb(255, 82, 82));
                    selectedBorder.BorderThickness = new Thickness(2);
                }

                e.Handled = true;
            }
        }

        // Helper method to find all visual children of a specific type
        private List<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            List<T> results = new List<T>();
            FindVisualChildrenRecursive<T>(parent, results);
            return results;
        }

        private void FindVisualChildrenRecursive<T>(DependencyObject parent, List<T> results) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    results.Add(typedChild);
                }

                FindVisualChildrenRecursive<T>(child, results);
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            SelectedItem = null;
            DialogResult = false;
            Close();
        }

        private void btnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem != null)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select an item first.", "No Selection",
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}