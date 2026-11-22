using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.NotificationModels;

public class PreferencesDto
{
    public bool LoginAlerts { get; set; } = true;
    public bool MatchStart { get; set; } = true;
    public bool Boundaries { get; set; } = true;
    public bool Wickets { get; set; } = true;
    public bool Milestones { get; set; } = true;
    public bool InningsEnd { get; set; } = true;
    public bool MatchResult { get; set; } = true;
    /// <summary>"HH:mm" (UTC), or null.</summary>
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }
}

public class NotificationDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public Dictionary<string, string>? Data { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InboxResponse
{
    public List<NotificationDto> Items { get; set; } = new();
    public int UnreadCount { get; set; }
    public string? NextCursor { get; set; }
}

public class TestNotificationRequest
{
    [StringLength(160)] public string Title { get; set; } = "Test";
    [StringLength(500)] public string Body { get; set; } = "Hello";
}

public class SendResultDto
{
    public int Sent { get; set; }
    public int Failed { get; set; }
}

public class BroadcastRequest
{
    [Required, StringLength(160)] public string Title { get; set; } = "";
    [Required, StringLength(500)] public string Body { get; set; } = "";
    [StringLength(100)] public string Topic { get; set; } = "all";
}
