using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Cricket_Models.MatchModels;

/// <summary>One delivery. Mirrors the app's Ball JSON exactly ("out", "striker", ...).</summary>
public class BallDto
{
    /// <summary>0-based position in the innings. Optional on input of the ball endpoint (append when omitted).
    /// Omitted from the full-match JSON, where array order is the sequence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seq { get; set; }
    public string Striker { get; set; } = "";
    public string NonStriker { get; set; } = "";
    public string Bowler { get; set; } = "";
    public int Runs { get; set; }
    public string Extra { get; set; } = "none";
    public int ExtraRuns { get; set; }
    public string? Wicket { get; set; }
    public string? Out { get; set; }
    public string? Fielder { get; set; }
    public string? ShotDirection { get; set; }
    public List<List<double>>? ShotPath { get; set; }
    public string? Loft { get; set; }
}

public class InningsDto
{
    public int? InningsNo { get; set; }
    public string BattingTeamId { get; set; } = "";
    public string BowlingTeamId { get; set; } = "";
    public string? Striker { get; set; }
    public string? NonStriker { get; set; }
    public string? Bowler { get; set; }
    public bool Completed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BallDto>? Balls { get; set; }
    // Derived: returned by the server, ignored on input.
    public int? Runs { get; set; }
    public int? Wickets { get; set; }
    public int? LegalBalls { get; set; }
}

/// <summary>The app's CricketMatch.toJson() shape.</summary>
public class MatchDto
{
    public string Id { get; set; } = "";
    [Required] public string TeamAId { get; set; } = "";
    [Required] public string TeamBId { get; set; } = "";
    [Required, StringLength(80)] public string TeamAName { get; set; } = "";
    [Required, StringLength(80)] public string TeamBName { get; set; } = "";
    [Required, StringLength(8)] public string TeamAShort { get; set; } = "";
    [Required, StringLength(8)] public string TeamBShort { get; set; } = "";
    public int Overs { get; set; }
    [Required] public string TossWinnerId { get; set; } = "";
    public bool ElectedToBat { get; set; }
    public List<string> TeamAPlayers { get; set; } = new();
    public List<string> TeamBPlayers { get; set; } = new();
    public Dictionary<string, string> PlayerNames { get; set; } = new();
    public DateTime Date { get; set; }
    [StringLength(200)] public string? Venue { get; set; } = "";
    public string Status { get; set; } = "live";
    public string? WinnerId { get; set; }
    [StringLength(255)] public string? ResultText { get; set; } = "";
    public List<InningsDto> Innings { get; set; } = new();
    /// <summary>Set when the match is started from an accepted invitation (team B belongs to another user).</summary>
    public string? InvitationId { get; set; }
    // Output only
    public string? OpponentUserId { get; set; }
    public string? Visibility { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class TeamRefDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Short { get; set; } = "";
}

public class InningsSummaryDto
{
    public int InningsNo { get; set; }
    public string BattingTeamId { get; set; } = "";
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int LegalBalls { get; set; }
    public bool Completed { get; set; }
}

public class MatchSummaryDto
{
    public string Id { get; set; } = "";
    /// <summary>Owner of the match (needed by followers to unfollow).</summary>
    public string OwnerId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime Date { get; set; }
    public string? Venue { get; set; }
    public int Overs { get; set; }
    public TeamRefDto TeamA { get; set; } = new();
    public TeamRefDto TeamB { get; set; } = new();
    public string TossText { get; set; } = "";
    public List<InningsSummaryDto> Innings { get; set; } = new();
    public string? WinnerId { get; set; }
    public string? ResultText { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

// ---- Live-scoring requests / responses ----

public class StartInningsRequest
{
    [Range(1, 4)] public int InningsNo { get; set; }
    [Required] public string BattingTeamId { get; set; } = "";
    [Required] public string BowlingTeamId { get; set; } = "";
}

public class InningsTotalsDto
{
    public int InningsNo { get; set; }
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int LegalBalls { get; set; }
    public bool Completed { get; set; }
}

public class MatchStateDto
{
    public string Status { get; set; } = "";
    public string? ResultText { get; set; }
}

public class AddBallResponse
{
    public BallDto Ball { get; set; } = new();
    public InningsTotalsDto Innings { get; set; } = new();
    public MatchStateDto Match { get; set; } = new();
}

public class UndoBallResponse
{
    public InningsTotalsDto Innings { get; set; } = new();
}

public class CompleteMatchRequest
{
    [Required] public string Status { get; set; } = "completed";
    public string? WinnerId { get; set; }
    [StringLength(255)] public string? ResultText { get; set; }
}

// ---- Sharing / following ----

public class ShareRequest
{
    /// <summary>"link" (default) or "public".</summary>
    public string? Visibility { get; set; }
}

public class ShareResponse
{
    public string ShareCode { get; set; } = "";
    public string Url { get; set; } = "";
}

public class FollowRequest
{
    [Required, StringLength(16)] public string ShareCode { get; set; } = "";
}
