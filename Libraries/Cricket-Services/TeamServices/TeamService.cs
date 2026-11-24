using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.TeamModels;
using Cricket_Repository.Entity;
using Cricket_Repository.PlayerRepository;
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
    /// <summary>The owner saves a team. Applies the limit of 3 teams (1 public, 2 private).</summary>
    Task<TeamDto> UpsertAsync(string uid, string id, TeamDto dto);
    Task DeleteAsync(string uid, string id);

    /// <summary>
    /// A manager (a user the team was handed to) changes the team. The caller has already checked that
    /// <paramref name="managerUid"/> is a manager. Visibility never changes, and the creator stays in the squad.
    /// </summary>
    Task<TeamDto> UpdateByManagerAsync(string managerUid, string ownerUid, string teamId, TeamDto dto);
}

public class TeamService : ITeamService
{
    private readonly ITeamRepo _teams;
    private readonly IUserRepo _users;
    private readonly IPlayerRepo _players;
    private readonly INotificationDispatcher _dispatcher;

    public TeamService(ITeamRepo teams, IUserRepo users, IPlayerRepo players, INotificationDispatcher dispatcher)
    {
        _teams = teams;
        _users = users;
        _players = players;
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

    public Task<TeamDto> UpsertAsync(string uid, string id, TeamDto dto) => UpsertCoreAsync(uid, id, dto, uid, false);

    public async Task<TeamDto> UpdateByManagerAsync(string managerUid, string ownerUid, string teamId, TeamDto dto)
    {
        var team = await _teams.GetAsync(ownerUid, teamId);
        if (team == null || team.DeletedAt != null) throw ApiException.NotFound("Team");
        dto.IsPublic = team.IsPublic;                       // visibility is the owner's decision
        return await UpsertCoreAsync(ownerUid, teamId, dto, managerUid, true);
    }

    private async Task<TeamDto> UpsertCoreAsync(string uid, string id, TeamDto dto, string actorUid, bool byManager)
    {
        var name = dto.Name?.Trim() ?? "";
        var shortName = dto.ShortName?.Trim() ?? "";
        var playerIds = (dto.PlayerIds ?? new()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();

        new ValidationBuilder()
            .Check(name.Length is > 0 and <= 80, "name", "required, max 80 characters")
            .Check(shortName.Length is > 0 and <= 8, "shortName", "required, max 8 characters")
            .Check(dto.ColorIndex >= 0, "colorIndex", "must be >= 0")
            .ThrowIfAny();

        var missing = await _teams.MissingPlayersAsync(uid, playerIds);
        if (missing.Count > 0)
            throw ApiException.Validation("playerIds: unknown player(s)",
                missing.Select(m => new ErrorDetail("playerIds", $"unknown player '{m}'")).ToList());

        // Captain / vice-captain have to be in the squad; if they are not (anymore) the role is simply cleared.
        var captain = Clean(dto.CaptainPlayerId);
        var vice = Clean(dto.ViceCaptainPlayerId);
        if (captain != null && !playerIds.Contains(captain)) captain = null;
        if (vice != null && (!playerIds.Contains(vice) || vice == captain)) vice = null;

        var now = Clock.UtcNow();
        var team = await _teams.GetAsync(uid, id);
        var isNew = team == null;
        var creating = team == null || team.DeletedAt != null;

        if (!byManager) await CheckLimitsAsync(uid, id, dto.IsPublic, creating, team);

        // Squad before this save: only players that are NEW in the squad get a notification.
        var squadBefore = isNew
            ? new List<string>()
            : (await _teams.GetPlayerIdsAsync(uid, new[] { id })).GetValueOrDefault(id) ?? new List<string>();

        if (byManager)
        {
            // The creator can never be removed by a manager.
            var creator = await _players.GetSelfAsync(uid);
            if (creator != null && squadBefore.Contains(creator.Id) && !playerIds.Contains(creator.Id))
                throw ApiException.Forbidden("The creator of the team cannot be removed from it.");
        }

        team ??= new Team { Id = id, UserId = uid, CreatedAt = now };
        team.Name = name;
        team.ShortName = shortName;
        team.ColorIndex = dto.ColorIndex;
        team.IsPublic = dto.IsPublic;
        team.CaptainPlayerId = captain;
        team.ViceCaptainPlayerId = vice;
        team.UpdatedAt = now;
        team.DeletedAt = null;                           // an upsert revives a soft-deleted team

        await _teams.SaveAsync(team, isNew, playerIds);

        var added = playerIds.Except(squadBefore).ToList();
        if (added.Count > 0) await QueueTeamAddedAsync(uid, actorUid, team, added);

        return ToDto(team, playerIds);
    }

    /// <summary>
    /// 3 teams per user: 1 public and 2 private (duplicates count). Teams that exist already are never
    /// touched: only creating a team, or changing its visibility, is checked.
    /// </summary>
    private async Task CheckLimitsAsync(string uid, string id, bool wantsPublic, bool creating, Team? existing)
    {
        var becomesPublic = wantsPublic && (creating || !existing!.IsPublic);
        var becomesPrivate = !wantsPublic && (creating || existing!.IsPublic);
        if (!creating && !becomesPublic && !becomesPrivate) return;

        var active = await _teams.CountActiveAsync(uid);
        var publicOthers = await _teams.CountPublicAsync(uid, id);
        // Everything except this team (when it already counts).
        var countsAlready = !creating;
        var othersTotal = active - (countsAlready ? 1 : 0);
        var privateOthers = othersTotal - publicOthers;

        if (creating && active >= TeamLimits.MaxTeams)
            throw ApiException.LimitReached(
                $"You can have up to {TeamLimits.MaxTeams} teams ({TeamLimits.MaxPublic} public and {TeamLimits.MaxPrivate} private). " +
                "Delete a team to create a new one.");
        if (becomesPublic && publicOthers >= TeamLimits.MaxPublic)
            throw ApiException.LimitReached(
                $"Only {TeamLimits.MaxPublic} team can be public. Make your other public team private first.");
        if (becomesPrivate && privateOthers >= TeamLimits.MaxPrivate)
            throw ApiException.LimitReached(
                $"You can have up to {TeamLimits.MaxPrivate} private teams. Delete one or make a team public (only {TeamLimits.MaxPublic} can be public).");
    }

    public async Task DeleteAsync(string uid, string id)
    {
        var t = await _teams.GetAsync(uid, id);
        if (t == null || t.DeletedAt != null) throw ApiException.NotFound("Team");
        await _teams.SoftDeleteAsync(t, Clock.UtcNow());
    }

    /// <summary>Tells every registered user who was just added to the squad (from the background queue).</summary>
    private async Task QueueTeamAddedAsync(string ownerUid, string actorUid, Team team, List<string> addedPlayerIds)
    {
        var targets = await _teams.GetLinkedUserIdsAsync(ownerUid, addedPlayerIds);
        if (targets.Count == 0) return;

        var actor = await _users.GetUserAsync(actorUid);
        var actorName = string.IsNullOrWhiteSpace(actor?.DisplayName) ? "A scorer" : actor!.DisplayName!;
        var teamId = team.Id;
        var teamName = team.Name;

        foreach (var target in targets)
        {
            _dispatcher.Enqueue(sp => sp.GetRequiredService<INotificationService>().SendToUserAsync(target, new OutgoingNotification
            {
                Type = NotificationTypes.TeamAdded,
                Title = "You were added to a team",
                Body = $"{actorName} added you to {teamName}",
                Data = new Dictionary<string, string> { ["teamId"] = teamId, ["ownerId"] = ownerUid, ["teamName"] = teamName }
            }));
        }
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

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
        CaptainPlayerId = t.CaptainPlayerId,
        ViceCaptainPlayerId = t.ViceCaptainPlayerId,
        PlayerIds = playerIds ?? new List<string>(),
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        DeletedAt = t.DeletedAt
    };
}
