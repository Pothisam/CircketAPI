using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.TeamModels;

public class TeamDto
{
    public string Id { get; set; } = "";
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [Required, StringLength(8)] public string ShortName { get; set; } = "";
    public int ColorIndex { get; set; }
    /// <summary>true = other users can find this team by name and challenge it to a match; false = private (default).</summary>
    public bool IsPublic { get; set; }
    public List<string> PlayerIds { get; set; } = new();
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
