using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.InvitationModels;
using Cricket_Models.MatchModels;
using Cricket_Models.MembershipModels;
using Cricket_Repository.Entity;
using Cricket_Repository.InvitationRepository;
using Cricket_Repository.MembershipRepository;
using Cricket_Repository.TeamRepository;
using Cricket_Services.NotificationServices;
using Microsoft.Extensions.DependencyInjection;

namespace Cricket_Services.InvitationServices;

public interface IInvitationService
{
    Task<List<TeamSearchItem>> SearchTeamsAsync(string me, string? q, int? limit);
    Task<InvitationDto> CreateAsync(string me, CreateInvitationRequest request);
    Task<List<InvitationDto>> ListAsync(string me, string? box, string? status);
    /// <summary>action: accept | reject | cancel</summary>
    Task<InvitationDto> RespondAsync(string me, string id, string action);
    /// <summary>The opponent team with its squad, for the scorer to pick the playing XI.</summary>
    Task<JoinedTeamDetailDto> GetOpponentSquadAsync(string me, string id);

    /// <summary>
    /// Used when the scorer saves a match that carries an invitation id: checks that the invitation is theirs,
    /// accepted and for these two teams.
    /// </summary>
    Task<MatchInvitation> ValidateForMatchAsync(string uid, string invitationId, string matchId, string teamAId, string teamBId);
    /// <summary>The match now exists: the fixture becomes "started".</summary>
    Task MarkStartedAsync(MatchInvitation invitation, string matchId);
}

public class InvitationService : IInvitationService
{
    private readonly IInvitationRepo _invitations;
    private readonly ITeamRepo _teams;
    private readonly IMembershipRepo _membership;
    private readonly INotificationDispatcher _dispatcher;

    public InvitationService(IInvitationRepo invitations, ITeamRepo teams, IMembershipRepo membership,
        INotificationDispatcher dispatcher)
    {
        _invitations = invitations;
        _teams = teams;
        _membership = membership;
        _dispatcher = dispatcher;
    }

    // -------------------------------------------------------------- search

    public async Task<List<TeamSearchItem>> SearchTeamsAsync(string me, string? q, int? limit)
    {
        var term = q?.Trim() ?? "";
        new ValidationBuilder().Check(term.Length is >= 2 and <= 80, "q", "must be 2 to 80 characters").ThrowIfAny();
        var rows = await _invitations.SearchTeamsAsync(me, term, Math.Clamp(limit ?? 20, 1, 50));
        return rows.Select(r => new TeamSearchItem
        {
            OwnerId = r.Team.UserId,
            OwnerName = r.OwnerName,
            TeamId = r.Team.Id,
            Name = r.Team.Name,
            ShortName = r.Team.ShortName,
            ColorIndex = r.Team.ColorIndex,
            PlayerCount = r.PlayerCount
        }).ToList();
    }

    // -------------------------------------------------------------- create

    public async Task<InvitationDto> CreateAsync(string me, CreateInvitationRequest r)
    {
        new ValidationBuilder()
            .Check(r.ToOwnerId != me, "toOwnerId", "you cannot challenge your own team; pick a team of another user")
            .Check(r.Overs is >= 1 and <= 100, "overs", "must be 1 to 100")
            .ThrowIfAny();

        var from = await _teams.GetAsync(me, r.FromTeamId);
        if (from == null || from.DeletedAt != null)
            throw ApiException.Validation("Your team is not on the server yet. Sync and try again.",
                new List<ErrorDetail> { new("fromTeamId", "unknown team") });

        var to = await _teams.GetAsync(r.ToOwnerId, r.ToTeamId);
        if (to == null || to.DeletedAt != null || !await _invitations.OwnerIsActiveAsync(r.ToOwnerId))
            throw ApiException.NotFound("Opponent team");

        if (await _invitations.HasPendingAsync(me, from.Id, to.UserId, to.Id))
            throw ApiException.Conflict("You already sent an invitation between these two teams that is still waiting for an answer");

        var now = Clock.UtcNow();
        var inv = new MatchInvitation
        {
            Id = Guid.NewGuid().ToString("N"),
            FromUserId = me, FromTeamId = from.Id, FromTeamName = from.Name, FromTeamShort = from.ShortName,
            ToUserId = to.UserId, ToTeamId = to.Id, ToTeamName = to.Name, ToTeamShort = to.ShortName,
            Overs = r.Overs,
            ScheduledAt = r.ScheduledAt.HasValue ? ToUtc(r.ScheduledAt.Value) : null,
            Venue = r.Venue?.Trim() ?? "",
            Message = string.IsNullOrWhiteSpace(r.Message) ? null : r.Message.Trim(),
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now
        };
        await _invitations.AddAsync(inv);

        var names = await _invitations.GetUserNamesAsync(new[] { me });
        var scorer = NameOf(names.GetValueOrDefault(me));
        Notify(inv.ToUserId, NotificationTypes.MatchInvite, "Match challenge",
            $"{scorer} challenges {inv.ToTeamName} to a match: {inv.FromTeamName} vs {inv.ToTeamName} ({inv.Overs} overs)", inv.Id);

        return await ToDtoAsync(me, inv);
    }

