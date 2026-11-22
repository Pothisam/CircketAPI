using Cricket_Models.PlayerModels;
using Cricket_Services.ProfileServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

/// <summary>The signed-in user's cricket profile (identity + playing details).</summary>
[Route("api/v1/me/profile")]
public class ProfileController : BaseApiController
{
    private readonly IProfileService _profiles;

    public ProfileController(IProfileService profiles)
    {
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get() => Ok(await _profiles.GetAsync(Uid));

    /// <summary>Replaces the profile. Also updates the user's own player, so it shows up (and syncs) in the player list.</summary>
    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Put(UpdateProfileRequest request) =>
        Ok(await _profiles.UpdateAsync(Uid, request));
}
