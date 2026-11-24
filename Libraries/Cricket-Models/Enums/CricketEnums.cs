namespace Cricket_Models.Enums;

/// <summary>Exact string values the Flutter app sends (spec section 2).</summary>
public static class CricketEnums
{
    public static readonly HashSet<string> PlayerRoles = new() { "batter", "bowler", "allRounder", "wicketKeeper" };
    public static readonly HashSet<string> BattingStyles = new() { "Right-hand bat", "Left-hand bat" };
    public static readonly HashSet<string> BowlingStyles = new()
    {
        "None", "Right-arm fast", "Right-arm medium", "Right-arm off spin", "Right-arm leg spin",
        "Left-arm fast", "Left-arm medium", "Left-arm orthodox", "Left-arm chinaman"
    };
    public static readonly HashSet<string> ExtraTypes = new() { "none", "wide", "noBall", "bye", "legBye" };
    public static readonly HashSet<string> WicketTypes = new() { "bowled", "caught", "lbw", "runOut", "stumped", "hitWicket" };
    public static readonly HashSet<string> ShotDirections = new()
    {
        "straight", "midOff", "cover", "point", "thirdMan", "fineLeg", "squareLeg", "midWicket", "midOn"
    };
    public static readonly HashSet<string> ShotLofts = new() { "ground", "lofted", "aerial" };
    public static readonly HashSet<string> MatchStatuses = new() { "live", "completed", "abandoned" };
    public static readonly HashSet<string> Platforms = new() { "android", "ios", "web" };
    public static readonly HashSet<string> Visibilities = new() { "private", "link", "public" };
}

public static class NotificationTypes
{
    public const string Login = "login";
    public const string MatchStart = "match_start";
    public const string Four = "four";
    public const string Six = "six";
    public const string Wicket = "wicket";
    public const string Milestone = "milestone";
    public const string InningsEnd = "innings_end";
    public const string MatchResult = "match_result";
    public const string TeamAdded = "team_added";
    public const string TeamManager = "team_manager";
    public const string TeamLeft = "team_left";
    public const string MatchInvite = "match_invite";
    public const string MatchInviteResponse = "match_invite_response";
    public const string Test = "test";
}
