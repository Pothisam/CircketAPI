using Cricket_Models.CommonModels;
using Cricket_Models.MiscModels;
using Cricket_Services.MiscServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

public class HealthController : BaseApiController
{
    private readonly IMiscService _misc;

    public HealthController(IMiscService misc)
    {
        _misc = misc;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health() => Ok(new { status = "ok", time = Clock.UtcNow() });

    /// <summary>Minimum supported app version and feature flags (anonymous so it can gate the login screen).</summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public ActionResult<ConfigResponse> Config() => Ok(_misc.GetConfig());

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback(FeedbackRequest request)
    {
        await _misc.AddFeedbackAsync(Uid, request);
        return NoContent();
    }
}
