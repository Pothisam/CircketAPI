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

    public TeamsController(ITeamService teams, IStatsService stats, IInvitationService invitations)
    {
        _teams = teams;
        _stats = stats;
        _invitations = invitations;
    }

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
