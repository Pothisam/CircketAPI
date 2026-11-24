using Cricket_Models.CommonModels;
using Cricket_Models.InvitationModels;
using Cricket_Models.StatsModels;
using Cricket_Models.TeamModels;
using Cricket_Services.InvitationServices;
using Cricket_Services.StatsServices;
using Cricket_Services.TeamServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

[Route("api/v1/teams")]
public class TeamsController : BaseApiController
{
    private readonly ITeamService _teams;
    private readonly IStatsService _stats;
    private readonly IInvitationService _invitations;
    private readonly ITeamManagementService _management;

    public TeamsController(ITeamService teams, IStatsService stats, IInvitationService invitations, ITeamManagementService management)
    {
        _teams = teams;
        _stats = stats;
        _invitations = invitations;
        _management = management;
    }

    /// <summary>Users this team was handed over to (owner only).</summary>
    [HttpGet("{id}/managers")]
    public async Task<IActionResult> Managers(string id) => Ok(new { items = await _management.ListManagersAsync(Uid, id) });

    /// <summary>
    /// Hand the team over to a registered user: it appears on their device and they can run it (squad, order,
    /// captain) but not delete it. The creator stays the owner. Max. 5 managers.
    /// </summary>
    [HttpPost("{id}/managers")]
    public async Task<IActionResult> AddManager(string id, AddManagerRequest request) =>
        Ok(new { items = await _management.AddManagerAsync(Uid, id, request.UserId) });

    /// <summary>Take the team back from a manager (owner only).</summary>
    [HttpDelete("{id}/managers/{userId}")]
    public async Task<IActionResult> RemoveManager(string id, string userId) =>
        Ok(new { items = await _management.RemoveManagerAsync(Uid, id, userId) });

    /// <summary>
    /// Find teams of OTHER users by name (min. 2 characters), to challenge them to a match.
    /// Only PUBLIC teams are listed (the owner switches a team between public and private).
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q, int? limit) =>
        Ok(new { items = await _invitations.SearchTeamsAsync(Uid, q, limit) });

    [HttpGet]
    public async Task<ActionResult<ListResponse<TeamDto>>> List(int? limit, string? cursor, string? updatedSince, bool includeDeleted = false) =>
        Ok(await _teams.ListAsync(Uid, limit, cursor, ParseSince(updatedSince), includeDeleted));

    [HttpGet("{id}")]
    public async Task<ActionResult<TeamDto>> Get(string id) => Ok(await _teams.GetAsync(Uid, id));

    /// <summary>Upsert by id (idempotent). Replaces the squad with playerIds.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<TeamDto>> Put(string id, TeamDto dto)
    {
        ReconcileId(dto.Id, id, v => dto.Id = v);
        return Ok(await _teams.UpsertAsync(Uid, id, dto));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _teams.DeleteAsync(Uid, id);
        return NoContent();
    }

    [HttpGet("{id}/stats")]
    public async Task<ActionResult<TeamStatsDto>> Stats(string id) => Ok(await _stats.GetTeamStatsAsync(Uid, id));
}
