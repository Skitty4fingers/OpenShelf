using HtmlAgilityPack;
using System.Net;
using System.Web;

namespace OpenShelf.Services;

public class AudibleService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly ILogger<AudibleService> _logger;

    public AudibleService(HttpClient httpClient, SettingsService settingsService, ILogger<AudibleService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
    }

    public async Task<(string? Narrator, string? Length, string? Description, string? CoverImageUrl)> SearchAndGetMetadataAsync(string title, string authors)
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnableAudible) return (null, null, null, null);

        try 
        {
            var query = $"{title} {authors}".Trim();
            var encodedQuery = HttpUtility.UrlEncode(query);
            var searchUrl = $"https://www.audible.com/search?keywords={encodedQuery}";

            var response = await _httpClient.GetAsync(searchUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Audible search failed: {response.StatusCode} for {query}");
                return (null, null, null, null);
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find first product list item
            var firstItem = doc.DocumentNode.SelectSingleNode("//li[contains(@class, 'productListItem')]");
            if (firstItem == null) 
            {
                // Try fallback selector (sometimes structure changes)
                firstItem = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'adbl-impression-container')]//li[contains(@class, 'bc-list-item')]");
            }

            if (firstItem == null) return (null, null, null, null);

            // Cover Image - look for img tag in the product
            string? coverImageUrl = null;
            var imgNode = firstItem.SelectSingleNode(".//img[contains(@class, 'bc-image-inset-border')]");
            if (imgNode == null)
            {
                // Fallback: any img tag in the item
                imgNode = firstItem.SelectSingleNode(".//img");
            }
            if (imgNode != null)
            {
                coverImageUrl = imgNode.GetAttributeValue("src", null);
                // Audible sometimes uses lazy loading with data-lazy
                if (string.IsNullOrEmpty(coverImageUrl))
                {
                    coverImageUrl = imgNode.GetAttributeValue("data-lazy", null);
                }
                
                // Clean up the URL - remove size parameters to get higher quality
                if (!string.IsNullOrEmpty(coverImageUrl))
                {
                    // Replace small image sizes with larger ones
                    coverImageUrl = coverImageUrl.Replace("._SL300_", "._SL500_")
                                                 .Replace("._SL175_", "._SL500_")
                                                 .Replace("._SL100_", "._SL500_");
                    _logger.LogInformation($"Found cover image: {coverImageUrl}");
                }
            }

            // Narrator
            string? narrator = null;
            var narratorNode = firstItem.SelectSingleNode(".//li[contains(@class, 'narratorLabel')]");
            if (narratorNode != null)
            {
                narrator = narratorNode.InnerText.Trim();
                // Clean up "Narrated by:"
                narrator = narrator.Replace("Narrated by:", "").Trim();
            }

            // Length
            string? length = null;
            var lengthNode = firstItem.SelectSingleNode(".//li[contains(@class, 'runtimeLabel')]");
            if (lengthNode != null)
            {
                length = lengthNode.InnerText.Trim();
                length = length.Replace("Length:", "").Trim();
            }

            // Description (Subtitle or Summary)
            // Usually subtitle is in h3 > a. Summary is in a paragraph.
            // But we already have Google Books description.
            // If we want "Listening Length", that's the main goal.
            
            return (narrator, length, null, coverImageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping Audible");
            return (null, null, null, null);
        }
    }
}
