using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.PlayerModels;

public class PlayerDto
{
    public string Id { get; set; } = "";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public int? Version { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // ---- profile ----
    /// <summary>Used to recognise the person: if it matches a registered user the player is linked to them.</summary>
    [StringLength(255)] public string? Email { get; set; }
    [StringLength(20)] public string? PhoneNumber { get; set; }
    public string? Gender { get; set; }                  // male | female | other
    public DateOnly? DateOfBirth { get; set; }           // yyyy-MM-dd
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public int? JerseyNumber { get; set; }
    [StringLength(80)] public string? City { get; set; }
    public string? PhotoUrl { get; set; }

    // ---- output only (ignored on input) ----
    public string? LinkedUserId { get; set; }
    public bool IsSelf { get; set; }
    /// <summary>True when the player is linked to a user who has signed in.</summary>
    public bool IsRegistered { get; set; }
}

/// <summary>Cricket profile of the signed-in user (user columns + their own player).</summary>
public class ProfileDto
{
    public string UserId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PhotoUrl { get; set; }
    public string? DisplayName { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? City { get; set; }
    public bool IsDiscoverable { get; set; } = true;
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public int? JerseyNumber { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateProfileRequest
{
    [Required, StringLength(120)] public string DisplayName { get; set; } = "";
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [StringLength(80)] public string? City { get; set; }
    [StringLength(20)] public string? PhoneNumber { get; set; }
    public string? PhotoUrl { get; set; }
    public bool IsDiscoverable { get; set; } = true;
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public int? JerseyNumber { get; set; }
}

/// <summary>One hit of GET /players/search.</summary>
public class PlayerSearchItem
{
    /// <summary>"mine" = already in the caller's player list, "user" = a registered user who is not yet.</summary>
    public string Source { get; set; } = "mine";
    public string? PlayerId { get; set; }
    public string? UserId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Own players: full email. Other users: masked (a***@gmail.com) unless searched by the exact email.</summary>
    public string? Email { get; set; }
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public string? City { get; set; }
    public bool IsRegistered { get; set; }
    public bool IsSelf { get; set; }
}

public class AddPlayerFromUserRequest
{
    [Required, StringLength(64)] public string UserId { get; set; } = "";
}
