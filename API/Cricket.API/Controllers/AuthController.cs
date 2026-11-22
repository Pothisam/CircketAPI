using Cricket.API.Infrastructure;
using Cricket_Models.AuthModels;
using Cricket_Services.AuthServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Cricket.API.Controllers;

public class AuthController : BaseApiController
{
    private readonly IAuthService _auth;
    private readonly IMemoryCache _cache;

    public AuthController(IAuthService auth, IMemoryCache cache)
    {
        _auth = auth;
        _cache = cache;
    }

    /// <summary>Call after every successful Firebase sign-in: upserts the user, registers the FCM token, triggers the login notification.</summary>
    [HttpPost("auth/session")]
    public async Task<ActionResult<SessionResponse>> Session(SessionRequest request)
    {
        var result = await _auth.SessionAsync(Me, request);
        _cache.Remove(UserSessionMiddleware.CacheKey(Uid));
        return Ok(result);
    }

    /// <summary>Call before signing out of Firebase; stops pushes to this device.</summary>
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(LogoutRequest request)
    {
        await _auth.LogoutAsync(Uid, request.FcmToken);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe() => Ok(await _auth.GetMeAsync(Uid));

    [HttpPatch("me")]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateMeRequest request) => Ok(await _auth.UpdateMeAsync(Uid, request));

    /// <summary>Deletes the account and all its data, and the Firebase user (required by Play Store / App Store).</summary>
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe()
    {
        await _auth.DeleteMeAsync(Uid);
        _cache.Remove(UserSessionMiddleware.CacheKey(Uid));
        return NoContent();
    }
}
