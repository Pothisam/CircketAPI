using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.PlayerRepository;

/// <summary>A registered user found by player search, with their own player (cricket details) if any.</summary>
public record UserSearchHit(User User, Player? Self);

public interface IPlayerRepo
{
    Task<List<Player>> ListAsync(string uid, DateTime? updatedSince, bool includeDeleted, int offset, int take);
    Task<Player?> GetAsync(string uid, string id);
    Task SaveAsync(Player player, bool isNew);
    /// <summary>Soft-deletes the player and removes them from every squad.</summary>
    Task SoftDeleteAsync(Player player, DateTime now);

    // ---- profile / linking ----
    /// <summary>The player that represents <paramref name="uid"/> in their own roster (even if soft-deleted).</summary>
    Task<Player?> GetSelfAsync(string uid);
    /// <summary>Players in the user's list (not deleted).</summary>
    Task<int> CountActiveAsync(string uid);
    /// <summary>A live (not deleted) player in the roster with this e-mail, optionally ignoring one id.</summary>
    Task<Player?> FindByEmailAsync(string uid, string email, string? exceptId = null);
    /// <summary>The player in <paramref name="uid"/>'s roster that is linked to <paramref name="userId"/> (even if deleted).</summary>
    Task<Player?> GetByLinkedUserAsync(string uid, string userId);
    /// <summary>Id of the active user that owns this e-mail, or null.</summary>
    Task<string?> FindUserIdByEmailAsync(string email);
    /// <summary>Links every unlinked manual player (in any roster) with this e-mail to the user. Returns the number linked.</summary>
    Task<int> LinkByEmailAsync(string email, string uid);

    // ---- search ----
    Task<List<Player>> SearchOwnAsync(string uid, string term, int take);
    Task<List<UserSearchHit>> SearchUsersAsync(string uid, string term, bool exactEmail, int take);
    Task<UserSearchHit?> GetDiscoverableUserAsync(string userId);

