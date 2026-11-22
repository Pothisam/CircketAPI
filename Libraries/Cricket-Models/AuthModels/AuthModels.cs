using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.AuthModels;

public class SessionRequest
{
    [Required, StringLength(450)] public string FcmToken { get; set; } = "";
    [StringLength(128)] public string? DeviceId { get; set; }
    [Required] public string Platform { get; set; } = "";
    [StringLength(120)] public string? DeviceModel { get; set; }
    [StringLength(40)] public string? OsVersion { get; set; }
    [StringLength(20)] public string? AppVersion { get; set; }
    [StringLength(10)] public string? Locale { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
}

public class LogoutRequest
{
    [Required] public string FcmToken { get; set; } = "";
}

public class UpdateMeRequest
{
    [StringLength(120)] public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
}

public class UserDto
{
    public string Id { get; set; } = "";
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
    public string AuthProvider { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class SessionDeviceDto
{
    public long Id { get; set; }
    public string Platform { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
}

public class SessionResponse
{
    public UserDto User { get; set; } = new();
    public bool IsNewUser { get; set; }
    public SessionDeviceDto Device { get; set; } = new();
    public Cricket_Models.NotificationModels.PreferencesDto Preferences { get; set; } = new();
}
