using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.MatchModels;
using Cricket_Repository.Entity;
using Cricket_Repository.MatchRepository;
using System.Text.Json;

namespace Cricket_Services.MatchServices;

/// <summary>Entity &lt;-&gt; DTO mapping and validation for matches, innings and balls.</summary>
internal static class MatchMapper
{
    // ------------------------------------------------------------ entity -> DTO

    public static MatchDto ToDto(MatchAggregate a)
    {
        var m = a.Match;
        var inningsDtos = a.Innings.OrderBy(i => i.InningsNo).Select(i => new InningsDto
        {
            InningsNo = i.InningsNo,
            BattingTeamId = i.BattingTeamId,
            BowlingTeamId = i.BowlingTeamId,
            Striker = i.StrikerId,
            NonStriker = i.NonStrikerId,
            Bowler = i.BowlerId,
            Completed = i.Completed,
            Runs = i.TotalRuns,
            Wickets = i.Wickets,
            LegalBalls = i.LegalBalls,
            Balls = a.Balls.Where(b => b.InningsNo == i.InningsNo).OrderBy(b => b.Seq).Select(b => ToDto(b, includeSeq: false)).ToList()
        }).ToList();

        return new MatchDto
        {
            Id = m.Id,
            TeamAId = m.TeamAId,
            TeamBId = m.TeamBId,
            TeamAName = m.TeamAName,
            TeamBName = m.TeamBName,
            TeamAShort = m.TeamAShort,
            TeamBShort = m.TeamBShort,
            Overs = m.Overs,
            TossWinnerId = m.TossWinnerId,
            ElectedToBat = m.ElectedToBat,
            TeamAPlayers = a.Players.Where(p => p.TeamId == m.TeamAId).OrderBy(p => p.BattingOrder).Select(p => p.PlayerId).ToList(),
            TeamBPlayers = a.Players.Where(p => p.TeamId == m.TeamBId).OrderBy(p => p.BattingOrder).Select(p => p.PlayerId).ToList(),
            PlayerNames = a.Players.ToDictionary(p => p.PlayerId, p => p.PlayerName),
            Date = m.MatchDate,
            Venue = m.Venue,
            Status = m.Status,
            WinnerId = m.WinnerId,
            ResultText = m.ResultText,
            Innings = inningsDtos,
            Visibility = m.Visibility,
            InvitationId = m.InvitationId,
            OpponentUserId = m.OpponentUserId,
            UpdatedAt = m.UpdatedAt,
            DeletedAt = m.DeletedAt
        };
    }

    public static BallDto ToDto(Ball b, bool includeSeq) => new()
    {
        Seq = includeSeq ? b.Seq : null,
        Striker = b.StrikerId,
        NonStriker = b.NonStrikerId,
        Bowler = b.BowlerId,
        Runs = b.Runs,
        Extra = b.Extra,
        ExtraRuns = b.ExtraRuns,
        Wicket = b.Wicket,
        Out = b.OutPlayerId,
        Fielder = b.FielderId,
        ShotDirection = b.ShotDirection,
        ShotPath = string.IsNullOrEmpty(b.ShotPath) ? null : JsonSerializer.Deserialize<List<List<double>>>(b.ShotPath),
        Loft = b.Loft
    };

    public static InningsDto ToDto(Innings i) => new()
    {
        InningsNo = i.InningsNo,
        BattingTeamId = i.BattingTeamId,
        BowlingTeamId = i.BowlingTeamId,
        Striker = i.StrikerId,
        NonStriker = i.NonStrikerId,
        Bowler = i.BowlerId,
        Completed = i.Completed,
        Runs = i.TotalRuns,
        Wickets = i.Wickets,
        LegalBalls = i.LegalBalls
    };

    public static InningsTotalsDto ToTotals(Innings i) => new()
    {
        InningsNo = i.InningsNo,
        Runs = i.TotalRuns,
        Wickets = i.Wickets,
        LegalBalls = i.LegalBalls,
        Completed = i.Completed
    };

    public static MatchSummaryDto ToSummary(Match m, IEnumerable<Innings> innings) => new()
    {
        Id = m.Id,
        OwnerId = m.UserId,
        Status = m.Status,
        Date = m.MatchDate,
        Venue = m.Venue,
        Overs = m.Overs,
        TeamA = new TeamRefDto { Id = m.TeamAId, Name = m.TeamAName, Short = m.TeamAShort },
        TeamB = new TeamRefDto { Id = m.TeamBId, Name = m.TeamBName, Short = m.TeamBShort },
        TossText = $"{(m.TossWinnerId == m.TeamAId ? m.TeamAName : m.TeamBName)} won the toss and chose to {(m.ElectedToBat ? "bat" : "bowl")}",
        Innings = innings.OrderBy(i => i.InningsNo).Select(i => new InningsSummaryDto
        {
            InningsNo = i.InningsNo,
            BattingTeamId = i.BattingTeamId,
            Runs = i.TotalRuns,
            Wickets = i.Wickets,
            LegalBalls = i.LegalBalls,
            Completed = i.Completed
        }).ToList(),
        WinnerId = m.WinnerId,
        ResultText = m.ResultText,
        UpdatedAt = m.UpdatedAt,
        DeletedAt = m.DeletedAt
    };

    // ------------------------------------------------------------ DTO -> entity

