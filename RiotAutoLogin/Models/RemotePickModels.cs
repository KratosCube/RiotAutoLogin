using System.Collections.Generic;

namespace RiotAutoLogin.Models
{
    public class RemotePickState
    {
        public string Phase { get; set; } = "None";
        public bool IsInChampSelect { get; set; }
        public bool IsMyTurn { get; set; }
        public string ActionId { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<int> BannedChampionIds { get; set; } = new();
        public List<int> PickedChampionIds { get; set; } = new();
        public List<RemoteChampionDto> Champions { get; set; } = new();
    }

    public class RemoteChampionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsBanned { get; set; }
        public bool IsPicked { get; set; }
        public bool IsDisabled => IsBanned || IsPicked;
    }

    public class RemotePickRequest
    {
        public int ChampionId { get; set; }
    }

    public class RemotePickActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
