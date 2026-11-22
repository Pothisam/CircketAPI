using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.MembershipRepository;

public record JoinedTeamRow(Team Team, string? OwnerName, int PlayerCount);

/// <summary>
/// "Teams I belong to" and "matches I played in", even though someone else created them.
/// Membership is not stored separately: a player in somebody's list is linked to a user
/// (players.linked_user_id), and that player sits in team_players / match_players.
/// </summary>
public interface IMembershipRepo
{
    /// <summary>Teams of other users that contain a player linked to <paramref name="me"/>.</summary>
    Task<List<JoinedTeamRow>> ListJoinedTeamsAsync(string me);
    Task<JoinedTeamRow?> GetJoinedTeamAsync(string me, string ownerId, string teamId);
    /// <summary>Squad (owner's players) of a team, in squad order.</summary>
    Task<List<Player>> GetSquadAsync(string ownerId, string teamId);
    /// <summary>Other users' matches I played in, or that were played by a team I belong to (newest first).</summary>
    Task<List<Match>> ListParticipantMatchesAsync(string me, int take);
    Task<bool> IsParticipantAsync(string me, string ownerId, string matchId);
}

public class MembershipRepo : IMembershipRepo
{
    private readonly CricketContext _ctx;

    public MembershipRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<List<JoinedTeamRow>> ListJoinedTeamsAsync(string me)
    {
        var rows = await (from p in _ctx.Players.AsNoTracking()
                          where p.LinkedUserId == me && !p.IsSelf && p.DeletedAt == null && p.UserId != me
                          join tp in _ctx.TeamPlayers.AsNoTracking()
                              on new { p.UserId, PlayerId = p.Id } equals new { tp.UserId, tp.PlayerId }
                          join t in _ctx.Teams.AsNoTracking()
                              on new { tp.UserId, Id = tp.TeamId } equals new { t.UserId, t.Id }
                          where t.DeletedAt == null
                          join o in _ctx.Users.AsNoTracking() on t.UserId equals o.Id
                          select new { Team = t, Owner = o.DisplayName }).ToListAsync();

        var teams = rows.GroupBy(r => (r.Team.UserId, r.Team.Id)).Select(g => g.First()).ToList();
        if (teams.Count == 0) return new List<JoinedTeamRow>();

        var owners = teams.Select(t => t.Team.UserId).Distinct().ToList();
        var counts = (await _ctx.TeamPlayers.AsNoTracking()
                .Where(x => owners.Contains(x.UserId))
                .GroupBy(x => new { x.UserId, x.TeamId })
                .Select(g => new { g.Key.UserId, g.Key.TeamId, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => (x.UserId, x.TeamId), x => x.Count);

        return teams
            .Select(t => new JoinedTeamRow(t.Team, t.Owner, counts.GetValueOrDefault((t.Team.UserId, t.Team.Id))))
            .OrderBy(t => t.Team.Name)
            .ToList();
    }

    public async Task<JoinedTeamRow?> GetJoinedTeamAsync(string me, string ownerId, string teamId) =>
        (await ListJoinedTeamsAsync(me)).FirstOrDefault(t => t.Team.UserId == ownerId && t.Team.Id == teamId);

    public Task<List<Player>> GetSquadAsync(string ownerId, string teamId) =>
        (from tp in _ctx.TeamPlayers.AsNoTracking()
         where tp.UserId == ownerId && tp.TeamId == teamId
         join p in _ctx.Players.AsNoTracking()
             on new { tp.UserId, Id = tp.PlayerId } equals new { p.UserId, p.Id }
         where p.DeletedAt == null
         orderby tp.SortOrder
         select p).ToListAsync();

    public async Task<List<Match>> ListParticipantMatchesAsync(string me, int take)
    {
        // 1. matches in which I am in the playing XI
        var played = await (from p in _ctx.Players.AsNoTracking()
                            where p.LinkedUserId == me && !p.IsSelf && p.UserId != me
                            join mp in _ctx.MatchPlayers.AsNoTracking()
                                on new { p.UserId, PlayerId = p.Id } equals new { mp.UserId, mp.PlayerId }
                            join m in _ctx.Matches.AsNoTracking()
                                on new { mp.UserId, Id = mp.MatchId } equals new { m.UserId, m.Id }
                            where m.DeletedAt == null
                            select m).ToListAsync();

        // 2. matches played by a team I belong to (even if I was not picked)
        var joined = await ListJoinedTeamsAsync(me);
        var pairs = joined.Select(j => (j.Team.UserId, j.Team.Id)).ToHashSet();
        var owners = pairs.Select(x => x.UserId).Distinct().ToList();
        var byTeam = owners.Count == 0
            ? new List<Match>()
            : (await _ctx.Matches.AsNoTracking()
                    .Where(m => (owners.Contains(m.UserId) || (m.OpponentUserId != null && owners.Contains(m.OpponentUserId)))
                                && m.DeletedAt == null && m.UserId != me)
                    .ToListAsync())
                .Where(m => pairs.Contains((m.UserId, m.TeamAId)) || pairs.Contains((m.UserId, m.TeamBId))
                            // a match against a team of another user: team B lives in the opponent's account
                            || (m.OpponentUserId != null && pairs.Contains((m.OpponentUserId, m.TeamBId))))
                .ToList();

        // 3. cross-user matches in which I am the opponent's owner
        var asOpponent = await _ctx.Matches.AsNoTracking()
            .Where(m => m.OpponentUserId == me && m.DeletedAt == null).ToListAsync();

        return played.Concat(byTeam).Concat(asOpponent)
            .GroupBy(m => (m.UserId, m.Id)).Select(g => g.First())
            .OrderByDescending(m => m.MatchDate)
            .Take(take)
            .ToList();
    }

    public async Task<bool> IsParticipantAsync(string me, string ownerId, string matchId)
    {
        if (ownerId == me) return false;
        var match = await _ctx.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == ownerId && m.Id == matchId && m.DeletedAt == null);
        if (match == null) return false;
        if (match.OpponentUserId == me) return true;      // I own the opponent team

        var played = await (from p in _ctx.Players.AsNoTracking()
                            where p.UserId == ownerId && p.LinkedUserId == me && !p.IsSelf
                            join mp in _ctx.MatchPlayers.AsNoTracking()
                                on new { p.UserId, PlayerId = p.Id } equals new { mp.UserId, mp.PlayerId }
                            where mp.MatchId == matchId
                            select 1).AnyAsync();
        if (played) return true;

        var joined = await ListJoinedTeamsAsync(me);
        return joined.Any(j => (j.Team.UserId == ownerId && (j.Team.Id == match.TeamAId || j.Team.Id == match.TeamBId))
                               || (match.OpponentUserId != null && j.Team.UserId == match.OpponentUserId && j.Team.Id == match.TeamBId));
    }
}
