using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.TeamModels;
using Cricket_Repository.Entity;
using Cricket_Repository.TeamRepository;
using Cricket_Repository.UserRepository;
using Cricket_Services.NotificationServices;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IUserRepo _users;
    private readonly INotificationDispatcher _dispatcher;

    public TeamService(ITeamRepo teams, IUserRepo users, INotificationDispatcher dispatcher)
    {
        _teams = teams;
        _users = users;
        _dispatcher = dispatcher;
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

        // Squad before this save: only players that are NEW in the squad get a notification.
        var squadBefore = isNew
            ? new List<string>()
            : (await _teams.GetPlayerIdsAsync(uid, new[] { id })).GetValueOrDefault(id) ?? new List<string>();

        team ??= new Team { Id = id, UserId = uid, CreatedAt = now };
        team.Name = name;
        team.ShortName = shortName;
        team.ColorIndex = dto.ColorIndex;
        team.IsPublic = dto.IsPublic;
        team.UpdatedAt = now;
        team.DeletedAt = null;                           // an upsert revives a soft-deleted team

        await _teams.SaveAsync(team, isNew, playerIds);

        var added = playerIds.Except(squadBefore).ToList();
        if (added.Count > 0) await QueueTeamAddedAsync(uid, team, added);

        return ToDto(team, playerIds);
    }

    public async Task DeleteAsync(string uid, string id)
    {
        var t = await _teams.GetAsync(uid, id);
        if (t == null || t.DeletedAt != null) throw ApiException.NotFound("Team");
        await _teams.SoftDeleteAsync(t, Clock.UtcNow());
    }

    /// <summary>Tells every registered user who was just added to the squad (from the background queue).</summary>
    private async Task QueueTeamAddedAsync(string ownerUid, Team team, List<string> addedPlayerIds)
    {
        var targets = await _teams.GetLinkedUserIdsAsync(ownerUid, addedPlayerIds);
        if (targets.Count == 0) return;

        var owner = await _users.GetUserAsync(ownerUid);
        var ownerName = string.IsNullOrWhiteSpace(owner?.DisplayName) ? "A scorer" : owner!.DisplayName!;
        var teamId = team.Id;
        var teamName = team.Name;

        foreach (var target in targets)
        {
            _dispatcher.Enqueue(sp => sp.GetRequiredService<INotificationService>().SendToUserAsync(target, new OutgoingNotification
            {
                Type = NotificationTypes.TeamAdded,
                Title = "You were added to a team",
                Body = $"{ownerName} added you to {teamName}",
                Data = new Dictionary<string, string> { ["teamId"] = teamId, ["ownerId"] = ownerUid, ["teamName"] = teamName }
            }));
        }
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
        IsPublic = t.IsPublic,
        PlayerIds = playerIds ?? new List<string>(),
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        DeletedAt = t.DeletedAt
    };
}
