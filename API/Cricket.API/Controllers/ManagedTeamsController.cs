using Cricket_Models.TeamModels;
using Cricket_Services.TeamServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

/// <summary>
/// Teams that were handed over to me by their creator. I can run them (name, colour, squad, order, captain,
/// vice-captain), but I cannot delete them, change their visibility, appoint managers or remove the creator.
/// </summary>
[Route("api/v1/me/managed-teams")]
public class ManagedTeamsController : BaseApiController
{
    private readonly ITeamManagementService _management;

    public ManagedTeamsController(ITeamManagementService management)
    {
        _management = management;
    }

    /// <summary>Teams handed over to me.</summary>
    [HttpGet]
    public async Task<IActionResult> List() => Ok(new { items = await _management.ListManagedAsync(Uid) });

    /// <summary>One of them with its squad and the owner's other players (to add).</summary>
    [HttpGet("{ownerId}/{teamId}")]
    public async Task<ActionResult<ManagedTeamDto>> Get(string ownerId, string teamId) =>
        Ok(await _management.GetManagedAsync(Uid, ownerId, teamId));

    /// <summary>Change the team: name, colour, the squad in batting order, captain, vice-captain.</summary>
    [HttpPut("{ownerId}/{teamId}")]
    public async Task<ActionResult<ManagedTeamDto>> Put(string ownerId, string teamId, ManagedTeamUpdate update) =>
        Ok(await _management.UpdateManagedAsync(Uid, ownerId, teamId, update));

    /// <summary>Add somebody to the squad: a registered user (userId) or a new player (name).</summary>
    [HttpPost("{ownerId}/{teamId}/players")]
    public async Task<ActionResult<ManagedTeamDto>> AddPlayer(string ownerId, string teamId, AddManagedPlayerRequest request) =>
        Ok(await _management.AddManagedPlayerAsync(Uid, ownerId, teamId, request));

    /// <summary>Stop managing the team (give it back).</summary>
    [HttpDelete("{ownerId}/{teamId}")]
    public async Task<IActionResult> Leave(string ownerId, string teamId)
    {
        await _management.LeaveAsync(Uid, ownerId, teamId);
        return NoContent();
    }
}
