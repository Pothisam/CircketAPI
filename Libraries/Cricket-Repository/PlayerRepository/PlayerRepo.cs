using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.PlayerRepository;

public interface IPlayerRepo
{
    Task<List<Player>> ListAsync(string uid, DateTime? updatedSince, bool includeDeleted, int offset, int take);
    Task<Player?> GetAsync(string uid, string id);
    Task SaveAsync(Player player, bool isNew);
    /// <summary>Soft-deletes the player and removes them from every squad.</summary>
    Task SoftDeleteAsync(Player player, DateTime now);
}

public class PlayerRepo : IPlayerRepo
{
    private readonly CricketContext _ctx;

    public PlayerRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    public Task<List<Player>> ListAsync(string uid, DateTime? updatedSince, bool includeDeleted, int offset, int take)
    {
        var q = _ctx.Players.AsNoTracking().Where(x => x.UserId == uid);
        if (!includeDeleted) q = q.Where(x => x.DeletedAt == null);
        if (updatedSince.HasValue) q = q.Where(x => x.UpdatedAt > updatedSince.Value);
        return q.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id).Skip(offset).Take(take).ToListAsync();
    }

    public Task<Player?> GetAsync(string uid, string id) =>
        _ctx.Players.FirstOrDefaultAsync(x => x.UserId == uid && x.Id == id);

    public async Task SaveAsync(Player player, bool isNew)
    {
        if (isNew) _ctx.Players.Add(player);
        await _ctx.SaveChangesAsync();
    }

    public async Task SoftDeleteAsync(Player player, DateTime now)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();
        player.DeletedAt = now;
        player.UpdatedAt = now;
        player.Version++;
        await _ctx.SaveChangesAsync();
        await _ctx.TeamPlayers.Where(x => x.UserId == player.UserId && x.PlayerId == player.Id).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }
}
