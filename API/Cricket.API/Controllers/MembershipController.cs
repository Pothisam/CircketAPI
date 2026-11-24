using Cricket_Models.MatchModels;
using Cricket_Models.MembershipModels;
using Cricket_Services.MembershipServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

/// <summary>
/// What I am part of although someone else created it: the teams I was added to and the matches
/// I played in. Everything here is read only.
/// </summary>
[Route("api/v1/me")]
public class MembershipController : BaseApiController
{
    private readonly IMembershipService _membership;

    public MembershipController(IMembershipService membership)
    {
        _membership = membership;
    }

    /// <summary>Teams of other scorers that contain me.</summary>
    [HttpGet("joined-teams")]
    public async Task<IActionResult> JoinedTeams() => Ok(new { items = await _membership.ListJoinedTeamsAsync(Uid) });

    /// <summary>A team I belong to, with its squad (public player details only).</summary>
    [HttpGet("joined-teams/{ownerId}/{teamId}")]
    public async Task<ActionResult<JoinedTeamDetailDto>> JoinedTeam(string ownerId, string teamId) =>
        Ok(await _membership.GetJoinedTeamAsync(Uid, ownerId, teamId));

    /// <summary>Leave a team somebody else added me to. I disappear from its squad and the creator is told.</summary>
    [HttpDelete("joined-teams/{ownerId}/{teamId}")]
    public async Task<IActionResult> LeaveTeam(string ownerId, string teamId)
    {
        await _membership.LeaveTeamAsync(Uid, ownerId, teamId);
        return NoContent();
    }

    /// <summary>My match history in other scorers' matches (I played, or my team played).</summary>
    [HttpGet("matches")]
    public async Task<IActionResult> Matches(int? limit) => Ok(new { items = await _membership.ListMatchesAsync(Uid, limit) });

    /// <summary>Full scorecard of one of those matches.</summary>
    [HttpGet("matches/{ownerId}/{matchId}")]
    public async Task<ActionResult<MatchDto>> Match(string ownerId, string matchId) =>
        Ok(await _membership.GetMatchAsync(Uid, ownerId, matchId));
}
