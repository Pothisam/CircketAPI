using Cricket_Models.NotificationModels;
using Cricket_Services.NotificationServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cricket.API.Controllers;

[Route("api/v1/notifications")]
public class NotificationsController : BaseApiController
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<InboxResponse>> Inbox(int? limit, string? cursor, bool unreadOnly = false) =>
        Ok(await _notifications.GetInboxAsync(Uid, unreadOnly, limit, cursor));

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id)
    {
        await _notifications.MarkReadAsync(Uid, id);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _notifications.MarkAllReadAsync(Uid);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await _notifications.DeleteAsync(Uid, id);
        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<PreferencesDto>> GetPreferences() => Ok(await _notifications.GetPreferencesAsync(Uid));

    [HttpPut("preferences")]
    public async Task<ActionResult<PreferencesDto>> PutPreferences(PreferencesDto dto) =>
        Ok(await _notifications.UpdatePreferencesAsync(Uid, dto));

    /// <summary>Sends a test push to the caller's own active devices (QA).</summary>
    [HttpPost("test")]
    public async Task<ActionResult<SendResultDto>> Test(TestNotificationRequest request) =>
        Ok(await _notifications.SendTestAsync(Uid, request));
}

/// <summary>Admin-only (Firebase custom claim admin=true).</summary>
[Route("api/v1/admin")]
[Authorize(Policy = "Admin")]
public class AdminController : BaseApiController
{
    private readonly INotificationService _notifications;

    public AdminController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpPost("notifications/broadcast")]
    public async Task<IActionResult> Broadcast(BroadcastRequest request)
    {
        var messageId = await _notifications.BroadcastAsync(request);
        return Ok(new { messageId });
    }
}