    // ---------------------------------------------------------------- list

    public async Task<List<InvitationDto>> ListAsync(string me, string? box, string? status)
    {
        var b = (box ?? "all").ToLowerInvariant();
        new ValidationBuilder()
            .Check(b is "all" or "incoming" or "outgoing", "box", "must be incoming, outgoing or all")
            .Check(status == null || Statuses.Contains(status), "status", "must be pending, accepted, rejected, cancelled or started")
            .ThrowIfAny();
        var rows = await _invitations.ListAsync(me, b, status, 100);
        return await ToDtosAsync(me, rows);
    }

    // ------------------------------------------------------------- respond

    public async Task<InvitationDto> RespondAsync(string me, string id, string action)
    {
        var inv = await _invitations.GetAsync(id) ?? throw ApiException.NotFound("Invitation");
        if (inv.FromUserId != me && inv.ToUserId != me) throw ApiException.NotFound("Invitation");   // not yours: do not reveal it

        var now = Clock.UtcNow();
        var names = await _invitations.GetUserNamesAsync(new[] { inv.FromUserId, inv.ToUserId });
        var title = $"{inv.FromTeamName} vs {inv.ToTeamName}";

        switch (action)
        {
            case "accept":
            case "reject":
                if (inv.ToUserId != me) throw ApiException.Forbidden("Only the owner of the invited team can answer");
                if (inv.Status != "pending") throw ApiException.Conflict($"The invitation is already {inv.Status}");
                inv.Status = action == "accept" ? "accepted" : "rejected";
                inv.RespondedAt = now;
                inv.UpdatedAt = now;
                await _invitations.SaveChangesAsync();
                Notify(inv.FromUserId, NotificationTypes.MatchInviteResponse,
                    action == "accept" ? "Match accepted" : "Match declined",
                    action == "accept"
                        ? $"{NameOf(names.GetValueOrDefault(inv.ToUserId))} accepted {title}. Start the match when you are ready."
                        : $"{NameOf(names.GetValueOrDefault(inv.ToUserId))} declined {title}.", inv.Id);
                break;

            case "cancel":
                var allowed = inv.Status == "pending" ? inv.FromUserId == me : inv.Status == "accepted";
                if (!allowed)
                    throw inv.Status is "pending" or "accepted"
                        ? ApiException.Forbidden("Only the team that sent the invitation can cancel it; reject it instead")
                        : ApiException.Conflict($"The invitation is already {inv.Status}");
                inv.Status = "cancelled";
                inv.RespondedAt = now;
                inv.UpdatedAt = now;
                await _invitations.SaveChangesAsync();
                var other = inv.FromUserId == me ? inv.ToUserId : inv.FromUserId;
                Notify(other, NotificationTypes.MatchInviteResponse, "Match cancelled",
                    $"{NameOf(names.GetValueOrDefault(me))} cancelled {title}.", inv.Id);
                break;

            default:
                throw ApiException.Validation("action must be accept, reject or cancel");
        }
        return await ToDtoAsync(me, inv);
    }

    // --------------------------------------------------------------- squad

