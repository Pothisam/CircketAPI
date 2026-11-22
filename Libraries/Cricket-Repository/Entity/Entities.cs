namespace Cricket_Repository.Entity;

// Plain table-shaped entities. Relationships are enforced by the DB (see Database/CricketDB_Deploy.sql),
// so there are no navigation properties. Column names are derived from property names (snake_case).

public class User
{
    public string Id { get; set; } = null!;                 // Firebase UID
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
    public string AuthProvider { get; set; } = "unknown";
    public bool EmailVerified { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ---- profile (upgrade 001) ----
    public string? Gender { get; set; }                     // male | female | other
    public DateOnly? DateOfBirth { get; set; }
    public string? City { get; set; }
    /// <summary>Other users may find this user in player search.</summary>
    public bool IsDiscoverable { get; set; } = true;
}

public class UserDevice
{
    public long Id { get; set; }
    public string UserId { get; set; } = null!;
    public string FcmToken { get; set; } = null!;
    public string? DeviceId { get; set; }
    public string Platform { get; set; } = null!;
    public string? DeviceModel { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
    public string? Locale { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Team
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string ShortName { get; set; } = null!;
    public int ColorIndex { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Player
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // ---- profile (upgrade 001) ----
    public string? Email { get; set; }                      // lower-case; used to match a registered user
    public string? PhoneNumber { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public int? JerseyNumber { get; set; }
    public string? City { get; set; }
    public string? PhotoUrl { get; set; }
    /// <summary>The signed-in user this player is (null = not registered).</summary>
    public string? LinkedUserId { get; set; }
    /// <summary>True for the player that represents the roster owner.</summary>
    public bool IsSelf { get; set; }
}

public class TeamPlayer
{
    public string UserId { get; set; } = null!;
    public string TeamId { get; set; } = null!;
    public string PlayerId { get; set; } = null!;
    public int SortOrder { get; set; }
}

public class Match
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string TeamAId { get; set; } = null!;
    public string TeamBId { get; set; } = null!;
    public string TeamAName { get; set; } = null!;
    public string TeamBName { get; set; } = null!;
    public string TeamAShort { get; set; } = null!;
    public string TeamBShort { get; set; } = null!;
    public int Overs { get; set; }
    public string TossWinnerId { get; set; } = null!;
    public bool ElectedToBat { get; set; }
    public DateTime MatchDate { get; set; }
    public string Venue { get; set; } = "";
    public string Status { get; set; } = "live";
    public string? WinnerId { get; set; }
    public string ResultText { get; set; } = "";
    public string Visibility { get; set; } = "private";
    public string? ShareCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class MatchPlayer
{
    public string UserId { get; set; } = null!;
    public string MatchId { get; set; } = null!;
    public string PlayerId { get; set; } = null!;
    public string TeamId { get; set; } = null!;
    public string PlayerName { get; set; } = null!;
    public int? BattingOrder { get; set; }
}

public class Innings
{
    public string UserId { get; set; } = null!;
    public string MatchId { get; set; } = null!;
    public int InningsNo { get; set; }
    public string BattingTeamId { get; set; } = null!;
    public string BowlingTeamId { get; set; } = null!;
    public string? StrikerId { get; set; }
    public string? NonStrikerId { get; set; }
    public string? BowlerId { get; set; }
    public bool Completed { get; set; }
    public int TotalRuns { get; set; }
    public int Wickets { get; set; }
    public int LegalBalls { get; set; }
}

public class Ball
{
    public string UserId { get; set; } = null!;
    public string MatchId { get; set; } = null!;
    public int InningsNo { get; set; }
    public int Seq { get; set; }
    public string StrikerId { get; set; } = null!;
    public string NonStrikerId { get; set; } = null!;
    public string BowlerId { get; set; } = null!;
    public int Runs { get; set; }
    public string Extra { get; set; } = "none";
    public int ExtraRuns { get; set; }
    public string? Wicket { get; set; }
    public string? OutPlayerId { get; set; }
    public string? FielderId { get; set; }
    public string? ShotDirection { get; set; }
    public string? ShotPath { get; set; }                   // JSON
    public string? Loft { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Notification
{
    public long Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string? Data { get; set; }                       // JSON object of strings
    public long? DeviceId { get; set; }
    public string? FcmMessageId { get; set; }
    public string Status { get; set; } = "queued";
    public string? Error { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}

public class NotificationPreference
{
    public string UserId { get; set; } = null!;
    public bool LoginAlerts { get; set; } = true;
    public bool MatchStart { get; set; } = true;
    public bool Boundaries { get; set; } = true;
    public bool Wickets { get; set; } = true;
    public bool Milestones { get; set; } = true;
    public bool InningsEnd { get; set; } = true;
    public bool MatchResult { get; set; } = true;
    public TimeSpan? QuietHoursStart { get; set; }
    public TimeSpan? QuietHoursEnd { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MatchFollower
{
    public string MatchOwnerId { get; set; } = null!;
    public string MatchId { get; set; } = null!;
    public string FollowerId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class Feedback
{
    public long Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? AppVersion { get; set; }
    public DateTime CreatedAt { get; set; }
}
