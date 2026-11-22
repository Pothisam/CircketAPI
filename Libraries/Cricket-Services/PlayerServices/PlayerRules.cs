using Cricket_Models.CommonModels;

namespace Cricket_Services.PlayerServices;

/// <summary>Validation / normalisation shared by the player and profile services.</summary>
public static class PlayerRules
{
    public static readonly HashSet<string> Genders = new() { "male", "female", "other" };

    /// <summary>Id of the player that represents a user in their own roster (fits the 64 char id column).</summary>
    public static string SelfPlayerId(string uid)
    {
        var id = "u_" + uid;
        return id.Length <= 64 ? id : id[..64];
    }

    public static string? NormalizeEmail(string? email)
    {
        var e = email?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(e) ? null : e;
    }

    public static string? Clean(string? v, int max)
    {
        var t = v?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length <= max ? t : t[..max];
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        return email[0] + "***" + email[at..];
    }

    public static ValidationBuilder CheckProfile(this ValidationBuilder v, string? gender, DateOnly? dob,
        decimal? heightCm, decimal? weightKg, int? jersey)
    {
        v.OneOf(gender, Genders, "gender", optional: true);
        v.Check(dob == null || (dob >= new DateOnly(1900, 1, 1) && dob <= DateOnly.FromDateTime(DateTime.UtcNow)),
            "dateOfBirth", "must be between 1900-01-01 and today");
        v.Check(heightCm == null || (heightCm >= 50 && heightCm <= 260), "heightCm", "must be 50 to 260");
        v.Check(weightKg == null || (weightKg >= 10 && weightKg <= 300), "weightKg", "must be 10 to 300");
        v.Check(jersey == null || (jersey >= 0 && jersey <= 999), "jerseyNumber", "must be 0 to 999");
        return v;
    }

    public static bool LooksLikeEmail(string e)
    {
        var at = e.IndexOf('@');
        return e.Length <= 255 && at > 0 && at < e.Length - 1 && e.IndexOf('@', at + 1) < 0 && !e.Contains(' ');
    }
}
