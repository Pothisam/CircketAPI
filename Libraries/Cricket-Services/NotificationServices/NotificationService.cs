using Cricket_Models.CommonModels;
using Cricket_Models.Enums;
using Cricket_Models.NotificationModels;
using Cricket_Repository.Entity;
using Cricket_Repository.NotificationRepository;
using Cricket_Repository.UserRepository;
using Cricket_Services.PushServices;
using System.Globalization;
using System.Text.Json;

namespace Cricket_Services.NotificationServices;

public class OutgoingNotification
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public Dictionary<string, string>? Data { get; set; }
    /// <summary>Which preference switch gates this notification (null = always send).</summary>
    public Func<NotificationPreference, bool>? Preference { get; set; }
    public long? OnlyDeviceId { get; set; }
    public long? ExcludeDeviceId { get; set; }
    public bool IgnoreQuietHours { get; set; }
}

public interface INotificationService
{
    /// <summary>Logs the notification in the inbox and pushes it to the user's eligible devices.</summary>
    Task<SendResultDto> SendToUserAsync(string uid, OutgoingNotification notification);

    Task<InboxResponse> GetInboxAsync(string uid, bool unreadOnly, int? limit, string? cursor);
    Task MarkReadAsync(string uid, long id);
    Task MarkAllReadAsync(string uid);
    Task DeleteAsync(string uid, long id);
    Task<PreferencesDto> GetPreferencesAsync(string uid);
    Task<PreferencesDto> UpdatePreferencesAsync(string uid, PreferencesDto dto);
    Task<SendResultDto> SendTestAsync(string uid, TestNotificationRequest request);
    Task<string?> BroadcastAsync(BroadcastRequest request);
}

public class NotificationService : INotificationService
{
    private readonly IUserRepo _users;
    private readonly INotificationRepo _notifications;
    private readonly IPushSender _push;

    public NotificationService(IUserRepo users, INotificationRepo notifications, IPushSender push)
    {
        _users = users;
        _notifications = notifications;
        _push = push;
    }

    public async Task<SendResultDto> SendToUserAsync(string uid, OutgoingNotification n)
    {
        var prefs = await _users.GetPreferencesAsync(uid) ?? new NotificationPreference { UserId = uid };
        if (n.Preference != null && !n.Preference(prefs)) return new SendResultDto();

        var now = Clock.UtcNow();
        var data = new Dictionary<string, string>(n.Data ?? new()) { ["type"] = n.Type };
        var row = new Notification
        {
            UserId = uid,
            Type = n.Type,
            Title = Trunc(n.Title, 160)!,
            Body = Trunc(n.Body, 500)!,
            Data = JsonSerializer.Serialize(data),
            DeviceId = n.OnlyDeviceId,
            Status = "queued",
            CreatedAt = now
        };
        await _notifications.AddAsync(row);   // inbox row exists even if the push can't be delivered

        var devices = (await _users.ListActiveDevicesAsync(uid))
            .Where(d => d.NotificationsEnabled
                        && (n.OnlyDeviceId == null || d.Id == n.OnlyDeviceId)
                        && (n.ExcludeDeviceId == null || d.Id != n.ExcludeDeviceId))
            .ToList();

        if (!n.IgnoreQuietHours && InQuietHours(prefs, now))
        {
            row.Error = "quiet_hours";
            await _notifications.SaveChangesAsync();
            return new SendResultDto { Reason = "quiet_hours" };
        }
        if (devices.Count == 0)
        {
            row.Status = "failed";
            row.Error = "no_target_devices";
            await _notifications.SaveChangesAsync();
            return new SendResultDto { Reason = "no_target_devices" };
        }

        var results = await _push.SendAsync(devices.Select(d => d.FcmToken).ToList(), n.Title, n.Body, data);

        foreach (var invalid in results.Where(r => r.InvalidToken))
        {
            await _users.DeactivateTokenGloballyAsync(invalid.Token);
        }

        var ok = results.Where(r => r.Success).ToList();
        row.Status = ok.Count > 0 ? "sent" : "failed";
        row.FcmMessageId = ok.FirstOrDefault()?.MessageId;
        row.SentAt = ok.Count > 0 ? now : null;
        row.Error = ok.Count > 0 ? null : Trunc(results.FirstOrDefault()?.Error, 255);
        await _notifications.SaveChangesAsync();
        return new SendResultDto
        {
            Sent = ok.Count,
            Failed = results.Count - ok.Count,
            Reason = ok.Count > 0 ? null : (results.FirstOrDefault()?.Error ?? "send_failed")
        };
    }

