using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.TeamRepository;

public interface ITeamRepo
{
    Task<List<Team>> ListAsync(string uid, DateTime? updatedSince, bool includeDeleted, int offset, int take);
    Task<Team?> GetAsync(string uid, string id);
    /// <summary>Squad ids per team, ordered by sort_order.</summary>
    Task<Dictionary<string, List<string>>> GetPlayerIdsAsync(string uid, IEnumerable<string> teamIds);
    /// <summary>Returns the subset of <paramref name="playerIds"/> that does not exist for this user.</summary>
    Task<List<string>> MissingPlayersAsync(string uid, IEnumerable<string> playerIds);
    /// <summary>Registered users (other than the owner) that the given players are linked to.</summary>
    Task<List<string>> GetLinkedUserIdsAsync(string uid, IEnumerable<string> playerIds);
    /// <summary>Teams the user owns (not deleted).</summary>
    Task<int> CountActiveAsync(string uid);
    /// <summary>Public teams the user owns (not deleted), ignoring one team.</summary>
    Task<int> CountPublicAsync(string uid, string? exceptTeamId);
    Task SaveAsync(Team team, bool isNew, List<string> playerIds);
    Task SoftDeleteAsync(Team team, DateTime now);
}

public class TeamRepo : ITeamRepo
{
    private readonly CricketContext _ctx;

    public TeamRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    public Task<List<Team>> ListAsync(string uid, DateTime? updatedSince, bool includeDeleted, int offset, int take)
    {
        var q = _ctx.Teams.AsNoTracking().Where(x => x.UserId == uid);
        if (!includeDeleted) q = q.Where(x => x.DeletedAt == null);
        if (updatedSince.HasValue) q = q.Where(x => x.UpdatedAt > updatedSince.Value);
        return q.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id).Skip(offset).Take(take).ToListAsync();
    }

    public Task<Team?> GetAsync(string uid, string id) =>
        _ctx.Teams.FirstOrDefaultAsync(x => x.UserId == uid && x.Id == id);

    public async Task<Dictionary<string, List<string>>> GetPlayerIdsAsync(string uid, IEnumerable<string> teamIds)
    {
        var ids = teamIds.ToList();
        var rows = await _ctx.TeamPlayers.AsNoTracking()
            .Where(x => x.UserId == uid && ids.Contains(x.TeamId))
            .OrderBy(x => x.SortOrder).ToListAsync();
        return rows.GroupBy(x => x.TeamId).ToDictionary(g => g.Key, g => g.Select(x => x.PlayerId).ToList());
    }

    public async Task<List<string>> MissingPlayersAsync(string uid, IEnumerable<string> playerIds)
    {
        var ids = playerIds.Distinct().ToList();
        if (ids.Count == 0) return ids;
        var found = await _ctx.Players.AsNoTracking().Where(x => x.UserId == uid && ids.Contains(x.Id))
            .Select(x => x.Id).ToListAsync();
        return ids.Except(found).ToList();
    }

    public Task<List<string>> GetLinkedUserIdsAsync(string uid, IEnumerable<string> playerIds)
    {
        var ids = playerIds.Distinct().ToList();
        return _ctx.Players.AsNoTracking()
            .Where(x => x.UserId == uid && ids.Contains(x.Id) && x.LinkedUserId != null && x.LinkedUserId != uid)
            .Select(x => x.LinkedUserId!).Distinct().ToListAsync();
    }

    public Task<int> CountActiveAsync(string uid) =>
        _ctx.Teams.AsNoTracking().CountAsync(x => x.UserId == uid && x.DeletedAt == null);

    public Task<int> CountPublicAsync(string uid, string? exceptTeamId) =>
        _ctx.Teams.AsNoTracking().CountAsync(x => x.UserId == uid && x.DeletedAt == null && x.IsPublic
                                                   && (exceptTeamId == null || x.Id != exceptTeamId));

    public async Task SaveAsync(Team team, bool isNew, List<string> playerIds)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();
        if (isNew) _ctx.Teams.Add(team);
        await _ctx.SaveChangesAsync();

        await _ctx.TeamPlayers.Where(x => x.UserId == team.UserId && x.TeamId == team.Id).ExecuteDeleteAsync();
        var order = 0;
        foreach (var pid in playerIds.Distinct())
        {
            _ctx.TeamPlayers.Add(new TeamPlayer { UserId = team.UserId, TeamId = team.Id, PlayerId = pid, SortOrder = order++ });
        }
        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public Task SoftDeleteAsync(Team team, DateTime now)
    {
        team.DeletedAt = now;
        team.UpdatedAt = now;
        return _ctx.SaveChangesAsync();
    }
}
