using Cricket_Models.MatchModels;
using Cricket_Services.MatchServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Cricket_Models.ConfigurationModels;
using System.Text.Json;

namespace Cricket.API.Controllers;

[Route("api/v1/matches")]
public class MatchesController : BaseApiController
{
    private readonly IMatchService _matches;
    private readonly AppConfig _appConfig;

    public MatchesController(IMatchService matches, IOptions<AppConfig> appConfig)
    {
        _matches = matches;
        _appConfig = appConfig.Value;
    }

    // ------------------------------------------------------------------ CRUD

    /// <summary>Summaries by default (Matches tab); add full=true for complete match objects.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? status, string? updatedSince, int? limit, string? cursor, bool full = false)
    {
        var since = ParseSince(updatedSince);
        return full
            ? Ok(await _matches.ListFullAsync(Uid, status, since, limit, cursor))
            : Ok(await _matches.ListSummariesAsync(Uid, status, since, limit, cursor));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MatchDto>> Get(string id) => Ok(await _matches.GetAsync(Uid, id));

    /// <summary>Upsert the entire match (initial sync, completing a match). Array order defines inningsNo and ball seq.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<MatchDto>> Put(string id, MatchDto dto)
    {
        ReconcileId(dto.Id, id, v => dto.Id = v);
        return Ok(await _matches.UpsertAsync(Uid, id, dto));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _matches.DeleteAsync(Uid, id);
        return NoContent();
    }

    // ---------------------------------------------------------- live scoring

    /// <summary>Start an innings. Innings 1 sets the match live and sends the match-start notification.</summary>
    [HttpPost("{id}/innings")]
    public async Task<ActionResult<InningsDto>> StartInnings(string id, StartInningsRequest request)
    {
        var (innings, created) = await _matches.StartInningsAsync(Uid, id, request);
        return created ? StatusCode(StatusCodes.Status201Created, innings) : Ok(innings);
    }

    /// <summary>Update crease state or complete the innings. Absent fields are unchanged; an explicit null clears.</summary>
    [HttpPatch("{id}/innings/{inningsNo:int}")]
    public async Task<ActionResult<InningsDto>> PatchInnings(string id, int inningsNo, [FromBody] JsonElement body) =>
        Ok(await _matches.PatchInningsAsync(Uid, id, inningsNo, body));

    /// <summary>Record one ball. 201 when stored, 200 for an identical retry, 409 for a different ball / sequence gap.</summary>
    [HttpPost("{id}/innings/{inningsNo:int}/balls")]
    public async Task<ActionResult<AddBallResponse>> AddBall(string id, int inningsNo, BallDto ball)
    {
        var (result, created) = await _matches.AddBallAsync(Uid, id, inningsNo, ball);
        return created ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
    }

    /// <summary>Undo the last ball (no notification).</summary>
    [HttpDelete("{id}/innings/{inningsNo:int}/balls/last")]
    public async Task<ActionResult<UndoBallResponse>> UndoLastBall(string id, int inningsNo) =>
        Ok(await _matches.UndoLastBallAsync(Uid, id, inningsNo));

    [HttpPost("{id}/complete")]
    public async Task<ActionResult<MatchDto>> Complete(string id, CompleteMatchRequest request) =>
        Ok(await _matches.CompleteAsync(Uid, id, request));

    // -------------------------------------------------- sharing and following

    [HttpPost("{id}/share")]
    public async Task<ActionResult<ShareResponse>> Share(string id, ShareRequest? request) =>
        Ok(await _matches.ShareAsync(Uid, id, request ?? new ShareRequest(), BaseUrl()));

    [HttpDelete("{id}/share")]
    public async Task<IActionResult> Unshare(string id)
    {
        await _matches.UnshareAsync(Uid, id);
        return NoContent();
    }

    [HttpPost("follow")]
    public async Task<ActionResult<MatchSummaryDto>> Follow(FollowRequest request) =>
        Ok(await _matches.FollowAsync(Uid, request.ShareCode));

    [HttpDelete("{ownerId}/{id}/follow")]
    public async Task<IActionResult> Unfollow(string ownerId, string id)
    {
        await _matches.UnfollowAsync(Uid, ownerId, id);
        return NoContent();
    }

    private string BaseUrl() =>
        !string.IsNullOrWhiteSpace(_appConfig.PublicBaseUrl) ? _appConfig.PublicBaseUrl : $"{Request.Scheme}://{Request.Host}";
}
