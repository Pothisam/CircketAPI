using Cricket_Models.CommonModels;
using Cricket_Models.TeamModels;
using Cricket_Repository.Entity;
using Cricket_Repository.TeamRepository;

namespace Cricket_Services.TeamServices;

public interface ITeamService
{
    Task<ListResponse<TeamDto>> ListAsync(string uid, int? limit, string? cursor, DateTime? updatedSince, bool includeDeleted);
    Task<List<TeamDto>> ListChangedAsync(string uid, DateTime? since);         // for sync (includes soft-deleted)
    Task<TeamDto> GetAsync(string uid, string id);
    Task<TeamDto> UpsertAsync(string uid, string id, TeamDto dto);
    Task DeleteAsync(string uid, string id);
}

public class TeamService : ITeamService
{
    private readonly ITeamRepo _teams;

    public TeamService(ITeamRepo teams)
    {
        _teams = teams;
    }

    public async Task<ListResponse<TeamDto>> ListAsync(string uid, int? limit, string? cursor, DateTime? updatedSince, bool includeDeleted)
    {
        var (l, offset) = Paging.Parse(limit, cursor);
        var rows = await _teams.ListAsync(uid, updatedSince, includeDeleted, offset, l + 1);
        var page = Paging.Build(rows, l, offset);
        var squads = await _teams.GetPlayerIdsAsync(uid, page.Items.Select(t => t.Id));
        return new ListResponse<TeamDto>
        {
            Items = page.Items.Select(t => ToDto(t, squads.GetValueOrDefault(t.Id))).ToList(),
            NextCursor = page.NextCursor
        };
    }

    public async Task<List<TeamDto>> ListChangedAsync(string uid, DateTime? since)
    {
        var rows = await _teams.ListAsync(uid, since, true, 0, 100000);
        var squads = await _teams.GetPlayerIdsAsync(uid, rows.Select(t => t.Id));
        return rows.Select(t => ToDto(t, squads.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<TeamDto> GetAsync(string uid, string id)
    {
        var t = await _teams.GetAsync(uid, id);
        if (t == null || t.DeletedAt != null) throw ApiException.NotFound("Team");
        return await ToDtoAsync(uid, t);
    }

    public async Task<TeamDto> UpsertAsync(string uid, string id, TeamDto dto)
    {
        var name = dto.Name?.Trim() ?? "";
        var shortName = dto.ShortName?.Trim() ?? "";
        var playerIds = (dto.PlayerIds ?? new()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();

        var v = new ValidationBuilder()
            .Check(name.Length is > 0 and <= 80, "name", "required, max 80 characters")
            .Check(shortName.Length is > 0 and <= 8, "shortName", "required, max 8 characters")
            .Check(dto.ColorIndex >= 0, "colorIndex", "must be >= 0");
        v.ThrowIfAny();

        var missing = await _teams.MissingPlayersAsync(uid, playerIds);
        if (missing.Count > 0)
            throw ApiException.Validation("playerIds: unknown player(s)",
                missing.Select(m => new ErrorDetail("playerIds", $"unknown player '{m}'")).ToList());

        var now = Clock.UtcNow();
        var team = await _teams.GetAsync(uid, id);
        var isNew = team == null;
        if (team != null && dto.Version.HasValue && dto.Version.Value < team.Version)
        {
            throw ApiException.Conflict("Version conflict: the server has a newer team",
                new() { ["current"] = await ToDtoAsync(uid, team) });
        }

        team ??= new Team { Id = id, UserId = uid, CreatedAt = now };
        team.Name = name;
        team.ShortName = shortName;
        team.ColorIndex = dto.ColorIndex;
        team.Version = isNew ? 1 : team.Version + 1;
        team.UpdatedAt = now;
        team.DeletedAt = null;                           // an upsert revives a soft-deleted team

        await _teams.SaveAsync(team, isNew, playerIds);
        return ToDto(team, playerIds);
    }

    public async Task DeleteAsync(string uid, string id)
    {
        var t = await _teams.GetAsync(uid, id);
        if (t == null || t.DeletedAt != null) throw ApiException.NotFound("Team");
        await _teams.SoftDeleteAsync(t, Clock.UtcNow());
    }

    private async Task<TeamDto> ToDtoAsync(string uid, Team t)
    {
        var squads = await _teams.GetPlayerIdsAsync(uid, new[] { t.Id });
        return ToDto(t, squads.GetValueOrDefault(t.Id));
    }

    private static TeamDto ToDto(Team t, List<string>? playerIds) => new()
    {
        Id = t.Id,
        Name = t.Name,
        ShortName = t.ShortName,
        ColorIndex = t.ColorIndex,
        PlayerIds = playerIds ?? new List<string>(),
        Version = t.Version,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        DeletedAt = t.DeletedAt
    };
}
