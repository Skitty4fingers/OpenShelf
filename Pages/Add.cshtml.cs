using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenShelf.Data;
using OpenShelf.Models;
using OpenShelf.Services;

namespace OpenShelf.Pages;

public class AddModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly GoogleBooksService _googleBooksService;
    private readonly AudibleService _audibleService;
    private readonly ILogger<AddModel> _logger;

    public AddModel(AppDbContext context, GoogleBooksService googleBooksService, AudibleService audibleService, ILogger<AddModel> logger)
    {
        _context = context;
        _googleBooksService = googleBooksService;
        _audibleService = audibleService;
        _logger = logger;
    }

    [BindProperty]
    public Recommendation Recommendation { get; set; } = new();

    // Helper property to bind list of items from form
    [BindProperty]
    public List<RecommendationItem> BookItems { get; set; } = new();

    [BindProperty]
    public string? SearchQuery { get; set; }

    public List<GoogleBookResult>? SearchResults { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostSearchAsync()
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchResults = await _googleBooksService.SearchBooksAsync(SearchQuery);
        }
        return Page();
    }

    // JSON endpoint for autocomplete
    public async Task<IActionResult> OnGetSearchAsync(string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        {
            return new JsonResult(new List<object>());
        }
        
        var results = await _googleBooksService.SearchBooksAsync(q);
        return new JsonResult(results);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        _logger.LogInformation($"Submit received. Title: {Recommendation.Title}. Items Count: {BookItems?.Count}");

        // Manual validation for Items
        if (BookItems == null || !BookItems.Any())
        {
            _logger.LogWarning("BookItems list is empty!");
            ModelState.AddModelError("", "Please add at least one book to the recommendation.");
        }

        if (!ModelState.IsValid)
        {
            foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
            {
                _logger.LogWarning($"Validation Error: {error.ErrorMessage}");
            }
            return Page();
        }

        // Auto-refresh metadata
        foreach (var item in BookItems)
        {
            try 
            {
                var query = $"{item.Title} by {item.Authors}";
                var results = await _googleBooksService.SearchBooksAsync(query);
                var bestMatch = results.FirstOrDefault();
                if (bestMatch != null)
                {
                    if (string.IsNullOrEmpty(item.Description)) item.Description = bestMatch.Description;
                    if (string.IsNullOrEmpty(item.ThumbnailUrl)) item.ThumbnailUrl = bestMatch.ThumbnailUrl;
                    if (string.IsNullOrEmpty(item.PublishedDate)) item.PublishedDate = bestMatch.PublishedDate;
                    if (string.IsNullOrEmpty(item.GoogleVolumeId)) item.GoogleVolumeId = bestMatch.Id;
                    if (!item.PageCount.HasValue) item.PageCount = bestMatch.PageCount;
                }
            } 
            catch { /* Ignore api failures */ }

            // Auto-fetch Audiobook metadata
            try
            {
                var (narrator, length, _, coverImage) = await _audibleService.SearchAndGetMetadataAsync(item.Title, item.Authors);
                if (!string.IsNullOrEmpty(narrator)) item.Narrator = narrator;
                if (!string.IsNullOrEmpty(length)) item.ListeningLength = length;
                // Use Amazon cover as fallback if Google Books didn't provide one
                if (string.IsNullOrEmpty(item.ThumbnailUrl) && !string.IsNullOrEmpty(coverImage))
                {
                    item.ThumbnailUrl = coverImage;
                }
            }
            catch (Exception ex) 
            {
                _logger.LogWarning(ex, "Failed to fetch audiobook metadata");
            }
        }

        Recommendation.AddedAt = DateTime.UtcNow;
        Recommendation.Items = BookItems; // EF Core will handle relations

        _context.Recommendations.Add(Recommendation);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Saved Recommendation to DB successfully.");

        return RedirectToPage("/Index");
    }
}
