using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.PlayerModels;
using Cricket_Models.TeamModels;
using Cricket_Repository.Entity;
using Cricket_Repository.PlayerRepository;
using Cricket_Repository.TeamManagerRepository;
using Cricket_Repository.TeamRepository;
using Cricket_Repository.UserRepository;
using Cricket_Services.NotificationServices;
using Cricket_Services.PlayerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Cricket_Services.TeamServices;

public interface ITeamManagementService
{
    // ---- owner: hand the team to other users
    Task<List<ManagerDto>> ListManagersAsync(string me, string teamId);
    Task<List<ManagerDto>> AddManagerAsync(string me, string teamId, string userId);
    Task<List<ManagerDto>> RemoveManagerAsync(string me, string teamId, string userId);

    // ---- manager: run a team that was handed over
    Task<List<ManagedTeamDto>> ListManagedAsync(string me);
    Task<ManagedTeamDto> GetManagedAsync(string me, string ownerId, string teamId);
    Task<ManagedTeamDto> UpdateManagedAsync(string me, string ownerId, string teamId, ManagedTeamUpdate update);
    Task<ManagedTeamDto> AddManagedPlayerAsync(string me, string ownerId, string teamId, AddManagedPlayerRequest request);
    /// <summary>A manager gives the team back (stops managing it).</summary>
    Task LeaveAsync(string me, string ownerId, string teamId);
}

/// <summary>
/// Handing a team over. The creator stays the owner: only the owner deletes the team, changes its
/// visibility and appoints managers. A manager runs it: name, colour, squad, order, captain.
/// </summary>
public class TeamManagementService : ITeamManagementService
{
    private readonly ITeamRepo _teams;
    private readonly ITeamManagerRepo _managers;
    private readonly IPlayerRepo _players;
    private readonly IPlayerService _playerService;
    private readonly ITeamService _teamService;
    private readonly IUserRepo _users;
    private readonly INotificationDispatcher _dispatcher;

    public TeamManagementService(ITeamRepo teams, ITeamManagerRepo managers, IPlayerRepo players, IPlayerService playerService,
        ITeamService teamService, IUserRepo users, INotificationDispatcher dispatcher)
    {
        _teams = teams;
        _managers = managers;
        _players = players;
        _playerService = playerService;
        _teamService = teamService;
        _users = users;
        _dispatcher = dispatcher;
    }

    // ============================================================== owner

    public async Task<List<ManagerDto>> ListManagersAsync(string me, string teamId)
    {
        await RequireOwnTeamAsync(me, teamId);
        return await ManagersAsync(me, teamId);
    }

    public async Task<List<ManagerDto>> AddManagerAsync(string me, string teamId, string userId)
    {
        var team = await RequireOwnTeamAsync(me, teamId);
        if (userId == me)
            throw ApiException.Validation("You already own this team.", new List<ErrorDetail> { new("userId", "is yourself") });

        var target = await _users.GetUserAsync(userId);
        if (target == null || !target.IsActive) throw ApiException.NotFound("User");
        // Only somebody the owner can see: a discoverable user, or a player of their own list that is that user.
        var known = target.IsDiscoverable || await _players.GetByLinkedUserAsync(me, userId) != null;
        if (!known) throw ApiException.NotFound("User");

        var current = await _managers.ListManagersAsync(me, teamId);
        if (current.Any(m => m.UserId == userId)) return await ManagersAsync(me, teamId);
        if (current.Count >= TeamLimits.MaxManagers)
            throw ApiException.LimitReached($"A team can have up to {TeamLimits.MaxManagers} managers.");

        await _managers.AddAsync(new TeamManager { OwnerUserId = me, TeamId = teamId, ManagerUserId = userId, CreatedAt = Clock.UtcNow() });

        var owner = await _users.GetUserAsync(me);
        Notify(userId, "Team handed over to you",
            $"{NameOf(owner?.DisplayName)} made you a manager of {team.Name}. You can run the team, but not delete it.",
            new Dictionary<string, string> { ["teamId"] = teamId, ["ownerId"] = me, ["teamName"] = team.Name });
        return await ManagersAsync(me, teamId);
    }

    public async Task<List<ManagerDto>> RemoveManagerAsync(string me, string teamId, string userId)
    {
        var team = await RequireOwnTeamAsync(me, teamId);
        if (await _managers.RemoveAsync(me, teamId, userId))
        {
            Notify(userId, "Team taken back", $"You are no longer a manager of {team.Name}.",
                new Dictionary<string, string> { ["teamId"] = teamId, ["ownerId"] = me, ["teamName"] = team.Name });
        }
        return await ManagersAsync(me, teamId);
    }

    // ============================================================ manager

    public async Task<List<ManagedTeamDto>> ListManagedAsync(string me)
    {
        var rows = await _managers.ListManagedTeamsAsync(me);
        return rows.Select(r => Header(r.Team, r.OwnerName, r.PlayerCount)).ToList();
    }

    public async Task<ManagedTeamDto> GetManagedAsync(string me, string ownerId, string teamId)
    {
        var team = await RequireManagedAsync(me, ownerId, teamId);
        return await BuildDetailAsync(team);
    }

    public async Task<ManagedTeamDto> UpdateManagedAsync(string me, string ownerId, string teamId, ManagedTeamUpdate u)
    {
        await RequireManagedAsync(me, ownerId, teamId);
        await _teamService.UpdateByManagerAsync(me, ownerId, teamId, new TeamDto
        {
            Id = teamId,
            Name = u.Name,
            ShortName = u.ShortName,
            ColorIndex = u.ColorIndex,
            PlayerIds = u.PlayerIds ?? new List<string>(),
            CaptainPlayerId = u.CaptainPlayerId,
            ViceCaptainPlayerId = u.ViceCaptainPlayerId
        });
        return await GetManagedAsync(me, ownerId, teamId);
    }

