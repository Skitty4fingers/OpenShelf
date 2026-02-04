using System.ComponentModel.DataAnnotations;

namespace OpenShelf.Models;

public class RecommendationLike
{
    public int Id { get; set; }

    public int RecommendationId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public Recommendation Recommendation { get; set; } = null!;
}
