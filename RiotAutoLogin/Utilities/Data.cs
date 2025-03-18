// Create a new file called Data.cs
using RiotAutoLogin.Services;
using System.Collections.Generic;

namespace RiotAutoLogin
{
    public static class Data
    {
        public static List<ChampionModel> champsSorterd = new List<ChampionModel>();
        public static List<SummonerSpellModel> spellsSorted = new List<SummonerSpellModel>();
        public static string currentSummonerId = "";

        // Add methods to load champion and spell data
        public static async Task LoadChampionsList()
        {
            // If the list is already populated, return
            if (champsSorterd.Any())
                return;

            // Otherwise load the data from LCU or Data Dragon
            champsSorterd = await LCUService.GetChampionsAsync();
        }

        public static async Task LoadSpellsList()
        {
            // If the list is already populated, return
            if (spellsSorted.Any())
                return;

            // Otherwise load the data from LCU or Data Dragon
            spellsSorted = await LCUService.GetSummonerSpellsAsync();
        }
    }
}