    // ---- registered users own their data ----
    /// <summary>The user and their own player, whether or not they are discoverable.</summary>
    Task<UserSearchHit?> GetUserWithSelfAsync(string userId);
    /// <summary>
    /// Copies the user's public profile (name, role, styles, city, photo) onto every copy of them in other
    /// people's player lists and clears the private fields there. Only rows that differ are touched, so
    /// nobody re-syncs for nothing. Returns the number of copies updated.
    /// </summary>
    Task<int> MirrorProfileAsync(string userId, string name, string role, string battingStyle, string bowlingStyle,
        string? city, string? photoUrl);
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
        await _ctx.SaveChangesAsync();
        await _ctx.TeamPlayers.Where(x => x.UserId == player.UserId && x.PlayerId == player.Id).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }

    // ------------------------------------------------------------ profile / linking

    public Task<int> CountActiveAsync(string uid) =>
        _ctx.Players.AsNoTracking().CountAsync(x => x.UserId == uid && x.DeletedAt == null);

    public Task<Player?> GetSelfAsync(string uid) =>
        _ctx.Players.FirstOrDefaultAsync(x => x.UserId == uid && x.IsSelf);

    public Task<Player?> FindByEmailAsync(string uid, string email, string? exceptId = null) =>
        _ctx.Players.FirstOrDefaultAsync(x => x.UserId == uid && x.DeletedAt == null && x.Email == email
                                              && (exceptId == null || x.Id != exceptId));

    public Task<Player?> GetByLinkedUserAsync(string uid, string userId) =>
        _ctx.Players.FirstOrDefaultAsync(x => x.UserId == uid && x.LinkedUserId == userId && !x.IsSelf);

    public Task<string?> FindUserIdByEmailAsync(string email) =>
        _ctx.Users.AsNoTracking().Where(x => x.Email == email && x.IsActive).Select(x => x.Id).FirstOrDefaultAsync();

    public Task<int> LinkByEmailAsync(string email, string uid)
    {
        var now = DateTime.UtcNow;
        // Bump version / updated_at so the owners' devices pick the change up on their next sync.
        return _ctx.Players.Where(x => x.Email == email && x.LinkedUserId == null && !x.IsSelf && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LinkedUserId, uid)
                .SetProperty(x => x.UpdatedAt, now));
    }

    // ------------------------------------------------------------------- search

    public Task<List<Player>> SearchOwnAsync(string uid, string term, int take) =>
        _ctx.Players.AsNoTracking()
            .Where(x => x.UserId == uid && x.DeletedAt == null
                        && (x.Name.Contains(term) || (x.Email != null && x.Email.Contains(term))))
            .OrderByDescending(x => x.IsSelf).ThenBy(x => x.Name)
            .Take(take).ToListAsync();

    public async Task<List<UserSearchHit>> SearchUsersAsync(string uid, string term, bool exactEmail, int take)
    {
        // Users already in the caller's list are returned as "mine" instead.
        var alreadyLinked = await _ctx.Players.AsNoTracking()
            .Where(x => x.UserId == uid && x.DeletedAt == null && x.LinkedUserId != null)
            .Select(x => x.LinkedUserId!).ToListAsync();

        var users = _ctx.Users.AsNoTracking()
            .Where(u => u.IsActive && u.IsDiscoverable && u.Id != uid && !alreadyLinked.Contains(u.Id));
        users = exactEmail
            ? users.Where(u => u.Email == term)
            : users.Where(u => u.DisplayName != null && u.DisplayName.Contains(term));

        var rows = await (from u in users.OrderBy(u => u.DisplayName).Take(take)
                          join p in _ctx.Players.Where(p => p.IsSelf && p.DeletedAt == null) on u.Id equals p.UserId into g
                          from p in g.DefaultIfEmpty()
                          select new { User = u, Self = p }).ToListAsync();
        return rows.Select(r => new UserSearchHit(r.User, r.Self)).ToList();
    }

    public async Task<UserSearchHit?> GetDiscoverableUserAsync(string userId)
    {
        var u = await _ctx.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.IsActive && x.IsDiscoverable);
        if (u == null) return null;
        var self = await _ctx.Players.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.IsSelf && x.DeletedAt == null);
        return new UserSearchHit(u, self);
    }

    public async Task<UserSearchHit?> GetUserWithSelfAsync(string userId)
    {
        var u = await _ctx.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);
        if (u == null) return null;
        var self = await _ctx.Players.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.IsSelf && x.DeletedAt == null);
        return new UserSearchHit(u, self);
    }

    public Task<int> MirrorProfileAsync(string userId, string name, string role, string battingStyle, string bowlingStyle,
        string? city, string? photoUrl)
    {
        var now = DateTime.UtcNow;
        var cityValue = city ?? "";
        var photoValue = photoUrl ?? "";
        return _ctx.Players
            .Where(x => x.LinkedUserId == userId && !x.IsSelf && x.DeletedAt == null
                        && (x.Name != name || x.Role != role || x.BattingStyle != battingStyle || x.BowlingStyle != bowlingStyle
                            || (x.City ?? "") != cityValue || (x.PhotoUrl ?? "") != photoValue
                            || x.HeightCm != null || x.WeightKg != null || x.DateOfBirth != null
                            || x.PhoneNumber != null || x.Gender != null || x.JerseyNumber != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, name)
                .SetProperty(x => x.Role, role)
                .SetProperty(x => x.BattingStyle, battingStyle)
                .SetProperty(x => x.BowlingStyle, bowlingStyle)
                .SetProperty(x => x.City, city)
                .SetProperty(x => x.PhotoUrl, photoUrl)
                // Private details belong to the person, not to the people who added them.
                .SetProperty(x => x.HeightCm, (decimal?)null)
                .SetProperty(x => x.WeightKg, (decimal?)null)
                .SetProperty(x => x.DateOfBirth, (DateOnly?)null)
                .SetProperty(x => x.PhoneNumber, (string?)null)
                .SetProperty(x => x.Gender, (string?)null)
                .SetProperty(x => x.JerseyNumber, (int?)null)
                .SetProperty(x => x.UpdatedAt, now));
    }
}
