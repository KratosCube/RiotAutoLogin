using System.Collections.Generic;

namespace RiotAutoLogin.Models
{
    public class RemotePickState
    {
        public string Phase { get; set; } = "None";
        public string ChampSelectPhase { get; set; } = string.Empty;
        public string PhaseLabel { get; set; } = "Waiting";
        public int TimeLeftInPhaseMs { get; set; } = -1;
        public int TotalTimeInPhaseMs { get; set; } = -1;
        public bool IsTimerInfinite { get; set; }
        public bool IsInChampSelect { get; set; }
        public bool IsMyTurn { get; set; }
        public bool CanPick => IsMyTurn && ActionType == "pick";
        public bool CanBan => IsMyTurn && ActionType == "ban";
        public bool CanLeave { get; set; }
        public string ActionId { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string PickActionId { get; set; } = string.Empty;
        public int ActionGroupIndex { get; set; } = -1;
        public string TimerActionKey { get; set; } = string.Empty;
        public string AssignedPosition { get; set; } = string.Empty;
        public int MapId { get; set; }
        public int QueueId { get; set; }
        public string QueueName { get; set; } = string.Empty;
        public string ChampSelectMode { get; set; } = string.Empty;
        public bool IsRandomChampionMode { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SelectedChampionId { get; set; }
        public int PickIntentChampionId { get; set; }
        public int Spell1Id { get; set; }
        public int Spell2Id { get; set; }
        public List<int> BannedChampionIds { get; set; } = new();
        public List<int> PickedChampionIds { get; set; } = new();
        public List<int> AvailableSpellIds { get; set; } = new();
        public List<int> AvailableChampionIds { get; set; } = new();
        public List<RemoteChampionDto> Champions { get; set; } = new();
        public List<RemoteSummonerSpellDto> SummonerSpells { get; set; } = new();
        public List<RemoteRunePageDto> RunePages { get; set; } = new();
        public List<RemoteRecommendedRunePageDto> RecommendedRunePages { get; set; } = new();
        public RemoteRunePageDto? CurrentRunePage { get; set; }
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
        public bool IsAvailableInCurrentMode { get; set; } = true;
        public bool IsDisabled => IsBanned || IsPicked || !IsAvailableInCurrentMode;
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

    public class RemoteRunePageDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public bool IsEditable { get; set; }
        public bool IsDeletable { get; set; }
    }

    public class RemoteRecommendedRunePageDto
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public int PrimaryStyleId { get; set; }
        public int SubStyleId { get; set; }
        public List<int> SelectedPerkIds { get; set; } = new();
        public bool CanApply => PrimaryStyleId > 0 && SubStyleId > 0 && SelectedPerkIds.Count > 0;
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

    public class RemoteRunePageRequest
    {
        public long PageId { get; set; }
    }

    public class RemoteRecommendedRunePageRequest
    {
        public int Index { get; set; }
    }

    public class RemotePickActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
