using Cricket_Models.InvitationModels;
using Cricket_Models.MembershipModels;
using Cricket_Services.InvitationServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

/// <summary>
/// Matches between teams that belong to different users: invite, accept / reject, start.
/// An accepted invitation is the scheduled match; both owners can see it.
/// </summary>
[Route("api/v1/match-invitations")]
public class MatchInvitationsController : BaseApiController
{
    private readonly IInvitationService _invitations;

    public MatchInvitationsController(IInvitationService invitations)
    {
        _invitations = invitations;
    }

    /// <summary>Invitations I sent and received. box = incoming | outgoing | all (default), optional status filter.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? box, string? status) =>
        Ok(new { items = await _invitations.ListAsync(Uid, box, status) });

    /// <summary>Challenge a team of another user to a match (the owner of that team is notified).</summary>
    [HttpPost]
    public async Task<ActionResult<InvitationDto>> Create(CreateInvitationRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _invitations.CreateAsync(Uid, request));

    /// <summary>The owner of the invited team accepts: the match is now scheduled for both teams.</summary>
    [HttpPost("{id}/accept")]
    public async Task<ActionResult<InvitationDto>> Accept(string id) => Ok(await _invitations.RespondAsync(Uid, id, "accept"));

    [HttpPost("{id}/reject")]
    public async Task<ActionResult<InvitationDto>> Reject(string id) => Ok(await _invitations.RespondAsync(Uid, id, "reject"));

    /// <summary>The sender cancels a pending invitation; either side can cancel an accepted match before it starts.</summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<InvitationDto>> Cancel(string id) => Ok(await _invitations.RespondAsync(Uid, id, "cancel"));

    /// <summary>The opponent team and its players (ids included), for the scorer to pick the playing XI.</summary>
    [HttpGet("{id}/opponent-squad")]
    public async Task<ActionResult<JoinedTeamDetailDto>> OpponentSquad(string id) => Ok(await _invitations.GetOpponentSquadAsync(Uid, id));
}