    // ------------------------------------------------------------------ inbox

    public async Task<InboxResponse> GetInboxAsync(string uid, bool unreadOnly, int? limit, string? cursor)
    {
        var (l, offset) = Paging.Parse(limit, cursor);
        var rows = await _notifications.ListAsync(uid, unreadOnly, offset, l + 1);
        var page = Paging.Build(rows, l, offset);
        return new InboxResponse
        {
            Items = page.Items.Select(ToDto).ToList(),
            UnreadCount = await _notifications.UnreadCountAsync(uid),
            NextCursor = page.NextCursor
        };
    }

    public async Task MarkReadAsync(string uid, long id)
    {
        if (!await _notifications.MarkReadAsync(uid, id, Clock.UtcNow())) throw ApiException.NotFound("Notification");
    }

    public Task MarkAllReadAsync(string uid) => _notifications.MarkAllReadAsync(uid, Clock.UtcNow());

    public async Task DeleteAsync(string uid, long id)
    {
        if (!await _notifications.DeleteAsync(uid, id)) throw ApiException.NotFound("Notification");
    }

    // ------------------------------------------------------------ preferences

    public async Task<PreferencesDto> GetPreferencesAsync(string uid)
    {
        var p = await _users.GetPreferencesAsync(uid);
        return p == null ? new PreferencesDto() : ToDto(p);
    }

    public async Task<PreferencesDto> UpdatePreferencesAsync(string uid, PreferencesDto dto)
    {
        var start = ParseTime(dto.QuietHoursStart, "quietHoursStart");
        var end = ParseTime(dto.QuietHoursEnd, "quietHoursEnd");

        var p = await _users.GetPreferencesAsync(uid);
        var isNew = p == null;
        p ??= new NotificationPreference { UserId = uid };
        p.LoginAlerts = dto.LoginAlerts;
        p.MatchStart = dto.MatchStart;
        p.Boundaries = dto.Boundaries;
        p.Wickets = dto.Wickets;
        p.Milestones = dto.Milestones;
        p.InningsEnd = dto.InningsEnd;
        p.MatchResult = dto.MatchResult;
        p.QuietHoursStart = start;
        p.QuietHoursEnd = end;
        p.UpdatedAt = Clock.UtcNow();
        if (isNew) await _users.AddPreferencesAsync(p); else await _users.SaveChangesAsync();
        return ToDto(p);
    }

    // ------------------------------------------------------------ test / admin

    public Task<SendResultDto> SendTestAsync(string uid, TestNotificationRequest request) =>
        SendToUserAsync(uid, new OutgoingNotification
        {
            Type = NotificationTypes.Test,
            Title = request.Title,
            Body = request.Body,
            IgnoreQuietHours = true
        });

    public Task<string?> BroadcastAsync(BroadcastRequest request) =>
        _push.SendToTopicAsync(request.Topic, request.Title, request.Body,
            new Dictionary<string, string> { ["type"] = "broadcast" });

    // ---------------------------------------------------------------- helpers

    private static bool InQuietHours(NotificationPreference p, DateTime nowUtc)
    {
        if (p.QuietHoursStart is not { } start || p.QuietHoursEnd is not { } end || start == end) return false;
        var t = nowUtc.TimeOfDay;   // quiet hours are interpreted in UTC
        return start < end ? t >= start && t < end : t >= start || t < end;
    }

    private static TimeSpan? ParseTime(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var t)) return t;
        throw ApiException.Validation($"{field} must be HH:mm", new List<ErrorDetail> { new(field, "must be HH:mm") });
    }

    private static string? Trunc(string? s, int max) => s == null || s.Length <= max ? s : s[..max];

    private static PreferencesDto ToDto(NotificationPreference p) => new()
    {
        LoginAlerts = p.LoginAlerts,
        MatchStart = p.MatchStart,
        Boundaries = p.Boundaries,
        Wickets = p.Wickets,
        Milestones = p.Milestones,
        InningsEnd = p.InningsEnd,
        MatchResult = p.MatchResult,
        QuietHoursStart = p.QuietHoursStart?.ToString(@"hh\:mm"),
        QuietHoursEnd = p.QuietHoursEnd?.ToString(@"hh\:mm")
    };

    private static NotificationDto ToDto(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Body = n.Body,
        Data = string.IsNullOrEmpty(n.Data) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(n.Data),
        ReadAt = n.ReadAt,
        CreatedAt = n.CreatedAt
    };
}
