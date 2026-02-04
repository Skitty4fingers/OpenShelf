using System;
using System.Text.Json.Serialization;

namespace OpenShelf.Models;

public class Comment
{
    public int Id { get; set; }
    public string Author { get; set; } = "Anonymous";
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int RecommendationId { get; set; }
    [JsonIgnore]
    public Recommendation Recommendation { get; set; } = null!;
}
