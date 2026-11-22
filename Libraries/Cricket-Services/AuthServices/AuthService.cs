using Cricket_Models.AuthModels;
using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.NotificationModels;
using Cricket_Repository.Entity;
using Cricket_Repository.UserRepository;
using Cricket_Services.DeviceServices;
using Cricket_Services.NotificationServices;
using Cricket_Services.PushServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cricket_Services.AuthServices;

public interface IAuthService
{
    Task<SessionResponse> SessionAsync(UserContext ctx, SessionRequest request);
    Task LogoutAsync(string uid, string fcmToken);
    Task<UserDto> GetMeAsync(string uid);
    Task<UserDto> UpdateMeAsync(string uid, UpdateMeRequest request);
    Task DeleteMeAsync(string uid);
}

public class AuthService : IAuthService
{
    private readonly IUserRepo _users;
    private readonly IDeviceService _devices;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IPushSender _push;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUserRepo users, IDeviceService devices, INotificationDispatcher dispatcher,
        IPushSender push, ILogger<AuthService> logger)
    {
        _users = users;
        _devices = devices;
        _dispatcher = dispatcher;
        _push = push;
        _logger = logger;
    }

    /// <summary>Login hook: upsert user, register the device, create default prefs, send login notification.</summary>
    public async Task<SessionResponse> SessionAsync(UserContext ctx, SessionRequest request)
    {
        var now = Clock.UtcNow();
        var user = await _users.GetUserAsync(ctx.Uid);
        var isNew = user == null;
        if (user is { IsActive: false }) throw ApiException.Forbidden("This account is disabled");

        user ??= new User { Id = ctx.Uid, CreatedAt = now };

        // Firebase can hold the same email/phone on two uids; the columns are unique, so don't copy
        // a value another row already owns. (Use Firebase account linking to merge identities.)
        if (!string.IsNullOrEmpty(ctx.Email))
        {
            var email = ctx.Email.Trim().ToLowerInvariant();
            if (user.Email != email && !await _users.EmailTakenAsync(email, ctx.Uid)) user.Email = email;
        }
        if (!string.IsNullOrEmpty(ctx.PhoneNumber))
        {
            if (user.PhoneNumber != ctx.PhoneNumber && !await _users.PhoneTakenAsync(ctx.PhoneNumber, ctx.Uid))
                user.PhoneNumber = ctx.PhoneNumber;
        }
        user.DisplayName ??= ctx.DisplayName;      // don't overwrite a name the user edited via PATCH /me
        user.PhotoUrl ??= ctx.PhotoUrl;
        user.AuthProvider = ctx.Provider;
        user.EmailVerified = ctx.EmailVerified;
        user.LastLoginAt = now;
        user.UpdatedAt = now;

        if (isNew) await _users.AddUserAsync(user); else await _users.SaveChangesAsync();

        var upsert = await _devices.UpsertAsync(ctx.Uid, request, null);
        var device = upsert.Device;

        var prefs = await _users.GetPreferencesAsync(ctx.Uid);
        if (prefs == null)
        {
            prefs = new NotificationPreference { UserId = ctx.Uid, UpdatedAt = now };
            await _users.AddPreferencesAsync(prefs);
        }

        QueueLoginNotification(ctx.Uid, isNew, upsert.SeenRecently, device);

        return new SessionResponse
        {
            User = ToDto(user),
            IsNewUser = isNew,
            Device = new SessionDeviceDto { Id = device.Id, Platform = device.Platform, RegisteredAt = device.CreatedAt },
            Preferences = new PreferencesDto
            {
                LoginAlerts = prefs.LoginAlerts, MatchStart = prefs.MatchStart, Boundaries = prefs.Boundaries,
                Wickets = prefs.Wickets, Milestones = prefs.Milestones, InningsEnd = prefs.InningsEnd,
                MatchResult = prefs.MatchResult,
                QuietHoursStart = prefs.QuietHoursStart?.ToString(@"hh\:mm"),
                QuietHoursEnd = prefs.QuietHoursEnd?.ToString(@"hh\:mm")
            }
        };
    }

    public Task LogoutAsync(string uid, string fcmToken) => _devices.DeactivateTokenAsync(uid, fcmToken);

    public async Task<UserDto> GetMeAsync(string uid) =>
        ToDto(await _users.GetUserAsync(uid) ?? throw ApiException.NotFound("User"));

    public async Task<UserDto> UpdateMeAsync(string uid, UpdateMeRequest request)
    {
        var user = await _users.GetUserAsync(uid) ?? throw ApiException.NotFound("User");
        if (request.DisplayName != null) user.DisplayName = request.DisplayName.Trim();
        if (request.PhotoUrl != null) user.PhotoUrl = request.PhotoUrl.Trim();
        user.UpdatedAt = Clock.UtcNow();
        await _users.SaveChangesAsync();
        return ToDto(user);
    }

    public async Task DeleteMeAsync(string uid)
    {
        await _users.DeleteUserAsync(uid);          // cascades to every table
        try
        {
            await _push.DeleteFirebaseUserAsync(uid);
        }
        catch (Exception ex)
        {
            // Data is already gone; a leftover Firebase identity holds no cricket data.
            _logger.LogWarning(ex, "Deleted user data but failed to delete the Firebase user");
        }
    }

    // Login notification rules (spec 7.3)
    private void QueueLoginNotification(string uid, bool isNew, bool seenRecently, UserDevice device)
    {
        var deviceId = device.Id;
        var label = string.IsNullOrWhiteSpace(device.DeviceModel) ? device.Platform : device.DeviceModel;

        if (isNew)
        {
            _dispatcher.Enqueue(sp => sp.GetRequiredService<INotificationService>().SendToUserAsync(uid, new OutgoingNotification
            {
                Type = NotificationTypes.Login,
                Title = "Welcome to Cricket Scorer",
                Body = "Your account is ready. Start scoring your first match!",
                OnlyDeviceId = deviceId
            }));
        }
        else if (!seenRecently)
        {
            _dispatcher.Enqueue(sp => sp.GetRequiredService<INotificationService>().SendToUserAsync(uid, new OutgoingNotification
            {
                Type = NotificationTypes.Login,
                Title = "New sign-in",
                Body = $"{label} signed in to your account",
                Preference = p => p.LoginAlerts,
                ExcludeDeviceId = deviceId       // never alert the device that just signed in
            }));
        }
    }

    private static UserDto ToDto(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        PhoneNumber = u.PhoneNumber,
        DisplayName = u.DisplayName,
        PhotoUrl = u.PhotoUrl,
        AuthProvider = u.AuthProvider,
        CreatedAt = u.CreatedAt,
        LastLoginAt = u.LastLoginAt
    };
}
