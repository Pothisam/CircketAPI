using Cricket_Models.CommonModels;
using Cricket_Models.MatchModels;
using Cricket_Models.MembershipModels;
using Cricket_Repository.MatchRepository;
using Cricket_Repository.PlayerRepository;
using Cricket_Models.Enums;
using Cricket_Repository.MembershipRepository;
using Cricket_Repository.UserRepository;
using Cricket_Services.NotificationServices;
using Microsoft.Extensions.DependencyInjection;
using Cricket_Services.MatchServices;

namespace Cricket_Services.MembershipServices;

public interface IMembershipService
{
    /// <summary>Teams created by others in which the caller is a member.</summary>
    Task<List<JoinedTeamDto>> ListJoinedTeamsAsync(string me);
    Task<JoinedTeamDetailDto> GetJoinedTeamAsync(string me, string ownerId, string teamId);
    /// <summary>Leave a team somebody else added me to. The creator is told.</summary>
    Task LeaveTeamAsync(string me, string ownerId, string teamId);
    /// <summary>
    /// Public teams of other scorers in which one of MY players (a registered user) plays. Private teams are
    /// never shown, and neither are the teams of users who switched off "let other players find me".
    /// </summary>
    Task<List<JoinedTeamDto>> ListPublicTeamsOfPlayerAsync(string me, string playerId);
    /// <summary>Matches (of other scorers) the caller played in, or that a team of theirs played.</summary>
    Task<List<MatchSummaryDto>> ListMatchesAsync(string me, int? limit);
    /// <summary>Full scorecard of one of those matches (read only).</summary>
    Task<MatchDto> GetMatchAsync(string me, string ownerId, string matchId);
}

public class MembershipService : IMembershipService
{
    private readonly IMembershipRepo _membership;
    private readonly IMatchRepo _matches;
    private readonly IPlayerRepo _players;
    private readonly IUserRepo _users;
    private readonly INotificationDispatcher _dispatcher;

    public MembershipService(IMembershipRepo membership, IMatchRepo matches, IPlayerRepo players, IUserRepo users,
        INotificationDispatcher dispatcher)
    {
        _membership = membership;
        _matches = matches;
        _players = players;
        _users = users;
        _dispatcher = dispatcher;
    }

    public async Task LeaveTeamAsync(string me, string ownerId, string teamId)
    {
        var row = await _membership.GetJoinedTeamAsync(me, ownerId, teamId) ?? throw ApiException.NotFound("Team");
        if (await _membership.LeaveTeamAsync(me, ownerId, teamId) == 0) throw ApiException.NotFound("Team");

        var who = await _users.GetUserAsync(me);
        var name = string.IsNullOrWhiteSpace(who?.DisplayName) ? "A player" : who!.DisplayName!;
        var teamName = row.Team.Name;
        _dispatcher.Enqueue(sp => sp.GetRequiredService<INotificationService>().SendToUserAsync(ownerId, new OutgoingNotification
        {
            Type = NotificationTypes.TeamLeft,
            Title = "A player left your team",
            Body = $"{name} left {teamName}",
            Data = new Dictionary<string, string> { ["teamId"] = teamId, ["teamName"] = teamName }
        }));
    }

    public async Task<List<JoinedTeamDto>> ListPublicTeamsOfPlayerAsync(string me, string playerId)
    {
        var player = await _players.GetAsync(me, playerId);
        if (player == null || player.DeletedAt != null) throw ApiException.NotFound("Player");
        if (player.LinkedUserId == null || player.LinkedUserId == me) return new List<JoinedTeamDto>();
        if (await _players.GetDiscoverableUserAsync(player.LinkedUserId) == null) return new List<JoinedTeamDto>();

        return (await _membership.ListPublicTeamsOfUserAsync(player.LinkedUserId, me))
            .Select(r => ToDto(r.Team, r.OwnerName, r.PlayerCount)).ToList();
    }

    public async Task<List<JoinedTeamDto>> ListJoinedTeamsAsync(string me) =>
        (await _membership.ListJoinedTeamsAsync(me)).Select(r => ToDto(r.Team, r.OwnerName, r.PlayerCount)).ToList();

    public async Task<JoinedTeamDetailDto> GetJoinedTeamAsync(string me, string ownerId, string teamId)
    {
        // Only members can look at a squad.
        var row = await _membership.GetJoinedTeamAsync(me, ownerId, teamId) ?? throw ApiException.NotFound("Team");
        var squad = await _membership.GetSquadAsync(ownerId, teamId);
        var d = ToDto(row.Team, row.OwnerName, squad.Count);
        return new JoinedTeamDetailDto
        {
            OwnerId = d.OwnerId, OwnerName = d.OwnerName, TeamId = d.TeamId, Name = d.Name, ShortName = d.ShortName,
            ColorIndex = d.ColorIndex, PlayerCount = d.PlayerCount,
            Squad = squad.Select(p => new SquadMemberDto
            {
                PlayerId = p.Id,
                Name = p.Name,
                Role = p.Role,
                BattingStyle = p.BattingStyle,
                BowlingStyle = p.BowlingStyle,
                PhotoUrl = p.PhotoUrl,
                IsRegistered = p.LinkedUserId != null,
                IsMe = p.LinkedUserId == me,
                IsCaptain = row.Team.CaptainPlayerId == p.Id,
                IsViceCaptain = row.Team.ViceCaptainPlayerId == p.Id,
                IsOwner = p.IsSelf
            }).ToList()
        };
    }

    public async Task<List<MatchSummaryDto>> ListMatchesAsync(string me, int? limit)
    {
        var rows = await _membership.ListParticipantMatchesAsync(me, Math.Clamp(limit ?? 100, 1, 200));
        var innings = await _matches.GetInningsForMatchesAsync(rows);
        return rows.Select(m => MatchMapper.ToSummary(m, innings.Where(i => i.UserId == m.UserId && i.MatchId == m.Id))).ToList();
    }

    public async Task<MatchDto> GetMatchAsync(string me, string ownerId, string matchId)
    {
        if (!await _membership.IsParticipantAsync(me, ownerId, matchId)) throw ApiException.NotFound("Match");
        var agg = await _matches.GetAggregateAsync(ownerId, matchId);
        if (agg == null || agg.Match.DeletedAt != null) throw ApiException.NotFound("Match");
        return MatchMapper.ToDto(agg);
    }

    private static JoinedTeamDto ToDto(Cricket_Repository.Entity.Team t, string? ownerName, int count) => new()
    {
        OwnerId = t.UserId,
        OwnerName = ownerName,
        TeamId = t.Id,
        Name = t.Name,
        ShortName = t.ShortName,
        ColorIndex = t.ColorIndex,
        PlayerCount = count
    };
}
