using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.TeamManagerRepository;

public record ManagerRow(string UserId, string? Name, string? PhotoUrl, string? Email);
public record ManagedTeamRow(Team Team, string? OwnerName, int PlayerCount);

/// <summary>Who may run a team besides its creator.</summary>
public interface ITeamManagerRepo
{
    Task<List<ManagerRow>> ListManagersAsync(string ownerId, string teamId);
    Task<bool> IsManagerAsync(string userId, string ownerId, string teamId);
    Task<int> CountManagersAsync(string ownerId, string teamId);
    Task AddAsync(TeamManager manager);
    Task<bool> RemoveAsync(string ownerId, string teamId, string managerUserId);
    /// <summary>Removes every manager of the team that is not in <paramref name="keepUserIds"/> (admins must be in the squad).</summary>
    Task<int> RemoveExceptAsync(string ownerId, string teamId, IEnumerable<string> keepUserIds);
    /// <summary>Teams of other users that were handed over to <paramref name="userId"/> (not deleted).</summary>
    Task<List<ManagedTeamRow>> ListManagedTeamsAsync(string userId);
}

public class TeamManagerRepo : ITeamManagerRepo
{
    private readonly CricketContext _ctx;

    public TeamManagerRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<List<ManagerRow>> ListManagersAsync(string ownerId, string teamId) =>
        (await (from m in _ctx.TeamManagers.AsNoTracking()
                where m.OwnerUserId == ownerId && m.TeamId == teamId
                join u in _ctx.Users.AsNoTracking() on m.ManagerUserId equals u.Id
                orderby m.CreatedAt
                select new { u.Id, u.DisplayName, u.PhotoUrl, u.Email }).ToListAsync())
        .Select(x => new ManagerRow(x.Id, x.DisplayName, x.PhotoUrl, x.Email)).ToList();

    public Task<bool> IsManagerAsync(string userId, string ownerId, string teamId) =>
        _ctx.TeamManagers.AsNoTracking().AnyAsync(m => m.ManagerUserId == userId && m.OwnerUserId == ownerId && m.TeamId == teamId);

    public Task<int> CountManagersAsync(string ownerId, string teamId) =>
        _ctx.TeamManagers.AsNoTracking().CountAsync(m => m.OwnerUserId == ownerId && m.TeamId == teamId);

    public async Task AddAsync(TeamManager manager)
    {
        _ctx.TeamManagers.Add(manager);
        await _ctx.SaveChangesAsync();
    }

    public async Task<bool> RemoveAsync(string ownerId, string teamId, string managerUserId) =>
        await _ctx.TeamManagers.Where(m => m.OwnerUserId == ownerId && m.TeamId == teamId && m.ManagerUserId == managerUserId)
            .ExecuteDeleteAsync() > 0;

    public Task<int> RemoveExceptAsync(string ownerId, string teamId, IEnumerable<string> keepUserIds)
    {
        var keep = keepUserIds.ToList();
        return _ctx.TeamManagers.Where(m => m.OwnerUserId == ownerId && m.TeamId == teamId && !keep.Contains(m.ManagerUserId))
            .ExecuteDeleteAsync();
    }

    public async Task<List<ManagedTeamRow>> ListManagedTeamsAsync(string userId)
    {
        var rows = await (from m in _ctx.TeamManagers.AsNoTracking()
                          where m.ManagerUserId == userId
                          join t in _ctx.Teams.AsNoTracking()
                              on new { UserId = m.OwnerUserId, Id = m.TeamId } equals new { t.UserId, t.Id }
                          where t.DeletedAt == null
                          join o in _ctx.Users.AsNoTracking() on t.UserId equals o.Id
                          orderby t.Name
                          select new { Team = t, Owner = o.DisplayName }).ToListAsync();
        if (rows.Count == 0) return new List<ManagedTeamRow>();

        var owners = rows.Select(r => r.Team.UserId).Distinct().ToList();
        var counts = (await _ctx.TeamPlayers.AsNoTracking()
                .Where(x => owners.Contains(x.UserId))
                .GroupBy(x => new { x.UserId, x.TeamId })
                .Select(g => new { g.Key.UserId, g.Key.TeamId, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => (x.UserId, x.TeamId), x => x.Count);

        return rows.Select(r => new ManagedTeamRow(r.Team, r.Owner, counts.GetValueOrDefault((r.Team.UserId, r.Team.Id)))).ToList();
    }
}
