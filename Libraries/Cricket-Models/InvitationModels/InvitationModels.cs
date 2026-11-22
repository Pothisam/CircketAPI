using Cricket_Models.MatchModels;
using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.InvitationModels;

/// <summary>A team of another user, found by name (GET /teams/search).</summary>
public class TeamSearchItem
{
    public string OwnerId { get; set; } = "";
    public string? OwnerName { get; set; }
    public string TeamId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public int ColorIndex { get; set; }
    public int PlayerCount { get; set; }
}

public class CreateInvitationRequest
{
    /// <summary>One of my teams.</summary>
    [Required, StringLength(64)] public string FromTeamId { get; set; } = "";
    /// <summary>The opponent team and its owner (both from GET /teams/search).</summary>
    [Required, StringLength(64)] public string ToOwnerId { get; set; } = "";
    [Required, StringLength(64)] public string ToTeamId { get; set; } = "";
    [Range(1, 100)] public int Overs { get; set; } = 20;
    /// <summary>When the match is planned (optional).</summary>
    public DateTime? ScheduledAt { get; set; }
    [StringLength(200)] public string? Venue { get; set; }
    [StringLength(300)] public string? Message { get; set; }
}

public class InvitationDto
{
    public string Id { get; set; } = "";
    /// <summary>"outgoing" = I sent it (I am the scorer), "incoming" = I received it.</summary>
    public string Direction { get; set; } = "";
    /// <summary>pending | accepted | rejected | cancelled | started</summary>
    public string Status { get; set; } = "";
    public string FromUserId { get; set; } = "";
    public string? FromUserName { get; set; }
    public TeamRefDto FromTeam { get; set; } = new();
    public string ToUserId { get; set; } = "";
    public string? ToUserName { get; set; }
    public TeamRefDto ToTeam { get; set; } = new();
    public int Overs { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? Venue { get; set; }
    public string? Message { get; set; }
    /// <summary>Set once the scorer has started the match.</summary>
    public string? MatchId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
