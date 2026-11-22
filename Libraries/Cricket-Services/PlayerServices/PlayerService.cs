using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.PlayerModels;
using Cricket_Repository.Entity;
using Cricket_Repository.PlayerRepository;

namespace Cricket_Services.PlayerServices;

public interface IPlayerService
{
    Task<ListResponse<PlayerDto>> ListAsync(string uid, int? limit, string? cursor, DateTime? updatedSince, bool includeDeleted);
    Task<List<PlayerDto>> ListChangedAsync(string uid, DateTime? since);       // for sync (includes soft-deleted)
    Task<PlayerDto> GetAsync(string uid, string id);
    Task<PlayerDto> UpsertAsync(string uid, string id, PlayerDto dto);
    Task DeleteAsync(string uid, string id);
}

public class PlayerService : IPlayerService
{
    private readonly IPlayerRepo _players;

    public PlayerService(IPlayerRepo players)
    {
        _players = players;
    }

    public async Task<ListResponse<PlayerDto>> ListAsync(string uid, int? limit, string? cursor, DateTime? updatedSince, bool includeDeleted)
    {
        var (l, offset) = Paging.Parse(limit, cursor);
        var rows = await _players.ListAsync(uid, updatedSince, includeDeleted, offset, l + 1);
        var page = Paging.Build(rows, l, offset);
        return new ListResponse<PlayerDto> { Items = page.Items.Select(ToDto).ToList(), NextCursor = page.NextCursor };
    }

    public async Task<List<PlayerDto>> ListChangedAsync(string uid, DateTime? since) =>
        (await _players.ListAsync(uid, since, true, 0, 100000)).Select(ToDto).ToList();

    public async Task<PlayerDto> GetAsync(string uid, string id)
    {
        var p = await _players.GetAsync(uid, id);
        if (p == null || p.DeletedAt != null) throw ApiException.NotFound("Player");
        return ToDto(p);
    }

    public async Task<PlayerDto> UpsertAsync(string uid, string id, PlayerDto dto)
    {
        var name = dto.Name?.Trim() ?? "";
        new ValidationBuilder()
            .Check(name.Length is > 0 and <= 120, "name", "required, max 120 characters")
            .OneOf(dto.Role, CricketEnums.PlayerRoles, "role")
            .OneOf(dto.BattingStyle, CricketEnums.BattingStyles, "battingStyle")
            .OneOf(dto.BowlingStyle, CricketEnums.BowlingStyles, "bowlingStyle")
            .ThrowIfAny();

        var now = Clock.UtcNow();
        var player = await _players.GetAsync(uid, id);
        var isNew = player == null;
        if (player != null && dto.Version.HasValue && dto.Version.Value < player.Version)
        {
            throw ApiException.Conflict("Version conflict: the server has a newer player",
                new() { ["current"] = ToDto(player) });
        }

        player ??= new Player { Id = id, UserId = uid, CreatedAt = now };
        player.Name = name;
        player.Role = dto.Role;
        player.BattingStyle = dto.BattingStyle;
        player.BowlingStyle = dto.BowlingStyle;
        player.Version = isNew ? 1 : player.Version + 1;
        player.UpdatedAt = now;
        player.DeletedAt = null;

        await _players.SaveAsync(player, isNew);
        return ToDto(player);
    }

    public async Task DeleteAsync(string uid, string id)
    {
        var p = await _players.GetAsync(uid, id);
        if (p == null || p.DeletedAt != null) throw ApiException.NotFound("Player");
        await _players.SoftDeleteAsync(p, Clock.UtcNow());
    }

    private static PlayerDto ToDto(Player p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Role = p.Role,
        BattingStyle = p.BattingStyle,
        BowlingStyle = p.BowlingStyle,
        Version = p.Version,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        DeletedAt = p.DeletedAt
    };
}
