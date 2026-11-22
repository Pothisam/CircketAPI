using System.ComponentModel.DataAnnotations;

namespace Cricket_Models.PlayerModels;

public class PlayerDto
{
    public string Id { get; set; } = "";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public string Role { get; set; } = "batter";
    public string BattingStyle { get; set; } = "Right-hand bat";
    public string BowlingStyle { get; set; } = "None";
    public int? Version { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
