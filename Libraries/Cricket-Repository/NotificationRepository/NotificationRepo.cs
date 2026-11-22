using Cricket_Repository.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cricket_Repository.NotificationRepository;

public interface INotificationRepo
{
    Task AddAsync(Notification notification);
    Task SaveChangesAsync();
    Task<List<Notification>> ListAsync(string uid, bool unreadOnly, int offset, int take);
    Task<int> UnreadCountAsync(string uid);
    Task<bool> MarkReadAsync(string uid, long id, DateTime now);
    Task MarkAllReadAsync(string uid, DateTime now);
    Task<bool> DeleteAsync(string uid, long id);
}

public class NotificationRepo : INotificationRepo
{
    private readonly CricketContext _ctx;

    public NotificationRepo(CricketContext ctx)
    {
        _ctx = ctx;
    }

    public async Task AddAsync(Notification notification)
    {
        _ctx.Notifications.Add(notification);
        await _ctx.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => _ctx.SaveChangesAsync();

    public Task<List<Notification>> ListAsync(string uid, bool unreadOnly, int offset, int take)
    {
        var q = _ctx.Notifications.AsNoTracking().Where(x => x.UserId == uid);
        if (unreadOnly) q = q.Where(x => x.ReadAt == null);
        return q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(offset).Take(take).ToListAsync();
    }

    public Task<int> UnreadCountAsync(string uid) =>
        _ctx.Notifications.CountAsync(x => x.UserId == uid && x.ReadAt == null);

    public async Task<bool> MarkReadAsync(string uid, long id, DateTime now)
    {
        var exists = await _ctx.Notifications.AnyAsync(x => x.UserId == uid && x.Id == id);
        if (!exists) return false;
        await _ctx.Notifications.Where(x => x.UserId == uid && x.Id == id && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, now));
        return true;
    }

    public Task MarkAllReadAsync(string uid, DateTime now) =>
        _ctx.Notifications.Where(x => x.UserId == uid && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, now));

    public async Task<bool> DeleteAsync(string uid, long id) =>
        await _ctx.Notifications.Where(x => x.UserId == uid && x.Id == id).ExecuteDeleteAsync() > 0;
}
