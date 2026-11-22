using Cricket_Models.MatchModels;
using Cricket_Services.MatchServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

/// <summary>Read-only views for shared matches (phase 2 of the spec).</summary>
public class SharingController : BaseApiController
{
    private readonly IMatchService _matches;

    public SharingController(IMatchService matches)
    {
        _matches = matches;
    }

    /// <summary>No auth: anyone with the share code can read the match.</summary>
    [HttpGet("public/matches/{shareCode}")]
    [AllowAnonymous]
    public async Task<ActionResult<MatchDto>> GetPublic(string shareCode) => Ok(await _matches.GetPublicAsync(shareCode));

    /// <summary>Matches the caller follows.</summary>
    [HttpGet("following")]
    public async Task<IActionResult> Following() => Ok(new { items = await _matches.ListFollowingAsync(Uid) });
}
