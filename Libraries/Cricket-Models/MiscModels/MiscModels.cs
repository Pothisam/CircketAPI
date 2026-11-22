using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.MiscModels;

public class FeedbackRequest
{
    [Required, StringLength(2000)] public string Message { get; set; } = "";
    [StringLength(20)] public string? AppVersion { get; set; }
}

public class ConfigResponse
{
    public string MinAppVersion { get; set; } = "";
    public Dictionary<string, bool> Features { get; set; } = new();
}
