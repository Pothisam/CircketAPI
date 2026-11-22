namespace Cricket_Models.StatsModels;

public class BattingStatsDto
{
    public int Innings { get; set; }
    public int Runs { get; set; }
    public int BallsFaced { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public int HighestScore { get; set; }
    public int Dismissals { get; set; }
    public double? Average { get; set; }
    public double? StrikeRate { get; set; }
    public int Fifties { get; set; }
    public int Hundreds { get; set; }
}

public class BowlingStatsDto
{
    public int Innings { get; set; }
    public int LegalBalls { get; set; }
    /// <summary>Overs as "x.y" (e.g. 12.4).</summary>
    public string Overs { get; set; } = "0.0";
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public double? Economy { get; set; }
    public double? Average { get; set; }
    public string? BestFigures { get; set; }
}

public class PlayerStatsDto
{
    public string PlayerId { get; set; } = "";
    public int Matches { get; set; }
    public BattingStatsDto Batting { get; set; } = new();
    public BowlingStatsDto Bowling { get; set; } = new();
}

public class TeamStatsDto
{
    public string TeamId { get; set; } = "";
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int NoResult { get; set; }
}
