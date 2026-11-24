using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.TeamModels;

/// <summary>Limits per user. Enforced by the API.</summary>
public static class TeamLimits
{
    /// <summary>Teams a user can own (duplicates count).</summary>
    public const int MaxTeams = 3;
    /// <summary>Of those, at most this many can be public; the rest are private (1 public + 2 private).</summary>
    public const int MaxPublic = 1;
    public const int MaxPrivate = MaxTeams - MaxPublic;
    /// <summary>Users a team can be handed to.</summary>
    public const int MaxManagers = 5;
}

/// <summary>A user the team was handed to (GET /teams/{id}/managers).</summary>
public class ManagerDto
{
    public string UserId { get; set; } = "";
    public string? Name { get; set; }
    public string? PhotoUrl { get; set; }
    /// <summary>Masked (a***@gmail.com).</summary>
    public string? Email { get; set; }
}

public class AddManagerRequest
{
    [Required, StringLength(64)] public string UserId { get; set; } = "";
}

/// <summary>A player of the team's squad (or of the owner's list), as a manager sees it.</summary>
public class ManagedMemberDto
{
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public string? PhotoUrl { get; set; }
    public bool IsRegistered { get; set; }
    /// <summary>The creator of the team: a manager cannot remove them.</summary>
    public bool IsOwner { get; set; }
}

/// <summary>A team handed over to me, with what I need to run it.</summary>
public class ManagedTeamDto
{
    public string OwnerId { get; set; } = "";
    public string? OwnerName { get; set; }
    public string TeamId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public int ColorIndex { get; set; }
    public bool IsPublic { get; set; }
    public string? CaptainPlayerId { get; set; }
    public string? ViceCaptainPlayerId { get; set; }
    public int PlayerCount { get; set; }
    /// <summary>The squad in batting order (only filled by the detail call).</summary>
    public List<ManagedMemberDto> Squad { get; set; } = new();
    /// <summary>Players of the owner's list that are not in the squad yet (only filled by the detail call).</summary>
    public List<ManagedMemberDto> Roster { get; set; } = new();
}

/// <summary>What a manager can change.</summary>
public class ManagedTeamUpdate
{
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [Required, StringLength(8)] public string ShortName { get; set; } = "";
    public int ColorIndex { get; set; }
    /// <summary>The whole squad, in batting order.</summary>
    public List<string> PlayerIds { get; set; } = new();
    public string? CaptainPlayerId { get; set; }
    public string? ViceCaptainPlayerId { get; set; }
}

/// <summary>Add somebody to the squad: a registered user (userId) or a new player by name.</summary>
public class AddManagedPlayerRequest
{
    public string? UserId { get; set; }
    [StringLength(120)] public string? Name { get; set; }
    public string? Role { get; set; }
}
