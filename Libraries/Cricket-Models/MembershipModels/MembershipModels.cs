namespace Cricket_Models.MembershipModels;

/// <summary>A team created by someone else in which the caller is a member.</summary>
public class JoinedTeamDto
{
    public string OwnerId { get; set; } = "";
    public string? OwnerName { get; set; }
    public string TeamId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public int ColorIndex { get; set; }
    public int PlayerCount { get; set; }
}

/// <summary>Public details of a player in a squad. Private details (height, weight, DOB, phone, e-mail) are never included.</summary>
public class SquadMemberDto
{
    /// <summary>Id in the squad owner's player list (used to pick a playing XI).</summary>
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public string? PhotoUrl { get; set; }
    public bool IsRegistered { get; set; }
    /// <summary>This member is the caller.</summary>
    public bool IsMe { get; set; }
    public bool IsCaptain { get; set; }
    public bool IsViceCaptain { get; set; }
    /// <summary>The creator of the team.</summary>
    public bool IsOwner { get; set; }
}

public class JoinedTeamDetailDto : JoinedTeamDto
{
    public List<SquadMemberDto> Squad { get; set; } = new();
}
