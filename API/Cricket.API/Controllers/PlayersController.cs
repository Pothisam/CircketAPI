using Cricket_Models.CommonModels;
using Cricket_Models.PlayerModels;
using Cricket_Models.StatsModels;
using Cricket_Services.PlayerServices;
using Cricket_Services.StatsServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

[Route("api/v1/players")]
public class PlayersController : BaseApiController
{
    private readonly IPlayerService _players;
    private readonly IStatsService _stats;

    public PlayersController(IPlayerService players, IStatsService stats)
    {
        _players = players;
        _stats = stats;
    }

    [HttpGet]
    public async Task<ActionResult<ListResponse<PlayerDto>>> List(int? limit, string? cursor, string? updatedSince, bool includeDeleted = false) =>
        Ok(await _players.ListAsync(Uid, limit, cursor, ParseSince(updatedSince), includeDeleted));

    /// <summary>
    /// Find players by name or e-mail. Returns the caller's own players (source "mine") and registered
    /// users who are not in the list yet (source "user"; found by name or by their exact e-mail).
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q, int? limit) =>
        Ok(new { items = await _players.SearchAsync(Uid, q, limit) });

    /// <summary>Adds a registered user to the caller's player list (linked to that user).</summary>
    [HttpPost("from-user")]
    public async Task<ActionResult<PlayerDto>> AddFromUser(AddPlayerFromUserRequest request)
    {
        var (player, created) = await _players.AddFromUserAsync(Uid, request.UserId);
        return created ? StatusCode(StatusCodes.Status201Created, player) : Ok(player);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PlayerDto>> Get(string id) => Ok(await _players.GetAsync(Uid, id));

    [HttpPut("{id}")]
    public async Task<ActionResult<PlayerDto>> Put(string id, PlayerDto dto)
    {
        ReconcileId(dto.Id, id, v => dto.Id = v);
        return Ok(await _players.UpsertAsync(Uid, id, dto));
    }

    /// <summary>Soft delete; also removes the player from every squad. Existing matches keep their name snapshot.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _players.DeleteAsync(Uid, id);
        return NoContent();
    }

    [HttpGet("{id}/stats")]
    public async Task<ActionResult<PlayerStatsDto>> Stats(string id) => Ok(await _stats.GetPlayerStatsAsync(Uid, id));
}