    public static Ball ToEntity(string uid, string matchId, int inningsNo, int seq, BallDto b, DateTime now) => new()
    {
        UserId = uid,
        MatchId = matchId,
        InningsNo = inningsNo,
        Seq = seq,
        StrikerId = b.Striker,
        NonStrikerId = b.NonStriker,
        BowlerId = b.Bowler,
        Runs = b.Runs,
        Extra = b.Extra,
        ExtraRuns = b.ExtraRuns,
        Wicket = b.Wicket,
        OutPlayerId = b.Out,
        FielderId = b.Fielder,
        ShotDirection = b.ShotDirection,
        ShotPath = b.ShotPath == null ? null : JsonSerializer.Serialize(b.ShotPath),
        Loft = b.Loft,
        CreatedAt = now
    };

    public static bool SameBall(Ball a, Ball b) =>
        a.StrikerId == b.StrikerId && a.NonStrikerId == b.NonStrikerId && a.BowlerId == b.BowlerId
        && a.Runs == b.Runs && a.Extra == b.Extra && a.ExtraRuns == b.ExtraRuns
        && a.Wicket == b.Wicket && a.OutPlayerId == b.OutPlayerId && a.FielderId == b.FielderId
        && a.ShotDirection == b.ShotDirection && a.Loft == b.Loft && a.ShotPath == b.ShotPath;

    public static void ApplyTotals(Innings innings, IEnumerable<Ball> balls)
    {
        var list = balls.ToList();
        innings.TotalRuns = list.Sum(b => b.Runs + b.ExtraRuns);
        innings.Wickets = list.Count(b => b.Wicket != null);
        innings.LegalBalls = list.Count(b => b.Extra != "wide" && b.Extra != "noBall");
    }

    public static DateTime ToUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        _ => d.ToUniversalTime()
    };

    public static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // -------------------------------------------------------------- validation

    /// <param name="memberIds">Players in the match squads; a ball's "out" player must be one of them.</param>
    public static void ValidateBall(BallDto b, string path, HashSet<string> memberIds, ValidationBuilder v)
    {
        v.Check(!string.IsNullOrWhiteSpace(b.Striker), $"{path}.striker", "required")
         .Check(!string.IsNullOrWhiteSpace(b.NonStriker), $"{path}.nonStriker", "required")
         .Check(!string.IsNullOrWhiteSpace(b.Bowler), $"{path}.bowler", "required")
         .Check(b.Runs is >= 0 and <= 7, $"{path}.runs", "must be 0 to 7")
         .Check(b.ExtraRuns is >= 0 and <= 10, $"{path}.extraRuns", "must be 0 to 10")
         .OneOf(b.Extra, CricketEnums.ExtraTypes, $"{path}.extra")
         .OneOf(b.Wicket, CricketEnums.WicketTypes, $"{path}.wicket", optional: true)
         .OneOf(b.ShotDirection, CricketEnums.ShotDirections, $"{path}.shotDirection", optional: true)
         .OneOf(b.Loft, CricketEnums.ShotLofts, $"{path}.loft", optional: true);

        if (b.Out != null)
        {
            v.Check(b.Wicket != null, $"{path}.out", "requires a wicket type");
            v.Check(memberIds.Contains(b.Out), $"{path}.out", "player is not part of this match");
        }
        if (b.ShotPath != null)
        {
            v.Check(b.ShotPath.Count <= 50, $"{path}.shotPath", "at most 50 points");
            v.Check(b.ShotPath.All(p => p.Count == 2), $"{path}.shotPath", "each point must be [x, y]");
        }
    }

    public static void ValidateMatch(MatchDto d, ValidationBuilder v)
    {
        var teams = new HashSet<string> { d.TeamAId, d.TeamBId };
        v.Check(d.TeamAId != d.TeamBId, "teamBId", "must differ from teamAId")
         .Check(d.Overs is >= 1 and <= 100, "overs", "must be 1 to 100")
         .Check(teams.Contains(d.TossWinnerId), "tossWinnerId", "must be teamAId or teamBId")
         .Check(d.WinnerId == null || teams.Contains(d.WinnerId), "winnerId", "must be teamAId, teamBId or null")
         .Check(d.Date != default, "date", "required")
         .OneOf(d.Status, CricketEnums.MatchStatuses, "status")
         .Check(d.Innings.Count <= 4, "innings", "at most 4 innings");

        var allPlayers = d.TeamAPlayers.Concat(d.TeamBPlayers).ToList();
        v.Check(allPlayers.Distinct().Count() == allPlayers.Count, "teamAPlayers",
            "a player cannot appear twice in the match squads");

        var members = allPlayers.ToHashSet();
        for (var i = 0; i < d.Innings.Count; i++)
        {
            var inn = d.Innings[i];
            v.Check(teams.Contains(inn.BattingTeamId), $"innings[{i}].battingTeamId", "must be teamAId or teamBId")
             .Check(teams.Contains(inn.BowlingTeamId) && inn.BowlingTeamId != inn.BattingTeamId,
                    $"innings[{i}].bowlingTeamId", "must be the other team");
            var balls = inn.Balls ?? new List<BallDto>();
            for (var j = 0; j < balls.Count; j++)
                ValidateBall(balls[j], $"innings[{i}].balls[{j}]", members, v);
        }
    }
}
