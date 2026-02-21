using HtmlAgilityPack;
using System.Web;

namespace OpenShelf.Services;

public class GoodreadsService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly ILogger<GoodreadsService> _logger;

    public GoodreadsService(HttpClient httpClient, SettingsService settingsService, ILogger<GoodreadsService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<(string? CoverImageUrl, string? Description, string? Rating)> SearchAndGetMetadataAsync(string title, string authors)
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnableGoodreads) return (null, null, null);

        try 
        {
            var query = $"{title} {authors}".Trim();
            var encodedQuery = HttpUtility.UrlEncode(query);
            var searchUrl = $"https://www.goodreads.com/search?q={encodedQuery}";

            _logger.LogInformation($"Searching Goodreads for: {query}");

            var response = await _httpClient.GetAsync(searchUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Goodreads search failed: {response.StatusCode} for {query}");
                return (null, null, null);
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find first search result
            var firstResult = doc.DocumentNode.SelectSingleNode("//table[@class='tableList']//tr[1]");
            if (firstResult == null)
            {
                // Try alternative selector for newer Goodreads layout
                firstResult = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'BookListItem')]");
            }

            if (firstResult == null)
            {
                _logger.LogWarning("No results found on Goodreads");
                return (null, null, null);
            }

            // Extract cover image
            string? coverImageUrl = null;
            var imgNode = firstResult.SelectSingleNode(".//img[contains(@class, 'bookCover')]");
            if (imgNode == null)
            {
                // Fallback: any img in the result
                imgNode = firstResult.SelectSingleNode(".//img");
            }
            
            if (imgNode != null)
            {
                coverImageUrl = imgNode.GetAttributeValue("src", null);
                
                // Goodreads often uses low-res thumbnails, try to get higher quality
                if (!string.IsNullOrEmpty(coverImageUrl))
                {
                    // Replace size parameters to get larger image
                    coverImageUrl = coverImageUrl.Replace("._SY75_", "._SY475_")
                                                 .Replace("._SX50_", "._SX318_")
                                                 .Replace("._SY98_", "._SY475_")
                                                 .Replace("/nophoto/", "/photo/"); // Sometimes helps
                    
                    _logger.LogInformation($"Found Goodreads cover: {coverImageUrl}");
                }
            }

            // Try to get the book's detail page URL for more info
            var bookLinkNode = firstResult.SelectSingleNode(".//a[@class='bookTitle']");
            if (bookLinkNode == null)
            {
                bookLinkNode = firstResult.SelectSingleNode(".//a[contains(@href, '/book/show/')]");
            }

            string? description = null;
            string? rating = null;

            if (bookLinkNode != null)
            {
                var bookUrl = bookLinkNode.GetAttributeValue("href", null);
                if (!string.IsNullOrEmpty(bookUrl))
                {
                    // Make sure it's a full URL
                    if (!bookUrl.StartsWith("http"))
                    {
                        bookUrl = "https://www.goodreads.com" + bookUrl;
                    }

                    // Fetch the book detail page
                    try
                    {
                        var bookResponse = await _httpClient.GetAsync(bookUrl);
                        if (bookResponse.IsSuccessStatusCode)
                        {
                            var bookHtml = await bookResponse.Content.ReadAsStringAsync();
                            var bookDoc = new HtmlDocument();
                            bookDoc.LoadHtml(bookHtml);

                            // Try to extract description
                            var descNode = bookDoc.DocumentNode.SelectSingleNode("//div[@id='description']//span[last()]");
                            if (descNode == null)
                            {
                                descNode = bookDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'DetailsLayoutRightParagraph')]");
                            }
                            if (descNode != null)
                            {
                                description = descNode.InnerText.Trim();
                            }

                            // Try to extract rating
                            var ratingNode = bookDoc.DocumentNode.SelectSingleNode("//div[@class='RatingStatistics__rating']");
                            if (ratingNode == null)
                            {
                                ratingNode = bookDoc.DocumentNode.SelectSingleNode("//span[@itemprop='ratingValue']");
                            }
                            if (ratingNode != null)
                            {
                                rating = ratingNode.InnerText.Trim();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch Goodreads book detail page");
                    }
                }
            }

            return (coverImageUrl, description, rating);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping Goodreads");
            return (null, null, null);
        }
    }

    public async Task<List<SeriesBookInfo>> GetSeriesBooksAsync(string seriesName, string? firstBookTitle = null)
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnableGoodreads) return new List<SeriesBookInfo>();

        try
        {
            _logger.LogInformation($"Searching Goodreads series: {seriesName}");
            
            // Search for the series
            var query = firstBookTitle != null ? $"{firstBookTitle} {seriesName}" : seriesName;
            var encodedQuery = HttpUtility.UrlEncode(query);
            var searchUrl = $"https://www.goodreads.com/search?q={encodedQuery}";

            var response = await _httpClient.GetAsync(searchUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Goodreads series search failed: {response.StatusCode}");
                return new List<SeriesBookInfo>();
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find first book result and get its detail page
            var firstResult = doc.DocumentNode.SelectSingleNode("//table[@class='tableList']//tr[1]");
            if (firstResult == null)
            {
                firstResult = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'BookListItem')]");
            }

            if (firstResult == null)
            {
                _logger.LogWarning("No results found for series");
                return new List<SeriesBookInfo>();
            }

            var bookLinkNode = firstResult.SelectSingleNode(".//a[@class='bookTitle']");
            if (bookLinkNode == null)
            {
                bookLinkNode = firstResult.SelectSingleNode(".//a[contains(@href, '/book/show/')]");
            }

            if (bookLinkNode == null)
            {
                return new List<SeriesBookInfo>();
            }

            var bookUrl = bookLinkNode.GetAttributeValue("href", null);
            if (string.IsNullOrEmpty(bookUrl))
            {
                return new List<SeriesBookInfo>();
            }

            if (!bookUrl.StartsWith("http"))
            {
                bookUrl = "https://www.goodreads.com" + bookUrl;
            }

            // Fetch the book detail page to find series link
            var bookResponse = await _httpClient.GetAsync(bookUrl);
            if (!bookResponse.IsSuccessStatusCode)
            {
                return new List<SeriesBookInfo>();
            }

            var bookHtml = await bookResponse.Content.ReadAsStringAsync();
            var bookDoc = new HtmlDocument();
            bookDoc.LoadHtml(bookHtml);

            // Find series link
            var seriesLinkNode = bookDoc.DocumentNode.SelectSingleNode("//a[contains(@href, '/series/')]");
            if (seriesLinkNode == null)
            {
                _logger.LogWarning("No series link found on book page");
                return new List<SeriesBookInfo>();
            }

            var seriesUrl = seriesLinkNode.GetAttributeValue("href", null);
            if (string.IsNullOrEmpty(seriesUrl))
            {
                return new List<SeriesBookInfo>();
            }

            if (!seriesUrl.StartsWith("http"))
            {
                seriesUrl = "https://www.goodreads.com" + seriesUrl;
            }

            _logger.LogInformation($"Found series page: {seriesUrl}");

            // Fetch the series page
            var seriesResponse = await _httpClient.GetAsync(seriesUrl);
            if (!seriesResponse.IsSuccessStatusCode)
            {
                return new List<SeriesBookInfo>();
            }

            var seriesHtml = await seriesResponse.Content.ReadAsStringAsync();
            var seriesDoc = new HtmlDocument();
            seriesDoc.LoadHtml(seriesHtml);

            // Extract all books from the series
            var books = new List<SeriesBookInfo>();
            var bookNodes = seriesDoc.DocumentNode.SelectNodes("//div[contains(@itemtype, 'Book')]");
            
            if (bookNodes == null || !bookNodes.Any())
            {
                // Try alternative selector
                bookNodes = seriesDoc.DocumentNode.SelectNodes("//div[contains(@class, 'responsiveBook')]");
            }

            string? targetLanguage = null; // Will be set from first book

            if (bookNodes != null)
            {
                foreach (var bookNode in bookNodes)
                {
                    var titleNode = bookNode.SelectSingleNode(".//a[@class='bookTitle']");
                    if (titleNode == null)
                    {
                        titleNode = bookNode.SelectSingleNode(".//a[contains(@class, 'gr-h3')]");
                    }

                    var authorNode = bookNode.SelectSingleNode(".//a[@class='authorName']");
                    var imgNode = bookNode.SelectSingleNode(".//img");

                    if (titleNode != null)
                    {
                        var title = titleNode.InnerText.Trim();
                        
                        // Decode HTML entities (e.g., &#x27; to ')
                        title = System.Net.WebUtility.HtmlDecode(title);
                        
                        var author = authorNode?.InnerText.Trim() ?? "Unknown";
                        author = System.Net.WebUtility.HtmlDecode(author);
                        
                        // Skip collections (they usually have "Collection" or "Omnibus" in the title)
                        if (title.Contains("Collection", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Omnibus", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Box Set", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Boxed Set", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation($"Skipping collection: {title}");
                            continue;
                        }
                        
                        // Detect language from the book node (look for language indicators in the HTML)
                        // Goodreads sometimes includes language info in the book details
                        var bookLanguage = "English"; // Default to English
                        var detailsNode = bookNode.SelectSingleNode(".//div[contains(@class, 'bookDetails')]");
                        if (detailsNode != null)
                        {
                            var detailsText = detailsNode.InnerText;
                            // Look for language indicators like "Spanish", "French", etc.
                            if (detailsText.Contains("Spanish", StringComparison.OrdinalIgnoreCase))
                                bookLanguage = "Spanish";
                            else if (detailsText.Contains("French", StringComparison.OrdinalIgnoreCase))
                                bookLanguage = "French";
                            else if (detailsText.Contains("German", StringComparison.OrdinalIgnoreCase))
                                bookLanguage = "German";
                            else if (detailsText.Contains("Italian", StringComparison.OrdinalIgnoreCase))
                                bookLanguage = "Italian";
                            else if (detailsText.Contains("Portuguese", StringComparison.OrdinalIgnoreCase))
                                bookLanguage = "Portuguese";
                            else if (detailsText.Contains("Japanese", StringComparison.OrdinalIgnoreCase))
                                bookLanguage = "Japanese";
                            else if (detailsText.Contains("Chinese", StringComparison.OrdinalIgnoreCase))
                                bookLanguage = "Chinese";
                        }
                        
                        // Set target language from first book
                        if (targetLanguage == null)
                        {
                            targetLanguage = bookLanguage;
                            _logger.LogInformation($"Target language set to: {targetLanguage}");
                        }
                        
                        // Skip books in different languages
                        if (!bookLanguage.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation($"Skipping book in different language: {title} ({bookLanguage})");
                            continue;
                        }
                        
                        var coverUrl = imgNode?.GetAttributeValue("src", null);

                        // Upgrade cover quality
                        if (!string.IsNullOrEmpty(coverUrl))
                        {
                            coverUrl = coverUrl.Replace("._SY75_", "._SY475_")
                                             .Replace("._SX50_", "._SX318_")
                                             .Replace("._SY98_", "._SY475_");
                        }

                        // Extract series order from title (e.g., "Book Title (Series Name, #3)")
                        var orderMatch = System.Text.RegularExpressions.Regex.Match(title, @"#(\d+(?:\.\d+)?)");
                        double order = 0;
                        if (orderMatch.Success && double.TryParse(orderMatch.Groups[1].Value, out var parsedOrder))
                        {
                            order = parsedOrder;
                        }
                        else
                        {
                            order = books.Count + 1; // Fallback to position
                        }

                        // Clean up title (remove series info in parentheses)
                        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\([^)]*#[^)]*\)\s*$", "").Trim();

                        books.Add(new SeriesBookInfo
                        {
                            Title = title,
                            Author = author,
                            CoverUrl = coverUrl,
                            SeriesOrder = order
                        });
                    }
                }
            }

            _logger.LogInformation($"Found {books.Count} books in series (filtered by language: {targetLanguage ?? "English"})");
            return books.OrderBy(b => b.SeriesOrder).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching series books from Goodreads");
            return new List<SeriesBookInfo>();
        }
    }
}

public class SeriesBookInfo
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public double SeriesOrder { get; set; }
}
