using System.Collections.Generic;

namespace RiotAutoLogin.Models
{
    public class RemotePickState
    {
        public string Phase { get; set; } = "None";
        public bool IsInChampSelect { get; set; }
        public bool IsMyTurn { get; set; }
        public bool CanPick => IsMyTurn && ActionType == "pick";
        public bool CanBan => IsMyTurn && ActionType == "ban";
        public bool CanLeave { get; set; }
        public string ActionId { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int SelectedChampionId { get; set; }
        public int PickIntentChampionId { get; set; }
        public int Spell1Id { get; set; }
        public int Spell2Id { get; set; }
        public List<int> BannedChampionIds { get; set; } = new();
        public List<int> PickedChampionIds { get; set; } = new();
        public List<int> AvailableSpellIds { get; set; } = new();
        public List<RemoteChampionDto> Champions { get; set; } = new();
        public List<RemoteSummonerSpellDto> SummonerSpells { get; set; } = new();
    }

    public class RemoteChampionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsBanned { get; set; }
        public bool IsPicked { get; set; }
        public bool IsSelected { get; set; }
        public bool IsIntent { get; set; }
        public bool IsDisabled => IsBanned || IsPicked;
    }

    public class RemoteSummonerSpellDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsSpell1 { get; set; }
        public bool IsSpell2 { get; set; }
        public bool IsAvailable { get; set; } = true;
    }

    public class RemotePickRequest
    {
        public int ChampionId { get; set; }
    }

    public class RemoteSpellRequest
    {
        public int SpellId { get; set; }
        public int Slot { get; set; }
    }

    public class RemotePickActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
