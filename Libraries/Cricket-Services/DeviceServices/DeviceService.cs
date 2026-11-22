using Cricket_Models.AuthModels;
using Cricket_Models.CommonModels;
using Cricket_Models.DeviceModels;
using Cricket_Models.Enums;
using Cricket_Repository.Entity;
using Cricket_Repository.UserRepository;

namespace Cricket_Services.DeviceServices;

public record DeviceUpsertResult(UserDevice Device, bool SeenRecently);

public interface IDeviceService
{
    /// <summary>Registers/refreshes an FCM token for the user. A token already known (even for another user) is re-assigned.</summary>
    Task<DeviceUpsertResult> UpsertAsync(string uid, SessionRequest request, string? previousToken);
    Task<DeviceDto> RegisterTokenAsync(string uid, RegisterTokenRequest request);
    Task<List<DeviceDto>> ListAsync(string uid, string? currentDeviceId);
    Task<DeviceDto> UpdateAsync(string uid, long id, UpdateDeviceRequest request);
    Task DeleteAsync(string uid, long id);
    Task DeactivateTokenAsync(string uid, string fcmToken);
}

public class DeviceService : IDeviceService
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(10);
    private readonly IUserRepo _users;

    public DeviceService(IUserRepo users)
    {
        _users = users;
    }

    public async Task<DeviceUpsertResult> UpsertAsync(string uid, SessionRequest r, string? previousToken)
    {
        new ValidationBuilder()
            .OneOf(r.Platform, CricketEnums.Platforms, "platform")
            .Check(!string.IsNullOrWhiteSpace(r.FcmToken), "fcmToken", "required")
            .ThrowIfAny();

        var now = Clock.UtcNow();
        var deviceId = string.IsNullOrWhiteSpace(r.DeviceId) ? null : r.DeviceId;

        // Same install signed in moments ago? (used to suppress duplicate login alerts)
        var seenRecently = false;
        if (deviceId != null)
        {
            var active = await _users.ListActiveDevicesAsync(uid);
            seenRecently = active.Any(d => d.DeviceId == deviceId && now - d.LastSeenAt < RecentWindow);
        }

        var device = await _users.GetDeviceByTokenAsync(r.FcmToken);
        var isNew = device == null;
        device ??= new UserDevice { FcmToken = r.FcmToken, CreatedAt = now };
        device.UserId = uid;                              // token may move between users (shared phone)
        device.DeviceId = deviceId;
        device.Platform = r.Platform;
        device.DeviceModel = r.DeviceModel;
        device.OsVersion = r.OsVersion;
        device.AppVersion = r.AppVersion;
        device.Locale = r.Locale;
        device.NotificationsEnabled = r.NotificationsEnabled;
        device.IsActive = true;
        device.LastSeenAt = now;
        device.UpdatedAt = now;
        if (isNew) await _users.AddDeviceAsync(device); else await _users.SaveChangesAsync();

        // A rotated token replaces the old one for the same install.
        if (!string.IsNullOrWhiteSpace(previousToken) && previousToken != r.FcmToken)
            await _users.DeactivateTokensAsync(uid, new[] { previousToken });
        if (deviceId != null)
            await _users.DeactivateOtherTokensForDeviceAsync(uid, deviceId, r.FcmToken);

        return new DeviceUpsertResult(device, seenRecently);
    }

    public async Task<DeviceDto> RegisterTokenAsync(string uid, RegisterTokenRequest request)
    {
        var result = await UpsertAsync(uid, request, request.PreviousToken);
        return ToDto(result.Device, includeToken: true, isCurrent: null);
    }

    public async Task<List<DeviceDto>> ListAsync(string uid, string? currentDeviceId)
    {
        var devices = await _users.ListActiveDevicesAsync(uid);
        return devices.Select(d => ToDto(d, false, currentDeviceId == null ? null : d.DeviceId == currentDeviceId)).ToList();
    }

    public async Task<DeviceDto> UpdateAsync(string uid, long id, UpdateDeviceRequest request)
    {
        var d = await _users.GetDeviceAsync(uid, id) ?? throw ApiException.NotFound("Device");
        if (request.NotificationsEnabled.HasValue) d.NotificationsEnabled = request.NotificationsEnabled.Value;
        d.UpdatedAt = Clock.UtcNow();
        await _users.SaveChangesAsync();
        return ToDto(d, true, null);
    }

    public async Task DeleteAsync(string uid, long id)
    {
        var d = await _users.GetDeviceAsync(uid, id) ?? throw ApiException.NotFound("Device");
        await _users.DeleteDeviceAsync(d);
    }

    public Task DeactivateTokenAsync(string uid, string fcmToken) =>
        _users.DeactivateTokensAsync(uid, new[] { fcmToken });

    private static DeviceDto ToDto(UserDevice d, bool includeToken, bool? isCurrent) => new()
    {
        Id = d.Id,
        FcmToken = includeToken ? d.FcmToken : null,
        Platform = d.Platform,
        DeviceModel = d.DeviceModel,
        AppVersion = d.AppVersion,
        NotificationsEnabled = d.NotificationsEnabled,
        IsActive = d.IsActive,
        IsCurrent = isCurrent,
        LastSeenAt = d.LastSeenAt
    };
}
