using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.MatchRepository;

/// <summary>A match with all of its child rows, loaded without tracking.</summary>
public class MatchAggregate
{
    public Match Match { get; set; } = null!;
    public List<MatchPlayer> Players { get; set; } = new();
    public List<Innings> Innings { get; set; } = new();
    public List<Ball> Balls { get; set; } = new();
}

public record BatterTotals(int Runs, int BallsFaced);

/// <summary>Innings score as it stood after a given ball.</summary>
public record ScoreSnapshot(int Runs, int Wickets, int LegalBalls);

public interface IMatchRepo
{
    // Match rows
    Task<List<Match>> ListAsync(string uid, string? status, DateTime? updatedSince, bool includeDeleted, int offset, int take);
    Task<Match?> GetAsync(string uid, string id);                          // tracked (includes soft-deleted)
    Task<MatchAggregate?> GetAggregateAsync(string uid, string id);        // no-tracking
    Task<List<MatchAggregate>> GetAggregatesAsync(List<Match> matches);    // no-tracking, children only
    Task<List<Innings>> GetInningsForMatchesAsync(List<Match> matches);
    Task SaveAggregateAsync(MatchAggregate aggregate, bool isNew);
    Task SoftDeleteAsync(Match match, DateTime now);
    Task SaveChangesAsync();

    // Live scoring
    Task<Innings?> GetInningsAsync(string uid, string matchId, int inningsNo);   // tracked
    Task AddInningsAsync(Innings innings);
    Task<int> GetBallCountAsync(string uid, string matchId, int inningsNo);
    Task<Ball?> GetBallAsync(string uid, string matchId, int inningsNo, int seq);
    Task<List<Ball>> GetBallsAsync(string uid, string matchId, int inningsNo);
    Task AddBallAsync(Ball ball);
    Task<Ball?> DeleteLastBallAsync(string uid, string matchId, int inningsNo);
    /// <summary>Recomputes total_runs / wickets / legal_balls from the balls table and saves them.</summary>
    Task<Innings> RecomputeTotalsAsync(Innings innings);
    Task<HashSet<string>> GetMatchPlayerIdsAsync(string uid, string matchId);
    Task<Dictionary<string, string>> GetPlayerNamesAsync(string uid, string matchId);
    Task<BatterTotals> GetBatterTotalsAsync(string uid, string matchId, int inningsNo, string playerId, int uptoSeq);
    /// <summary>Score after ball <paramref name="uptoSeq"/> (inclusive), independent of balls recorded since.</summary>
    Task<ScoreSnapshot> GetScoreUpToAsync(string uid, string matchId, int inningsNo, int uptoSeq);

    // Sharing / following
    Task<Match?> GetByShareCodeAsync(string code);
    Task<bool> ShareCodeExistsAsync(string code);
    Task AddFollowerAsync(string ownerId, string matchId, string followerId);
    Task<bool> RemoveFollowerAsync(string ownerId, string matchId, string followerId);
    Task<List<string>> GetFollowerIdsAsync(string ownerId, string matchId);
    Task<List<Match>> ListFollowedAsync(string followerId);

    // Statistics
    Task<List<Ball>> GetPlayerBallsAsync(string uid, string playerId);
    Task<int> CountPlayerMatchesAsync(string uid, string playerId);
    Task<List<Match>> GetTeamMatchesAsync(string uid, string teamId);
}

public class MatchRepo : IMatchRepo
{
    private readonly CricketContext _ctx;

    public MatchRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    // ------------------------------------------------------------ match rows

