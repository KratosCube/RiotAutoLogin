public class AutoPickSettings
{
    public string PickChampionName { get; set; } = "Select Champion";
    public int PickChampionId { get; set; }
    public string SecondaryChampionName { get; set; } = "Select Champion";
    public int SecondaryChampionId { get; set; }
    public string BanChampionName { get; set; } = "Select Champion";
    public int BanChampionId { get; set; }
    public string SummonerSpell1Name { get; set; } = "Select Spell";
    public int SummonerSpell1Id { get; set; }
    public string SummonerSpell2Name { get; set; } = "Select Spell";
    public int SummonerSpell2Id { get; set; }

    // Timing settings
    public int PickHoverDelayMs { get; set; } = 1000;
    public int PickLockDelayMs { get; set; } = 2000;
    public int BanHoverDelayMs { get; set; } = 1000;
    public int BanLockDelayMs { get; set; } = 2000;

    // Feature toggles
    public bool AutoPickEnabled { get; set; }
    public bool AutoBanEnabled { get; set; }
    public bool AutoSpellsEnabled { get; set; }
    public bool InstantLock { get; set; }
    public bool InstantBan { get; set; }
}