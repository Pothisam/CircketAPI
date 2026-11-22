using Cricket_Models.SyncModels;
using Cricket_Services.SyncServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

[Route("api/v1/sync")]
public class SyncController : BaseApiController
{
    private readonly ISyncService _sync;

    public SyncController(ISyncService sync)
    {
        _sync = sync;
    }

    /// <summary>Send everything created/changed offline in one call. Items are accepted or rejected individually.</summary>
    [HttpPost("push")]
    public async Task<ActionResult<SyncPushResponse>> Push(SyncPushRequest request) => Ok(await _sync.PushAsync(Uid, request));

    /// <summary>Everything changed since a timestamp (omit "since" for a full download). Store serverTime for the next call.</summary>
    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullResponse>> Pull(string? since) => Ok(await _sync.PullAsync(Uid, ParseSince(since, "since")));
}