    public Task<List<Match>> ListAsync(string uid, string? status, DateTime? updatedSince, bool includeDeleted, int offset, int take)
    {
        var q = _ctx.Matches.AsNoTracking().Where(x => x.UserId == uid);
        if (!includeDeleted) q = q.Where(x => x.DeletedAt == null);
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        if (updatedSince.HasValue) q = q.Where(x => x.UpdatedAt > updatedSince.Value);
        // Sync pulls (updatedSince) need a stable ascending order; the Matches tab wants newest first.
        var ordered = updatedSince.HasValue
            ? q.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id)
            : q.OrderByDescending(x => x.MatchDate).ThenBy(x => x.Id);
        return ordered.Skip(offset).Take(take).ToListAsync();
    }

    public Task<Match?> GetAsync(string uid, string id) =>
        _ctx.Matches.FirstOrDefaultAsync(x => x.UserId == uid && x.Id == id);

    public async Task<MatchAggregate?> GetAggregateAsync(string uid, string id)
    {
        var match = await _ctx.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid && x.Id == id);
        if (match == null) return null;
        return (await GetAggregatesAsync(new List<Match> { match }))[0];
    }

    public async Task<List<MatchAggregate>> GetAggregatesAsync(List<Match> matches)
    {
        if (matches.Count == 0) return new List<MatchAggregate>();
        var ids = matches.Select(m => m.Id).Distinct().ToList();
        var owners = matches.Select(m => m.UserId).Distinct().ToList();

        var players = await _ctx.MatchPlayers.AsNoTracking()
            .Where(x => owners.Contains(x.UserId) && ids.Contains(x.MatchId))
            .OrderBy(x => x.BattingOrder).ToListAsync();
        var innings = await _ctx.Innings.AsNoTracking()
            .Where(x => owners.Contains(x.UserId) && ids.Contains(x.MatchId))
            .OrderBy(x => x.InningsNo).ToListAsync();
        var balls = await _ctx.Balls.AsNoTracking()
            .Where(x => owners.Contains(x.UserId) && ids.Contains(x.MatchId))
            .OrderBy(x => x.InningsNo).ThenBy(x => x.Seq).ToListAsync();

        // Ids are only unique per user, so match children back on (user, match).
        return matches.Select(m => new MatchAggregate
        {
            Match = m,
            Players = players.Where(p => p.UserId == m.UserId && p.MatchId == m.Id).ToList(),
            Innings = innings.Where(i => i.UserId == m.UserId && i.MatchId == m.Id).ToList(),
            Balls = balls.Where(b => b.UserId == m.UserId && b.MatchId == m.Id).ToList()
        }).ToList();
    }

    public async Task<List<Innings>> GetInningsForMatchesAsync(List<Match> matches)
    {
        if (matches.Count == 0) return new List<Innings>();
        var ids = matches.Select(m => m.Id).Distinct().ToList();
        var owners = matches.Select(m => m.UserId).Distinct().ToList();
        return await _ctx.Innings.AsNoTracking()
            .Where(x => owners.Contains(x.UserId) && ids.Contains(x.MatchId))
            .OrderBy(x => x.InningsNo).ToListAsync();
    }

    /// <summary>
    /// Writes the match row and replaces its children (players, innings, balls) in one transaction.
    /// For an existing match the caller passes the tracked entity it modified.
    /// </summary>
    public async Task SaveAggregateAsync(MatchAggregate agg, bool isNew)
    {
        var uid = agg.Match.UserId;
        var id = agg.Match.Id;
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        if (isNew) _ctx.Matches.Add(agg.Match);
        await _ctx.SaveChangesAsync();

        await _ctx.Balls.Where(x => x.UserId == uid && x.MatchId == id).ExecuteDeleteAsync();
        await _ctx.Innings.Where(x => x.UserId == uid && x.MatchId == id).ExecuteDeleteAsync();
        await _ctx.MatchPlayers.Where(x => x.UserId == uid && x.MatchId == id).ExecuteDeleteAsync();

        _ctx.MatchPlayers.AddRange(agg.Players);
        _ctx.Innings.AddRange(agg.Innings);
        await _ctx.SaveChangesAsync();
        _ctx.Balls.AddRange(agg.Balls);
        await _ctx.SaveChangesAsync();

        await tx.CommitAsync();
        _ctx.ChangeTracker.Clear();
    }

    public Task SoftDeleteAsync(Match match, DateTime now)
    {
        match.DeletedAt = now;
        match.UpdatedAt = now;
        return _ctx.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => _ctx.SaveChangesAsync();

    // ----------------------------------------------------------- live scoring

    public Task<Innings?> GetInningsAsync(string uid, string matchId, int inningsNo) =>
        _ctx.Innings.FirstOrDefaultAsync(x => x.UserId == uid && x.MatchId == matchId && x.InningsNo == inningsNo);

    public async Task AddInningsAsync(Innings innings)
    {
        _ctx.Innings.Add(innings);
        await _ctx.SaveChangesAsync();
    }

    public Task<int> GetBallCountAsync(string uid, string matchId, int inningsNo) =>
        _ctx.Balls.CountAsync(x => x.UserId == uid && x.MatchId == matchId && x.InningsNo == inningsNo);

    public Task<Ball?> GetBallAsync(string uid, string matchId, int inningsNo, int seq) =>
        _ctx.Balls.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid && x.MatchId == matchId && x.InningsNo == inningsNo && x.Seq == seq);

    public Task<List<Ball>> GetBallsAsync(string uid, string matchId, int inningsNo) =>
        _ctx.Balls.AsNoTracking().Where(x => x.UserId == uid && x.MatchId == matchId && x.InningsNo == inningsNo)
            .OrderBy(x => x.Seq).ToListAsync();

    public async Task AddBallAsync(Ball ball)
    {
        _ctx.Balls.Add(ball);
        await _ctx.SaveChangesAsync();
    }

    public async Task<Ball?> DeleteLastBallAsync(string uid, string matchId, int inningsNo)
    {
        var last = await _ctx.Balls.Where(x => x.UserId == uid && x.MatchId == matchId && x.InningsNo == inningsNo)
            .OrderByDescending(x => x.Seq).FirstOrDefaultAsync();
        if (last == null) return null;
        _ctx.Balls.Remove(last);
        await _ctx.SaveChangesAsync();
        return last;
    }

    public async Task<Innings> RecomputeTotalsAsync(Innings innings)
    {
        var totals = await _ctx.Balls.AsNoTracking()
            .Where(x => x.UserId == innings.UserId && x.MatchId == innings.MatchId && x.InningsNo == innings.InningsNo)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Runs = g.Sum(b => b.Runs + b.ExtraRuns),
                Wickets = g.Count(b => b.Wicket != null),
                Legal = g.Count(b => b.Extra != "wide" && b.Extra != "noBall")
            }).FirstOrDefaultAsync();

        innings.TotalRuns = totals?.Runs ?? 0;
        innings.Wickets = totals?.Wickets ?? 0;
        innings.LegalBalls = totals?.Legal ?? 0;
        await _ctx.SaveChangesAsync();
        return innings;
    }

    public async Task<HashSet<string>> GetMatchPlayerIdsAsync(string uid, string matchId) =>
        (await _ctx.MatchPlayers.AsNoTracking().Where(x => x.UserId == uid && x.MatchId == matchId)
            .Select(x => x.PlayerId).ToListAsync()).ToHashSet();

    public Task<Dictionary<string, string>> GetPlayerNamesAsync(string uid, string matchId) =>
        _ctx.MatchPlayers.AsNoTracking().Where(x => x.UserId == uid && x.MatchId == matchId)
            .ToDictionaryAsync(x => x.PlayerId, x => x.PlayerName);

    public async Task<BatterTotals> GetBatterTotalsAsync(string uid, string matchId, int inningsNo, string playerId, int uptoSeq)
    {
        var t = await _ctx.Balls.AsNoTracking()
            .Where(x => x.UserId == uid && x.MatchId == matchId && x.InningsNo == inningsNo
                        && x.StrikerId == playerId && x.Seq <= uptoSeq)
            .GroupBy(_ => 1)
            .Select(g => new { Runs = g.Sum(b => b.Runs), Faced = g.Count(b => b.Extra != "wide") })
            .FirstOrDefaultAsync();
        return new BatterTotals(t?.Runs ?? 0, t?.Faced ?? 0);
    }

    public async Task<ScoreSnapshot> GetScoreUpToAsync(string uid, string matchId, int inningsNo, int uptoSeq)
    {
        var t = await _ctx.Balls.AsNoTracking()
            .Where(x => x.UserId == uid && x.MatchId == matchId && x.InningsNo == inningsNo && x.Seq <= uptoSeq)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Runs = g.Sum(b => b.Runs + b.ExtraRuns),
                Wickets = g.Count(b => b.Wicket != null),
                Legal = g.Count(b => b.Extra != "wide" && b.Extra != "noBall")
            }).FirstOrDefaultAsync();
        return new ScoreSnapshot(t?.Runs ?? 0, t?.Wickets ?? 0, t?.Legal ?? 0);
    }

    // ------------------------------------------------------ sharing / following

    public Task<Match?> GetByShareCodeAsync(string code) =>
        _ctx.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.ShareCode == code && x.DeletedAt == null && x.Visibility != "private");

    public Task<bool> ShareCodeExistsAsync(string code) => _ctx.Matches.AnyAsync(x => x.ShareCode == code);

    public async Task AddFollowerAsync(string ownerId, string matchId, string followerId)
    {
        var exists = await _ctx.MatchFollowers.AnyAsync(x => x.MatchOwnerId == ownerId && x.MatchId == matchId && x.FollowerId == followerId);
        if (exists) return;
        _ctx.MatchFollowers.Add(new MatchFollower
        {
            MatchOwnerId = ownerId, MatchId = matchId, FollowerId = followerId, CreatedAt = DateTime.UtcNow
        });
        await _ctx.SaveChangesAsync();
    }

    public async Task<bool> RemoveFollowerAsync(string ownerId, string matchId, string followerId) =>
        await _ctx.MatchFollowers.Where(x => x.MatchOwnerId == ownerId && x.MatchId == matchId && x.FollowerId == followerId)
            .ExecuteDeleteAsync() > 0;

    public Task<List<string>> GetFollowerIdsAsync(string ownerId, string matchId) =>
        _ctx.MatchFollowers.AsNoTracking().Where(x => x.MatchOwnerId == ownerId && x.MatchId == matchId)
            .Select(x => x.FollowerId).ToListAsync();

    public Task<List<Match>> ListFollowedAsync(string followerId) =>
        (from f in _ctx.MatchFollowers.AsNoTracking()
         join m in _ctx.Matches.AsNoTracking() on new { U = f.MatchOwnerId, I = f.MatchId } equals new { U = m.UserId, I = m.Id }
         where f.FollowerId == followerId && m.DeletedAt == null && m.Visibility != "private"
         orderby m.MatchDate descending
         select m).ToListAsync();

    // ------------------------------------------------------------- statistics

    public Task<List<Ball>> GetPlayerBallsAsync(string uid, string playerId) =>
        (from b in _ctx.Balls.AsNoTracking()
         join m in _ctx.Matches.AsNoTracking() on new { U = b.UserId, I = b.MatchId } equals new { U = m.UserId, I = m.Id }
         where b.UserId == uid && m.DeletedAt == null
               && (b.StrikerId == playerId || b.BowlerId == playerId || b.OutPlayerId == playerId)
         select b).ToListAsync();

    public Task<int> CountPlayerMatchesAsync(string uid, string playerId) =>
        (from p in _ctx.MatchPlayers.AsNoTracking()
         join m in _ctx.Matches.AsNoTracking() on new { U = p.UserId, I = p.MatchId } equals new { U = m.UserId, I = m.Id }
         where p.UserId == uid && p.PlayerId == playerId && m.DeletedAt == null
         select p.MatchId).Distinct().CountAsync();

    public Task<List<Match>> GetTeamMatchesAsync(string uid, string teamId) =>
        _ctx.Matches.AsNoTracking()
            .Where(x => x.UserId == uid && x.DeletedAt == null && (x.TeamAId == teamId || x.TeamBId == teamId))
            .ToListAsync();
}
