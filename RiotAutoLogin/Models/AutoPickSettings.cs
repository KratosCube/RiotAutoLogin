public class AutoPickSettings
{
    public int PickChampionId { get; set; }
    public string PickChampionName { get; set; }
    public int SecondaryChampionId { get; set; }
    public string SecondaryChampionName { get; set; }
    public int BanChampionId { get; set; }
    public string BanChampionName { get; set; }
    public int SummonerSpell1Id { get; set; }
    public string SummonerSpell1Name { get; set; }
    public int SummonerSpell2Id { get; set; }
    public string SummonerSpell2Name { get; set; }

    // Timing settings
    public int PickHoverDelayMs { get; set; } = 0;
    public int PickLockDelayMs { get; set; } = 1000;
    public int BanHoverDelayMs { get; set; } = 0;
    public int BanLockDelayMs { get; set; } = 1000;

    // Feature toggles
    public bool AutoPickEnabled { get; set; } = false;
    public bool AutoBanEnabled { get; set; } = false;
    public bool AutoSpellsEnabled { get; set; } = false;
    public bool InstantLock { get; set; } = false;
    public bool InstantBan { get; set; } = false;
}