    public async Task<JoinedTeamDetailDto> GetOpponentSquadAsync(string me, string id)
    {
        var inv = await _invitations.GetAsync(id) ?? throw ApiException.NotFound("Invitation");
        if (inv.FromUserId != me) throw ApiException.NotFound("Invitation");
        if (inv.Status is not ("accepted" or "started")) throw ApiException.Conflict("The opponent has not accepted yet");

        var team = await _teams.GetAsync(inv.ToUserId, inv.ToTeamId);
        if (team == null || team.DeletedAt != null) throw ApiException.NotFound("Opponent team");
        var squad = await _membership.GetSquadAsync(inv.ToUserId, inv.ToTeamId);
        var names = await _invitations.GetUserNamesAsync(new[] { inv.ToUserId });
        return new JoinedTeamDetailDto
        {
            OwnerId = inv.ToUserId,
            OwnerName = names.GetValueOrDefault(inv.ToUserId),
            TeamId = team.Id, Name = team.Name, ShortName = team.ShortName, ColorIndex = team.ColorIndex,
            PlayerCount = squad.Count,
            Squad = squad.Select(p => new SquadMemberDto
            {
                PlayerId = p.Id, Name = p.Name, Role = p.Role, BattingStyle = p.BattingStyle, BowlingStyle = p.BowlingStyle,
                PhotoUrl = p.PhotoUrl, IsRegistered = p.LinkedUserId != null
            }).ToList()
        };
    }

    // -------------------------------------------------- starting the match

    public async Task<MatchInvitation> ValidateForMatchAsync(string uid, string invitationId, string matchId, string teamAId, string teamBId)
    {
        var inv = await _invitations.GetAsync(invitationId);
        if (inv == null || inv.FromUserId != uid)
            throw ApiException.Validation("invitationId: unknown invitation", new List<ErrorDetail> { new("invitationId", "unknown invitation") });
        if (inv.Status is not ("accepted" or "started"))
            throw ApiException.Conflict($"The invitation is {inv.Status}; the opponent has to accept it first");
        if (inv.MatchId != null && inv.MatchId != matchId)
            throw ApiException.Conflict("A match was already started from this invitation");
        if (inv.FromTeamId != teamAId || inv.ToTeamId != teamBId)
            throw ApiException.Validation("The teams do not match the invitation",
                new List<ErrorDetail> { new("teamAId", "must be the inviting team"), new("teamBId", "must be the invited team") });
        return inv;
    }

    public async Task MarkStartedAsync(MatchInvitation invitation, string matchId)
    {
        if (invitation.Status == "started" && invitation.MatchId == matchId) return;
        invitation.Status = "started";
        invitation.MatchId = matchId;
        invitation.UpdatedAt = Clock.UtcNow();
        await _invitations.SaveChangesAsync();
    }

    // ------------------------------------------------------------- helpers

    private static readonly HashSet<string> Statuses = new() { "pending", "accepted", "rejected", "cancelled", "started" };

    private static string NameOf(string? n) => string.IsNullOrWhiteSpace(n) ? "A scorer" : n!;

    private static DateTime ToUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        _ => d.ToUniversalTime()
    };

    private void Notify(string toUser, string type, string title, string body, string invitationId)
    {
        _dispatcher.Enqueue(sp => sp.GetRequiredService<INotificationService>().SendToUserAsync(toUser, new OutgoingNotification
        {
            Type = type,
            Title = title,
            Body = body,
            Data = new Dictionary<string, string> { ["invitationId"] = invitationId }
        }));
    }

    private async Task<InvitationDto> ToDtoAsync(string me, MatchInvitation inv) => (await ToDtosAsync(me, new List<MatchInvitation> { inv }))[0];

    private async Task<List<InvitationDto>> ToDtosAsync(string me, List<MatchInvitation> rows)
    {
        var names = await _invitations.GetUserNamesAsync(rows.SelectMany(r => new[] { r.FromUserId, r.ToUserId }));
        return rows.Select(r => new InvitationDto
        {
            Id = r.Id,
            Direction = r.FromUserId == me ? "outgoing" : "incoming",
            Status = r.Status,
            FromUserId = r.FromUserId,
            FromUserName = names.GetValueOrDefault(r.FromUserId),
            FromTeam = new TeamRefDto { Id = r.FromTeamId, Name = r.FromTeamName, Short = r.FromTeamShort },
            ToUserId = r.ToUserId,
            ToUserName = names.GetValueOrDefault(r.ToUserId),
            ToTeam = new TeamRefDto { Id = r.ToTeamId, Name = r.ToTeamName, Short = r.ToTeamShort },
            Overs = r.Overs,
            ScheduledAt = r.ScheduledAt,
            Venue = r.Venue,
            Message = r.Message,
            MatchId = r.MatchId,
            CreatedAt = r.CreatedAt,
            RespondedAt = r.RespondedAt
        }).ToList();
    }
}
