using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.UserRepository;

public interface IUserRepo
{
    // Users
    Task<User?> GetUserAsync(string uid);
    /// <summary>null = no such user; otherwise the is_active flag. Used by the per-request session check.</summary>
    Task<bool?> GetActiveFlagAsync(string uid);
    Task<bool> EmailTakenAsync(string email, string excludeUid);
    Task<bool> PhoneTakenAsync(string phone, string excludeUid);
    Task AddUserAsync(User user);
    Task DeleteUserAsync(string uid);

    // Devices
    Task<UserDevice?> GetDeviceByTokenAsync(string fcmToken);
    Task<UserDevice?> GetDeviceAsync(string uid, long id);
    Task<List<UserDevice>> ListActiveDevicesAsync(string uid);
    Task AddDeviceAsync(UserDevice device);
    Task DeactivateTokensAsync(string uid, IEnumerable<string> tokens);
    Task DeactivateOtherTokensForDeviceAsync(string uid, string deviceId, string keepToken);
    Task DeleteDeviceAsync(UserDevice device);
    Task DeactivateTokenGloballyAsync(string fcmToken);

    // Preferences
    Task<NotificationPreference?> GetPreferencesAsync(string uid);
    Task AddPreferencesAsync(NotificationPreference prefs);

    // Feedback
    Task AddFeedbackAsync(Feedback feedback);

    Task SaveChangesAsync();
}

public class UserRepo : IUserRepo
{
    private readonly CricketContext _ctx;

    public UserRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    public Task<User?> GetUserAsync(string uid) => _ctx.Users.FirstOrDefaultAsync(x => x.Id == uid);

    public async Task<bool?> GetActiveFlagAsync(string uid) =>
        await _ctx.Users.AsNoTracking().Where(x => x.Id == uid).Select(x => (bool?)x.IsActive).FirstOrDefaultAsync();

    public Task<bool> EmailTakenAsync(string email, string excludeUid) =>
        _ctx.Users.AnyAsync(x => x.Email == email && x.Id != excludeUid);

    public Task<bool> PhoneTakenAsync(string phone, string excludeUid) =>
        _ctx.Users.AnyAsync(x => x.PhoneNumber == phone && x.Id != excludeUid);

    public async Task AddUserAsync(User user)
    {
        _ctx.Users.Add(user);
        await _ctx.SaveChangesAsync();
    }

    // Everything the user owns is removed by the DB's ON DELETE CASCADE chain.
    // players.linked_user_id is NO ACTION (a second cascade path is not allowed), so other people's
    // players that point at this user are detached first.
    public async Task DeleteUserAsync(string uid)
    {
        await _ctx.Players.Where(x => x.LinkedUserId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LinkedUserId, (string?)null));
        // Invitations received by this user (the ones they sent are removed by the cascade) and matches
        // in which they were the opponent.
        await _ctx.MatchInvitations.Where(x => x.ToUserId == uid).ExecuteDeleteAsync();
        await _ctx.Matches.Where(x => x.OpponentUserId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OpponentUserId, (string?)null));
        await _ctx.Users.Where(x => x.Id == uid).ExecuteDeleteAsync();
    }

    public Task<UserDevice?> GetDeviceByTokenAsync(string fcmToken) =>
        _ctx.UserDevices.FirstOrDefaultAsync(x => x.FcmToken == fcmToken);

    public Task<UserDevice?> GetDeviceAsync(string uid, long id) =>
        _ctx.UserDevices.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);

    public Task<List<UserDevice>> ListActiveDevicesAsync(string uid) =>
        _ctx.UserDevices.AsNoTracking().Where(x => x.UserId == uid && x.IsActive)
            .OrderByDescending(x => x.LastSeenAt).ToListAsync();

    public async Task AddDeviceAsync(UserDevice device)
    {
        _ctx.UserDevices.Add(device);
        await _ctx.SaveChangesAsync();
    }

    public Task DeactivateTokensAsync(string uid, IEnumerable<string> tokens)
    {
        var list = tokens.ToList();
        return _ctx.UserDevices.Where(x => x.UserId == uid && list.Contains(x.FcmToken))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
    }

    public Task DeactivateOtherTokensForDeviceAsync(string uid, string deviceId, string keepToken) =>
        _ctx.UserDevices.Where(x => x.UserId == uid && x.DeviceId == deviceId && x.FcmToken != keepToken && x.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

    public Task DeactivateTokenGloballyAsync(string fcmToken) =>
        _ctx.UserDevices.Where(x => x.FcmToken == fcmToken)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

    public async Task DeleteDeviceAsync(UserDevice device)
    {
        // notifications.device_id is NO ACTION in the schema (see deploy script), so detach first.
        await _ctx.Notifications.Where(x => x.DeviceId == device.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceId, (long?)null));
        _ctx.UserDevices.Remove(device);
        await _ctx.SaveChangesAsync();
    }

    public Task<NotificationPreference?> GetPreferencesAsync(string uid) =>
        _ctx.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == uid);

    public async Task AddPreferencesAsync(NotificationPreference prefs)
    {
        _ctx.NotificationPreferences.Add(prefs);
        await _ctx.SaveChangesAsync();
    }

    public async Task AddFeedbackAsync(Feedback feedback)
    {
        _ctx.Feedbacks.Add(feedback);
        await _ctx.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => _ctx.SaveChangesAsync();
}
