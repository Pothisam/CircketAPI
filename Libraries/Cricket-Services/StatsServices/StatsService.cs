using Cricket_Models.CommonModels;
using Cricket_Models.StatsModels;
using Cricket_Repository.Entity;
using Cricket_Repository.MatchRepository;
using Cricket_Repository.PlayerRepository;
using Cricket_Repository.TeamRepository;

namespace Cricket_Services.StatsServices;

public interface IStatsService
{
    Task<PlayerStatsDto> GetPlayerStatsAsync(string uid, string playerId);
    Task<TeamStatsDto> GetTeamStatsAsync(string uid, string teamId);
}

/// <summary>Attribution rules follow spec 3.9: bowler is charged runs + (wide/no-ball penalty); run-outs are not bowler wickets.</summary>
public class StatsService : IStatsService
{
    private readonly IMatchRepo _matches;
    private readonly IPlayerRepo _players;
    private readonly ITeamRepo _teams;

    public StatsService(IMatchRepo matches, IPlayerRepo players, ITeamRepo teams)
    {
        _matches = matches;
        _players = players;
        _teams = teams;
    }

    public async Task<PlayerStatsDto> GetPlayerStatsAsync(string uid, string playerId)
    {
        var player = await _players.GetAsync(uid, playerId);
        if (player == null || player.DeletedAt != null) throw ApiException.NotFound("Player");

        var balls = await _matches.GetPlayerBallsAsync(uid, playerId);
        return new PlayerStatsDto
        {
            PlayerId = playerId,
            Matches = await _matches.CountPlayerMatchesAsync(uid, playerId),
            Batting = Batting(balls, playerId),
            Bowling = Bowling(balls, playerId)
        };
    }

    public async Task<TeamStatsDto> GetTeamStatsAsync(string uid, string teamId)
    {
        var team = await _teams.GetAsync(uid, teamId);
        if (team == null || team.DeletedAt != null) throw ApiException.NotFound("Team");

        var stats = new TeamStatsDto { TeamId = teamId };
        foreach (var m in await _matches.GetTeamMatchesAsync(uid, teamId))
        {
            if (m.Status == "live") continue;
            stats.Played++;
            if (m.Status == "abandoned") stats.NoResult++;
            else if (m.WinnerId == null) stats.Tied++;
            else if (m.WinnerId == teamId) stats.Won++;
            else stats.Lost++;
        }
        return stats;
    }

    private static BattingStatsDto Batting(List<Ball> allBalls, string playerId)
    {
        var faced = allBalls.Where(b => b.StrikerId == playerId).ToList();
        var perInnings = faced.GroupBy(b => (b.MatchId, b.InningsNo)).Select(g => g.Sum(b => b.Runs)).ToList();
        var runs = faced.Sum(b => b.Runs);
        var dismissals = allBalls.Count(b => b.Wicket != null && (b.OutPlayerId ?? b.StrikerId) == playerId);
        var ballsFaced = faced.Count(b => b.Extra != "wide");

        return new BattingStatsDto
        {
            Innings = perInnings.Count,
            Runs = runs,
            BallsFaced = ballsFaced,
            Fours = faced.Count(b => b.Runs == 4),
            Sixes = faced.Count(b => b.Runs == 6),
            HighestScore = perInnings.Count == 0 ? 0 : perInnings.Max(),
            Dismissals = dismissals,
            Average = dismissals == 0 ? null : Math.Round((double)runs / dismissals, 2),
            StrikeRate = ballsFaced == 0 ? null : Math.Round(runs * 100.0 / ballsFaced, 2),
            Hundreds = perInnings.Count(r => r >= 100),
            Fifties = perInnings.Count(r => r >= 50 && r < 100)
        };
    }

    private static BowlingStatsDto Bowling(List<Ball> allBalls, string playerId)
    {
        var bowled = allBalls.Where(b => b.BowlerId == playerId).ToList();
        static bool Legal(Ball b) => b.Extra != "wide" && b.Extra != "noBall";
        static int Charged(Ball b) => b.Runs + (Legal(b) ? 0 : b.ExtraRuns);
        static bool BowlerWicket(Ball b) => b.Wicket != null && b.Wicket != "runOut";

        var legal = bowled.Count(Legal);
        var runs = bowled.Sum(Charged);
        var wickets = bowled.Count(BowlerWicket);

        var figures = bowled.GroupBy(b => (b.MatchId, b.InningsNo))
            .Select(g => (Wickets: g.Count(BowlerWicket), Runs: g.Sum(Charged)))
            .OrderByDescending(f => f.Wickets).ThenBy(f => f.Runs).ToList();

        return new BowlingStatsDto
        {
            Innings = figures.Count,
            LegalBalls = legal,
            Overs = $"{legal / 6}.{legal % 6}",
            RunsConceded = runs,
            Wickets = wickets,
            Economy = legal == 0 ? null : Math.Round(runs * 6.0 / legal, 2),
            Average = wickets == 0 ? null : Math.Round((double)runs / wickets, 2),
            BestFigures = figures.Count == 0 ? null : $"{figures[0].Wickets}/{figures[0].Runs}"
        };
    }
}
