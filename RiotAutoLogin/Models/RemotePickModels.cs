using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace RiotAutoLogin.Models
{
    public class RemotePickState
    {
        private List<RemoteChampionDto> _champions = new();

        public string Phase { get; set; } = "None";
        public string ChampSelectPhase { get; set; } = string.Empty;
        public string PhaseLabel { get; set; } = "Waiting";
        public int TimeLeftInPhaseMs { get; set; } = -1;
        public int TotalTimeInPhaseMs { get; set; } = -1;
        public double PhaseElapsedSeconds { get; set; } = -1;
        public long LiveSampleTickMs { get; } = System.Environment.TickCount64;
        [JsonIgnore] public long TimerMeasuredTickMs { get; set; } = System.Environment.TickCount64;
        public long TimerSampleAgeMs => System.Math.Max(0, System.Environment.TickCount64 - TimerMeasuredTickMs);
        public bool IsTimerInfinite { get; set; }
        public bool IsInChampSelect { get; set; }
        public bool IsMyTurn { get; set; }
        public bool CanPick => (IsMyTurn && ActionType == "pick") || (IsInChampSelect && IsRandomChampionMode);
        public bool CanBan => IsMyTurn && ActionType == "ban";
        public bool CanLeave { get; set; }
        public bool CanStartLobby { get; set; }
        public bool IsCustomLobby { get; set; }
        public int PartySize { get; set; }
        public string LobbyHint { get; set; } = string.Empty;
        public bool CanAcceptMatch { get; set; }
        public bool MatchAccepted { get; set; }
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
        public bool IsGameClientRunning { get; set; }
        public bool IsGameLoaded { get; set; }
        public double GameTimeSeconds { get; set; } = -1;
        public string GameStatus { get; set; } = "Waiting";
        public string Message { get; set; } = string.Empty;
        public int SelectedChampionId { get; set; }
        public int PickIntentChampionId { get; set; }
        public int HoveredChampionId { get; set; }
        public string HoveredChampionSource { get; set; } = string.Empty;
        public int HoveredBanChampionId { get; set; }
        public bool BanFallbackArmed { get; set; }
        public int Spell1Id { get; set; }
        public int Spell2Id { get; set; }
        public List<int> BannedChampionIds { get; set; } = new();
        public List<int> PickedChampionIds { get; set; } = new();
        public List<int> AllyPickedChampionIds { get; set; } = new();
        public List<int> EnemyPickedChampionIds { get; set; } = new();
        public List<int> AvailableSpellIds { get; set; } = new();
        public List<int> AvailableChampionIds { get; set; } = new();
        public List<RemoteTeamSlotDto> AllySlots { get; set; } = new();
        public List<RemoteTeamSlotDto> EnemySlots { get; set; } = new();
        public List<RemoteChampionDto> Champions
        {
            get
            {
                if (CanBan || !IsInChampSelect)
                    return _champions;

                return _champions
                    .Where(champion => !champion.IsDisabled || champion.IsSelected || champion.IsIntent || champion.IsPicked || champion.IsBanned)
                    .ToList();
            }
            set => _champions = value ?? new List<RemoteChampionDto>();
        }
        public List<RemoteSummonerSpellDto> SummonerSpells { get; set; } = new();
        public List<RemoteRunePageDto> RunePages { get; set; } = new();
        public List<RemoteRecommendedRunePageDto> RecommendedRunePages { get; set; } = new();
        public RemoteRunePageDto? CurrentRunePage { get; set; }
    }

    public class RemoteTeamSlotDto
    {
        public int CellId { get; set; }
        public int ChampionId { get; set; }
        public int IntentChampionId { get; set; }
        public bool IsLocalPlayer { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string ProfileUrl { get; set; } = string.Empty;
        [JsonIgnore] public string PlayerPuuid { get; set; } = string.Empty;
        [JsonIgnore] public string PlayerSummonerId { get; set; } = string.Empty;
        [JsonIgnore] public bool IsPlayerNameVisible { get; set; }
        [JsonIgnore] public string RiotGameName { get; set; } = string.Empty;
        [JsonIgnore] public string RiotTagLine { get; set; } = string.Empty;
    }

    public class RemoteChampionDto
    {
        public string[] Positions { get; set; } = System.Array.Empty<string>();
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsBanned { get; set; }
        public bool IsPicked { get; set; }
        public bool IsSelected { get; set; }
        public bool IsIntent { get; set; }
        public bool IsAvailableInCurrentMode { get; set; } = true;
        public string TeamSide { get; set; } = string.Empty;
        public bool IsDisabled => IsBanned || IsPicked || !IsAvailableInCurrentMode || !RiotAutoLogin.Services.RemoteChampionAvailabilityService.CanUseChampion(Id);
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
        public string KeystoneName { get; set; } = string.Empty;
        public string KeystoneIconUrl { get; set; } = string.Empty;
        public string PrimaryStyleName { get; set; } = string.Empty;
        public string PrimaryStyleIconUrl { get; set; } = string.Empty;
        public string SecondaryStyleName { get; set; } = string.Empty;
        public string SecondaryStyleIconUrl { get; set; } = string.Empty;
        public List<string> KeyRuneNames { get; set; } = new();
        public List<string> KeyRuneIconUrls { get; set; } = new();
    }

    public class RemoteRecommendedRunePageDto
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public int PrimaryStyleId { get; set; }
        public int SubStyleId { get; set; }
        public List<int> SelectedPerkIds { get; set; } = new();
        public string KeystoneName { get; set; } = string.Empty;
        public string KeystoneIconUrl { get; set; } = string.Empty;
        public string PrimaryStyleName { get; set; } = string.Empty;
        public string PrimaryStyleIconUrl { get; set; } = string.Empty;
        public string SecondaryStyleName { get; set; } = string.Empty;
        public string SecondaryStyleIconUrl { get; set; } = string.Empty;
        public List<string> KeyRuneNames { get; set; } = new();
        public List<string> KeyRuneIconUrls { get; set; } = new();
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
        public int ConfirmedChampionId { get; set; }
        public string ConfirmedChampionSource { get; set; } = string.Empty;
        public bool AutoBanArmed { get; set; }
    }
}
