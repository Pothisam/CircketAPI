using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.InvitationRepository;

public record TeamSearchRow(Team Team, string? OwnerName, int PlayerCount);

public interface IInvitationRepo
{
    /// <summary>Teams of other (active, discoverable) users whose name contains the term.</summary>
    Task<List<TeamSearchRow>> SearchTeamsAsync(string me, string term, int take);
    Task<bool> OwnerIsActiveAsync(string userId);
    Task<Dictionary<string, string?>> GetUserNamesAsync(IEnumerable<string> userIds);

    Task<MatchInvitation?> GetAsync(string id);                 // tracked
    /// <summary>box: incoming | outgoing | all. status: optional filter.</summary>
    Task<List<MatchInvitation>> ListAsync(string me, string box, string? status, int take);
    Task<bool> HasPendingAsync(string fromUser, string fromTeam, string toUser, string toTeam);
    Task AddAsync(MatchInvitation invitation);
    Task SaveChangesAsync();
}

public class InvitationRepo : IInvitationRepo
{
    private readonly CricketContext _ctx;

    public InvitationRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<List<TeamSearchRow>> SearchTeamsAsync(string me, string term, int take)
    {
        var rows = await (from t in _ctx.Teams.AsNoTracking()
                          where t.UserId != me && t.DeletedAt == null && t.Name.Contains(term)
                          join u in _ctx.Users.AsNoTracking() on t.UserId equals u.Id
                          where u.IsActive && u.IsDiscoverable
                          orderby t.Name
                          select new { Team = t, Owner = u.DisplayName }).Take(take).ToListAsync();
        if (rows.Count == 0) return new List<TeamSearchRow>();

        var owners = rows.Select(r => r.Team.UserId).Distinct().ToList();
        var counts = (await _ctx.TeamPlayers.AsNoTracking()
                .Where(x => owners.Contains(x.UserId))
                .GroupBy(x => new { x.UserId, x.TeamId })
                .Select(g => new { g.Key.UserId, g.Key.TeamId, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => (x.UserId, x.TeamId), x => x.Count);

        return rows.Select(r => new TeamSearchRow(r.Team, r.Owner, counts.GetValueOrDefault((r.Team.UserId, r.Team.Id)))).ToList();
    }

    public Task<bool> OwnerIsActiveAsync(string userId) =>
        _ctx.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.IsActive);

    public async Task<Dictionary<string, string?>> GetUserNamesAsync(IEnumerable<string> userIds)
    {
        var ids = userIds.Distinct().ToList();
        return (await _ctx.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.DisplayName }).ToListAsync())
            .ToDictionary(x => x.Id, x => x.DisplayName);
    }

    public Task<MatchInvitation?> GetAsync(string id) => _ctx.MatchInvitations.FirstOrDefaultAsync(x => x.Id == id);

    public Task<List<MatchInvitation>> ListAsync(string me, string box, string? status, int take)
    {
        var q = _ctx.MatchInvitations.AsNoTracking().AsQueryable();
        q = box switch
        {
            "incoming" => q.Where(x => x.ToUserId == me),
            "outgoing" => q.Where(x => x.FromUserId == me),
            _ => q.Where(x => x.ToUserId == me || x.FromUserId == me)
        };
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        return q.OrderByDescending(x => x.UpdatedAt).Take(take).ToListAsync();
    }

    public Task<bool> HasPendingAsync(string fromUser, string fromTeam, string toUser, string toTeam) =>
        _ctx.MatchInvitations.AnyAsync(x => x.Status == "pending" && x.FromUserId == fromUser && x.FromTeamId == fromTeam
                                            && x.ToUserId == toUser && x.ToTeamId == toTeam);

    public async Task AddAsync(MatchInvitation invitation)
    {
        _ctx.MatchInvitations.Add(invitation);
        await _ctx.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => _ctx.SaveChangesAsync();
}