    public async Task<ManagedTeamDto> AddManagedPlayerAsync(string me, string ownerId, string teamId, AddManagedPlayerRequest r)
    {
        var team = await RequireManagedAsync(me, ownerId, teamId);

        string playerId;
        if (!string.IsNullOrWhiteSpace(r.UserId))
        {
            // A registered user: they become a (linked) player in the owner's list.
            var (player, _) = await _playerService.AddFromUserAsync(ownerId, r.UserId!.Trim());
            playerId = player.Id;
        }
        else
        {
            var name = r.Name?.Trim() ?? "";
            if (name.Length == 0) throw ApiException.Validation("Give a name or choose a registered user.");
            playerId = Guid.NewGuid().ToString("N");
            await _playerService.UpsertAsync(ownerId, playerId, new PlayerDto { Id = playerId, Name = name, Role = r.Role ?? "batter" });
        }

        var squad = (await _teams.GetPlayerIdsAsync(ownerId, new[] { teamId })).GetValueOrDefault(teamId) ?? new List<string>();
        if (!squad.Contains(playerId)) squad.Add(playerId);

        await _teamService.UpdateByManagerAsync(me, ownerId, teamId, new TeamDto
        {
            Id = teamId,
            Name = team.Name,
            ShortName = team.ShortName,
            ColorIndex = team.ColorIndex,
            PlayerIds = squad,
            CaptainPlayerId = team.CaptainPlayerId,
            ViceCaptainPlayerId = team.ViceCaptainPlayerId
        });
        return await GetManagedAsync(me, ownerId, teamId);
    }

    public async Task LeaveAsync(string me, string ownerId, string teamId)
    {
        if (!await _managers.RemoveAsync(ownerId, teamId, me)) throw ApiException.NotFound("Team");
    }

    // ============================================================ helpers

    private async Task<Team> RequireOwnTeamAsync(string me, string teamId)
    {
        var t = await _teams.GetAsync(me, teamId);
        if (t == null || t.DeletedAt != null) throw ApiException.NotFound("Team");
        return t;
    }

    /// <summary>The team, if <paramref name="me"/> is one of its managers (otherwise "not found": nothing is revealed).</summary>
    private async Task<Team> RequireManagedAsync(string me, string ownerId, string teamId)
    {
        if (!await _managers.IsManagerAsync(me, ownerId, teamId)) throw ApiException.NotFound("Team");
        var t = await _teams.GetAsync(ownerId, teamId);
        if (t == null || t.DeletedAt != null) throw ApiException.NotFound("Team");
        return t;
    }

    private async Task<List<ManagerDto>> ManagersAsync(string owner, string teamId) =>
        (await _managers.ListManagersAsync(owner, teamId))
        .Select(m => new ManagerDto { UserId = m.UserId, Name = m.Name, PhotoUrl = m.PhotoUrl, Email = PlayerRules.MaskEmail(m.Email) })
        .ToList();

    private async Task<ManagedTeamDto> BuildDetailAsync(Team team)
    {
        var owner = await _users.GetUserAsync(team.UserId);
        var squadIds = (await _teams.GetPlayerIdsAsync(team.UserId, new[] { team.Id })).GetValueOrDefault(team.Id) ?? new List<string>();
        var roster = await _players.ListAsync(team.UserId, null, false, 0, 1000);
        var creator = roster.FirstOrDefault(p => p.IsSelf);
        var byId = roster.ToDictionary(p => p.Id);

        ManagedMemberDto Member(Player p) => new()
        {
            PlayerId = p.Id,
            Name = p.Name,
            Role = p.Role,
            BattingStyle = p.BattingStyle,
            BowlingStyle = p.BowlingStyle,
            PhotoUrl = p.PhotoUrl,
            IsRegistered = p.LinkedUserId != null,
            IsOwner = creator != null && p.Id == creator.Id
        };

        var dto = Header(team, owner?.DisplayName, squadIds.Count);
        dto.Squad = squadIds.Where(byId.ContainsKey).Select(id => Member(byId[id])).ToList();
        dto.Roster = roster.Where(p => !squadIds.Contains(p.Id)).OrderBy(p => p.Name).Select(Member).ToList();
        dto.PlayerCount = dto.Squad.Count;
        return dto;
    }

    private static ManagedTeamDto Header(Team t, string? ownerName, int count) => new()
    {
        OwnerId = t.UserId,
        OwnerName = ownerName,
        TeamId = t.Id,
        Name = t.Name,
        ShortName = t.ShortName,
        ColorIndex = t.ColorIndex,
        IsPublic = t.IsPublic,
        CaptainPlayerId = t.CaptainPlayerId,
        ViceCaptainPlayerId = t.ViceCaptainPlayerId,
        PlayerCount = count
    };

    private static string NameOf(string? n) => string.IsNullOrWhiteSpace(n) ? "The owner" : n!;

    private void Notify(string userId, string title, string body, Dictionary<string, string> data)
    {
        _dispatcher.Enqueue(sp => sp.GetRequiredService<INotificationService>().SendToUserAsync(userId, new OutgoingNotification
        {
            Type = NotificationTypes.TeamManager,
            Title = title,
            Body = body,
            Data = data
        }));
    }
}
