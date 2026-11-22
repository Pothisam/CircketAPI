using Cricket_Models.MatchModels;
using Cricket_Models.PlayerModels;
using Cricket_Models.TeamModels;

namespace Cricket_Models.SyncModels;

public class SyncDeleted
{
    public List<string> Teams { get; set; } = new();
    public List<string> Players { get; set; } = new();
    public List<string> Matches { get; set; } = new();
}

public class SyncPushRequest
{
    public List<TeamDto> Teams { get; set; } = new();
    public List<PlayerDto> Players { get; set; } = new();
    public List<MatchDto> Matches { get; set; } = new();
    public SyncDeleted Deleted { get; set; } = new();
}

public class SyncCounts
{
    public int Teams { get; set; }
    public int Players { get; set; }
    public int Matches { get; set; }
}

public class SyncRejected
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Reason { get; set; } = "";
    /// <summary>Human readable explanation of the reason.</summary>
    public string? Message { get; set; }
}

public class SyncPushResponse
{
    public SyncCounts Accepted { get; set; } = new();
    public List<SyncRejected> Rejected { get; set; } = new();
    public DateTime ServerTime { get; set; }
}

public class SyncPullResponse
{
    public List<TeamDto> Teams { get; set; } = new();
    public List<PlayerDto> Players { get; set; } = new();
    public List<MatchDto> Matches { get; set; } = new();
    public SyncDeleted Deleted { get; set; } = new();
    public DateTime ServerTime { get; set; }
}
