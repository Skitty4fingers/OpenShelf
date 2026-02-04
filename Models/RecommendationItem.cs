using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OpenShelf.Models;

public class RecommendationItem
{
    public int Id { get; set; }

    public int RecommendationId { get; set; }
    [JsonIgnore]
    public Recommendation? Recommendation { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Authors { get; set; } = string.Empty;

    public string? ThumbnailUrl { get; set; }
    
    public string? GoogleVolumeId { get; set; }

    // Extended metadata
    public string? Description { get; set; }
    public int? PageCount { get; set; }
    public string? Narrator { get; set; } // Audiobook narrator
    public string? ListeningLength { get; set; } // e.g. "16 hours and 10 minutes"
    public string? Categories { get; set; } // Comma-separated genres
    public string? Publisher { get; set; }
    public string? PublishedDate { get; set; }

    // Expanded Metadata from CSV
    public string? PurchaseDate { get; set; }
    public string? ReleaseDate { get; set; }
    public string? AverageRating { get; set; }
    public string? RatingCount { get; set; }
    public string? SeriesName { get; set; }
    public string? SeriesSequence { get; set; }
    public string? ProductId { get; set; }
    public string? Asin { get; set; }
    public string? BookUrl { get; set; }
    public string? SeriesUrl { get; set; }
    public string? Abridged { get; set; }
    public string? Language { get; set; }
    public string? Copyright { get; set; }
    public int? SeriesOrder { get; set; } // Position in series (1, 2, 3...)
}
