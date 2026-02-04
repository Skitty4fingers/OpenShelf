using System.ComponentModel.DataAnnotations;

namespace OpenShelf.Models;

public class Recommendation
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty; // Series Name or Book Title

    [Required]
    public string RecommendedBy { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public List<RecommendationItem> Items { get; set; } = new();

    public bool IsSeries => Items.Count > 1;

    // New fields for enhanced features
    public int Likes { get; set; } = 0;
    public string? SeriesDescription { get; set; }
    public bool IsStaffPick { get; set; } = false;
    public List<Comment> Comments { get; set; } = new();
    public List<RecommendationLike> LikeHistory { get; set; } = new();
}
