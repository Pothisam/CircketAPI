using Cricket_Models.AuthModels;

namespace Cricket_Models.DeviceModels;

public class RegisterTokenRequest : SessionRequest
{
    public string? PreviousToken { get; set; }
}

public class UpdateDeviceRequest
{
    public bool? NotificationsEnabled { get; set; }
}

public class DeviceDto
{
    public long Id { get; set; }
    /// <summary>Only returned by PUT /devices/token and PATCH (never in the device list).</summary>
    public string? FcmToken { get; set; }
    public string Platform { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? AppVersion { get; set; }
    public bool NotificationsEnabled { get; set; }
    public bool IsActive { get; set; }
    public bool? IsCurrent { get; set; }
    public DateTime LastSeenAt { get; set; }
}
