using Cricket_Models.DeviceModels;
using Cricket_Services.DeviceServices;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

[Route("api/v1/devices")]
public class DevicesController : BaseApiController
{
    private readonly IDeviceService _devices;

    public DevicesController(IDeviceService devices)
    {
        _devices = devices;
    }

    /// <summary>Register or refresh an FCM token (call on app start and on onTokenRefresh).</summary>
    [HttpPut("token")]
    public async Task<ActionResult<DeviceDto>> RegisterToken(RegisterTokenRequest request) =>
        Ok(await _devices.RegisterTokenAsync(Uid, request));

    /// <summary>Active devices. Send the app's install id in the X-Device-Id header to get isCurrent.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromHeader(Name = "X-Device-Id")] string? currentDeviceId) =>
        Ok(new { items = await _devices.ListAsync(Uid, currentDeviceId) });

    [HttpPatch("{id:long}")]
    public async Task<ActionResult<DeviceDto>> Update(long id, UpdateDeviceRequest request) =>
        Ok(await _devices.UpdateAsync(Uid, id, request));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await _devices.DeleteAsync(Uid, id);
        return NoContent();
    }
}
