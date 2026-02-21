using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenShelf.Services;

public class GoogleBooksService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly ILogger<GoogleBooksService> _logger;

    public GoogleBooksService(HttpClient httpClient, SettingsService settingsService, ILogger<GoogleBooksService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<List<GoogleBookResult>> SearchBooksAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<GoogleBookResult>();

        // Run both searches in parallel
        var googleTask = SearchGoogleBooksAsync(query);
        var openLibTask = SearchOpenLibraryAsync(query);
        
        await Task.WhenAll(googleTask, openLibTask);
        
        var results = new List<GoogleBookResult>();
        results.AddRange(googleTask.Result);
        results.AddRange(openLibTask.Result);
        
        return results;
    }

    private async Task<List<GoogleBookResult>> SearchGoogleBooksAsync(string query)
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnableGoogleBooks) return new List<GoogleBookResult>();

        string encodedQuery;
        var partIndex = query.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (partIndex > 0)
        {
            var title = query.Substring(0, partIndex).Trim();
            var author = query.Substring(partIndex + 4).Trim();
            encodedQuery = $"intitle:{Uri.EscapeDataString(title)}+inauthor:{Uri.EscapeDataString(author)}";
        }
        else
        {
            encodedQuery = Uri.EscapeDataString(query);
        }

        var url = $"https://www.googleapis.com/books/v1/volumes?q={encodedQuery}&maxResults=40";
        
        // Add API key only if provided
        if (!string.IsNullOrEmpty(settings.GoogleBooksApiKey))
        {
            url += $"&key={settings.GoogleBooksApiKey}";
        }
        
        try 
        {
            _logger.LogInformation($"Searching Google Books: {query}");
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoogleBooksApiResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (result?.Items == null) 
            {
                _logger.LogWarning("Google Books returned null/empty items");
                return new List<GoogleBookResult>();
            }

            _logger.LogInformation($"Google Books found {result.Items.Count} results");

            return result.Items.Select(item => new GoogleBookResult 
            {
                Id = item.Id,
                Title = item.VolumeInfo?.Title ?? "Unknown Title",
                Authors = item.VolumeInfo?.Authors != null ? string.Join(", ", item.VolumeInfo.Authors) : "Unknown Author",
                Description = TruncateDescription(item.VolumeInfo?.Description, 500),
                ThumbnailUrl = item.VolumeInfo?.ImageLinks?.Thumbnail ?? item.VolumeInfo?.ImageLinks?.SmallThumbnail,
                FullDescription = item.VolumeInfo?.Description,
                PageCount = item.VolumeInfo?.PageCount,
                Categories = item.VolumeInfo?.Categories != null ? string.Join(", ", item.VolumeInfo.Categories) : null,
                Publisher = item.VolumeInfo?.Publisher,
                PublishedDate = item.VolumeInfo?.PublishedDate,
                Language = item.VolumeInfo?.Language
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Google Books search failed: {ex.Message}");
            return new List<GoogleBookResult>();
        }
    }

    private async Task<List<GoogleBookResult>> SearchOpenLibraryAsync(string query)
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnableOpenLibrary) return new List<GoogleBookResult>();

        string url;
        var partIndex = query.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (partIndex > 0)
        {
            var title = query.Substring(0, partIndex).Trim();
            var author = query.Substring(partIndex + 4).Trim();
            url = $"https://openlibrary.org/search.json?title={Uri.EscapeDataString(title)}&author={Uri.EscapeDataString(author)}&limit=40";
        }
        else
        {
            url = $"https://openlibrary.org/search.json?q={Uri.EscapeDataString(query)}&limit=40";
        }
        
        try 
        {
            _logger.LogInformation($"Searching Open Library: {query}");
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OpenLibraryResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (result?.Docs == null) return new List<GoogleBookResult>();

            _logger.LogInformation($"Open Library found {result.Docs.Count} results");

            return result.Docs.Take(40).Select(doc => new GoogleBookResult 
            {
                Id = doc.Key ?? Guid.NewGuid().ToString(),
                Title = doc.Title ?? "Unknown Title",
                Authors = doc.AuthorName != null ? string.Join(", ", doc.AuthorName) : "Unknown Author",
                Description = doc.FirstSentence?.FirstOrDefault(),
                ThumbnailUrl = doc.CoverId.HasValue ? $"https://covers.openlibrary.org/b/id/{doc.CoverId}-M.jpg" : null,
                FullDescription = doc.FirstSentence?.FirstOrDefault(),
                PageCount = doc.NumberOfPagesMedian,
                Categories = doc.Subject != null ? string.Join(", ", doc.Subject.Take(3)) : null,
                Publisher = doc.Publisher?.FirstOrDefault(),
                PublishedDate = doc.FirstPublishYear?.ToString(),
                Language = doc.Language?.FirstOrDefault()
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Open Library search failed: {ex.Message}");
            return new List<GoogleBookResult>();
        }
    }

    /// <summary>
    /// Fetches a book description, trying Google Books first, then Open Library.
    /// </summary>
    public async Task<string?> GetDescriptionAsync(string title, string authors)
    {
        var query = $"{title} {authors}";
        
        // Try Google Books first
        var googleResults = await SearchGoogleBooksAsync(query);
        var googleMatch = googleResults.FirstOrDefault(r => 
            !string.IsNullOrEmpty(r.FullDescription) &&
            (r.Title.Contains(title, StringComparison.OrdinalIgnoreCase) || 
             title.Contains(r.Title, StringComparison.OrdinalIgnoreCase)));
        
        if (!string.IsNullOrEmpty(googleMatch?.FullDescription))
        {
            _logger.LogInformation($"Found description in Google Books for: {title}");
            return googleMatch.FullDescription;
        }
        
        // Fallback to Open Library - need to get work details for full description
        try
        {
            var searchUrl = $"https://openlibrary.org/search.json?title={Uri.EscapeDataString(title)}&author={Uri.EscapeDataString(authors)}&limit=5";
            var response = await _httpClient.GetAsync(searchUrl);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var searchResult = JsonSerializer.Deserialize<OpenLibraryResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            var doc = searchResult?.Docs?.FirstOrDefault();
            if (doc?.Key != null)
            {
                // Fetch the work details which contains the full description
                var workUrl = $"https://openlibrary.org{doc.Key}.json";
                _logger.LogInformation($"Fetching Open Library work: {workUrl}");
                
                var workResponse = await _httpClient.GetAsync(workUrl);
                if (workResponse.IsSuccessStatusCode)
                {
                    var workJson = await workResponse.Content.ReadAsStringAsync();
                    var work = JsonSerializer.Deserialize<OpenLibraryWork>(workJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    var description = work?.Description;
                    if (description != null)
                    {
                        // Description can be a string or an object with "value" property
                        var descText = description.Value.ValueKind == JsonValueKind.String 
                            ? description.Value.GetString()
                            : description.Value.TryGetProperty("value", out var val) ? val.GetString() : null;
                        
                        if (!string.IsNullOrEmpty(descText))
                        {
                            _logger.LogInformation($"Found description in Open Library for: {title}");
                            return descText;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Open Library description fetch failed: {ex.Message}");
        }
        
        return null;
    }

    private string? TruncateDescription(string? description, int maxLength)
    {
        if (string.IsNullOrEmpty(description)) return null;
        if (description.Length <= maxLength) return description;
        return description.Substring(0, maxLength) + "...";
    }
}

public class GoogleBookResult
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? FullDescription { get; set; }
    public int? PageCount { get; set; }
    public string? Categories { get; set; }
    public string? Publisher { get; set; }
    public string? PublishedDate { get; set; }
    public string? Language { get; set; }
}

// Google Books API response classes
internal class GoogleBooksApiResponse
{
    public List<GoogleBookItem>? Items { get; set; }
}

internal class GoogleBookItem
{
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("volumeInfo")]
    public GoogleBookVolumeInfo? VolumeInfo { get; set; }
}

internal class GoogleBookVolumeInfo
{
    public string? Title { get; set; }
    public List<string>? Authors { get; set; }
    public string? Description { get; set; }
    public int? PageCount { get; set; }
    public List<string>? Categories { get; set; }
    public string? Publisher { get; set; }
    public string? PublishedDate { get; set; }
    public string? Language { get; set; }
    [JsonPropertyName("imageLinks")]
    public GoogleBookImageLinks? ImageLinks { get; set; }
}

internal class GoogleBookImageLinks
{
    public string? SmallThumbnail { get; set; }
    public string? Thumbnail { get; set; }
}

// Open Library API response classes
internal class OpenLibraryResponse
{
    public List<OpenLibraryDoc>? Docs { get; set; }
}

internal class OpenLibraryDoc
{
    public string? Key { get; set; }
    public string? Title { get; set; }
    
    [JsonPropertyName("author_name")]
    public List<string>? AuthorName { get; set; }
    
    [JsonPropertyName("cover_i")]
    public int? CoverId { get; set; }
    
    [JsonPropertyName("first_sentence")]
    public List<string>? FirstSentence { get; set; }
    
    [JsonPropertyName("number_of_pages_median")]
    public int? NumberOfPagesMedian { get; set; }
    
    public List<string>? Subject { get; set; }
    public List<string>? Publisher { get; set; }
    
    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; set; }
    public List<string>? Language { get; set; }
}

// Open Library Work details (for fetching description)
internal class OpenLibraryWork
{
    public JsonElement? Description { get; set; }
}

