using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;
using OpenShelf.Services;

namespace OpenShelf.Pages;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly GoogleBooksService _booksService;
    private readonly ILogger<DetailsModel> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // Shared progress tracker (reusing pattern from Import)
    public static ConcurrentDictionary<string, ProcessStatus> ProcessTracker = new();

    public class ProcessStatus
    {
        public int Percent { get; set; }
        public string Message { get; set; } = "";
        public bool IsComplete { get; set; }
        public bool IsError { get; set; }
    }

    public DetailsModel(AppDbContext context, GoogleBooksService booksService, ILogger<DetailsModel> logger, IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _booksService = booksService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public Recommendation Recommendation { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var recommendation = await _context.Recommendations
            .Include(r => r.Items.OrderBy(i => i.SeriesOrder))
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recommendation == null)
        {
            return NotFound();
        }

        Recommendation = recommendation;
        return Page();
    }

    public async Task<IActionResult> OnPostLikeAsync(int id)
    {
        var recommendation = await _context.Recommendations.FindAsync(id);
        if (recommendation != null)
        {
            recommendation.Likes++;
            await _context.SaveChangesAsync();
            return new JsonResult(new { likes = recommendation.Likes });
        }
        return NotFound();
    }

    // New async endpoint to start metadata refresh
    public IActionResult OnPostStartRefreshMetadata(int id)
    {
        var processId = Guid.NewGuid().ToString();
        ProcessTracker.TryAdd(processId, new ProcessStatus { Percent = 0, Message = "Starting metadata refresh...", IsComplete = false });

        // Start background task
        _ = Task.Run(() => RunRefreshMetadataLogic(processId, id));

        return new JsonResult(new { success = true, processId = processId });
    }

    // Progress polling endpoint
    public JsonResult OnGetRefreshProgress(string processId)
    {
        if (ProcessTracker.TryGetValue(processId, out var status))
        {
            return new JsonResult(status);
        }
        return new JsonResult(new { percent = 0, message = "Unknown process", isError = true });
    }

    // Background logic for metadata refresh
    private async Task RunRefreshMetadataLogic(string processId, int recommendationId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var booksService = scope.ServiceProvider.GetRequiredService<GoogleBooksService>();

            UpdateRefreshStatus(processId, 5, "Loading recommendation...");

            var recommendation = await context.Recommendations
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == recommendationId);

            if (recommendation == null)
            {
                UpdateRefreshStatus(processId, 0, "Recommendation not found", isError: true);
                return;
            }

            int updatedCount = 0;
            int totalItems = recommendation.Items.Count;
            int currentItem = 0;

            foreach (var item in recommendation.Items)
            {
                currentItem++;
                int progress = 10 + (int)((double)currentItem / totalItems * 80); // 10-90%
                UpdateRefreshStatus(processId, progress, $"Fetching metadata: {item.Title} ({currentItem}/{totalItems})");

                // Skip if we already have description
                if (!string.IsNullOrEmpty(item.Description)) continue;

                // Search for this book by title + author
                var searchQuery = $"{item.Title} {item.Authors}";
                _logger.LogInformation($"Fetching metadata for: {searchQuery}");

                var results = await booksService.SearchBooksAsync(searchQuery);

                // Find best match (first result with matching title)
                var match = results.FirstOrDefault(r =>
                    r.Title.Contains(item.Title, StringComparison.OrdinalIgnoreCase) ||
                    item.Title.Contains(r.Title, StringComparison.OrdinalIgnoreCase));

                if (match == null && results.Any())
                {
                    match = results.First(); // Use first result as fallback
                }

                if (match != null)
                {
                    _logger.LogInformation($"Found match: {match.Title}");

                    // Update missing fields
                    if (string.IsNullOrEmpty(item.Description))
                    {
                        item.Description = match.FullDescription ?? match.Description;

                        // If still no description, try dedicated description fetch (includes Open Library fallback)
                        if (string.IsNullOrEmpty(item.Description))
                        {
                            _logger.LogInformation($"No description from search, trying dedicated fetch for: {item.Title}");
                            item.Description = await booksService.GetDescriptionAsync(item.Title, item.Authors);
                        }
                    }
                    if (!item.PageCount.HasValue && match.PageCount.HasValue)
                        item.PageCount = match.PageCount;
                    if (string.IsNullOrEmpty(item.Categories))
                        item.Categories = match.Categories;
                    if (string.IsNullOrEmpty(item.Publisher))
                        item.Publisher = match.Publisher;
                    if (string.IsNullOrEmpty(item.PublishedDate))
                        item.PublishedDate = match.PublishedDate;
                    if (string.IsNullOrEmpty(item.ThumbnailUrl))
                        item.ThumbnailUrl = match.ThumbnailUrl;
                    if (string.IsNullOrEmpty(item.GoogleVolumeId))
                        item.GoogleVolumeId = match.Id;

                    // Try to extract series order from title if not manually set or if strictly default
                    var extractedOrder = ExtractSeriesOrder(item.Title);
                    if (extractedOrder.HasValue)
                    {
                        item.SeriesOrder = extractedOrder.Value;
                    }

                    updatedCount++;
                }
                else
                {
                    // No match found, but still try to get description
                    _logger.LogInformation($"No match found, trying dedicated description fetch for: {item.Title}");
                    var desc = await booksService.GetDescriptionAsync(item.Title, item.Authors);
                    if (!string.IsNullOrEmpty(desc))
                    {
                        item.Description = desc;
                        updatedCount++;
                    }

                    // Still try to exact order from existing title
                    var extractedOrder = ExtractSeriesOrder(item.Title);
                    if (extractedOrder.HasValue)
                    {
                        item.SeriesOrder = extractedOrder.Value;
                    }
                }
            }

            UpdateRefreshStatus(processId, 90, "Finalizing...");

            // Fallback Logic: If series order couldn't be determined for some items, sort by release date
            var orders = recommendation.Items.Select(i => i.SeriesOrder).Where(o => o > 0).ToList();
            bool hasMissingOrDuplicateOrders = orders.Count < recommendation.Items.Count || orders.Distinct().Count() < orders.Count;

            if (hasMissingOrDuplicateOrders)
            {
                _logger.LogInformation("Series order incomplete or ambiguous. Falling back to release date sort.");

                var sortedItems = recommendation.Items
                    .OrderBy(i =>
                    {
                        if (DateTime.TryParse(i.PublishedDate, out DateTime date)) return date;
                        if (int.TryParse(i.PublishedDate, out int year)) return new DateTime(year, 1, 1);
                        return DateTime.MaxValue;
                    })
                    .ToList();

                for (int i = 0; i < sortedItems.Count; i++)
                {
                    sortedItems[i].SeriesOrder = i + 1;
                }
                updatedCount += sortedItems.Count;
            }

            await context.SaveChangesAsync();
            _logger.LogInformation($"Updated {updatedCount} items");

            UpdateRefreshStatus(processId, 100, $"Complete! Updated {updatedCount} book(s)", isComplete: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing metadata");
            UpdateRefreshStatus(processId, 0, $"Error: {ex.Message}", isError: true);
        }
    }

    private void UpdateRefreshStatus(string id, int percent, string msg, bool isComplete = false, bool isError = false)
    {
        if (ProcessTracker.TryGetValue(id, out var status))
        {
            status.Percent = percent;
            status.Message = msg;
            status.IsComplete = isComplete;
            status.IsError = isError;
        }
    }

    // Keep old synchronous method for backwards compatibility (deprecated)
    public async Task<IActionResult> OnPostRefreshMetadataAsync(int id)
    {
        _logger.LogInformation($"Refreshing metadata for recommendation {id}");
        
        var recommendation = await _context.Recommendations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recommendation == null)
        {
            return NotFound();
        }

        int updatedCount = 0;
        
        foreach (var item in recommendation.Items)
        {
            // Skip if we already have description
            if (!string.IsNullOrEmpty(item.Description)) continue;
            
            // Search for this book by title + author
            var searchQuery = $"{item.Title} {item.Authors}";
            _logger.LogInformation($"Fetching metadata for: {searchQuery}");
            
            var results = await _booksService.SearchBooksAsync(searchQuery);
            
            // Find best match (first result with matching title)
            var match = results.FirstOrDefault(r => 
                r.Title.Contains(item.Title, StringComparison.OrdinalIgnoreCase) ||
                item.Title.Contains(r.Title, StringComparison.OrdinalIgnoreCase));
            
            if (match == null && results.Any())
            {
                match = results.First(); // Use first result as fallback
            }

            if (match != null)
            {
                _logger.LogInformation($"Found match: {match.Title}");
                
                // Update missing fields
                if (string.IsNullOrEmpty(item.Description))
                {
                    item.Description = match.FullDescription ?? match.Description;
                    
                    // If still no description, try dedicated description fetch (includes Open Library fallback)
                    if (string.IsNullOrEmpty(item.Description))
                    {
                        _logger.LogInformation($"No description from search, trying dedicated fetch for: {item.Title}");
                        item.Description = await _booksService.GetDescriptionAsync(item.Title, item.Authors);
                    }
                }
                if (!item.PageCount.HasValue && match.PageCount.HasValue)
                    item.PageCount = match.PageCount;
                if (string.IsNullOrEmpty(item.Categories))
                    item.Categories = match.Categories;
                if (string.IsNullOrEmpty(item.Publisher))
                    item.Publisher = match.Publisher;
                if (string.IsNullOrEmpty(item.PublishedDate))
                    item.PublishedDate = match.PublishedDate;
                if (string.IsNullOrEmpty(item.ThumbnailUrl))
                    item.ThumbnailUrl = match.ThumbnailUrl;
                if (string.IsNullOrEmpty(item.GoogleVolumeId))
                    item.GoogleVolumeId = match.Id;
                
                // Try to extract series order from title if not manually set or if strictly default
                var extractedOrder = ExtractSeriesOrder(item.Title);
                if (extractedOrder.HasValue)
                {
                    item.SeriesOrder = extractedOrder.Value;
                }
                    
                updatedCount++;
            }
            else
            {
                // No match found, but still try to get description
                _logger.LogInformation($"No match found, trying dedicated description fetch for: {item.Title}");
                var desc = await _booksService.GetDescriptionAsync(item.Title, item.Authors);
                if (!string.IsNullOrEmpty(desc))
                {
                    item.Description = desc;
                    updatedCount++;
                }

                // Still try to exact order from existing title
                var extractedOrder = ExtractSeriesOrder(item.Title);
                if (extractedOrder.HasValue)
                {
                    item.SeriesOrder = extractedOrder.Value;
                }
            }
        }

        // Fallback Logic: If series order couldn't be determined for some items, sort by release date
        // Check if we have duplicates or zeros in SeriesOrder
        var orders = recommendation.Items.Select(i => i.SeriesOrder).Where(o => o > 0).ToList();
        bool hasMissingOrDuplicateOrders = orders.Count < recommendation.Items.Count || orders.Distinct().Count() < orders.Count;

        if (hasMissingOrDuplicateOrders)
        {
            _logger.LogInformation("Series order incomplete or ambiguous. Falling back to release date sort.");
            
            var sortedItems = recommendation.Items
                .OrderBy(i => 
                {
                    if (DateTime.TryParse(i.PublishedDate, out DateTime date)) return date;
                    if (int.TryParse(i.PublishedDate, out int year)) return new DateTime(year, 1, 1);
                    return DateTime.MaxValue;
                })
                .ToList();

            for (int i = 0; i < sortedItems.Count; i++)
            {
                sortedItems[i].SeriesOrder = i + 1;
            }
            updatedCount += sortedItems.Count; // Mark as updated since we reordered
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation($"Updated {updatedCount} items");

        return new JsonResult(new { 
            success = true, 
            updatedCount = updatedCount,
            message = $"Updated metadata for {updatedCount} book(s)"
        });
    }

    public async Task<IActionResult> OnPostRemoveItemAsync(int itemId)
    {
        var item = await _context.RecommendationItems.FindAsync(itemId);
        if (item == null)
        {
            return NotFound();
        }

        _context.RecommendationItems.Remove(item);
        await _context.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostAddItemAsync(int id, [FromBody] GoogleBookResult book)
    {
        var recommendation = await _context.Recommendations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recommendation == null)
        {
            return NotFound();
        }

        var newItem = new RecommendationItem
        {
            Title = book.Title,
            Authors = book.Authors,
            ThumbnailUrl = book.ThumbnailUrl,
            Description = book.FullDescription ?? book.Description, // Try to capture descriptions if passed
            PageCount = book.PageCount,
            Publisher = book.Publisher,
            PublishedDate = book.PublishedDate,
            Categories = book.Categories,
            GoogleVolumeId = book.Id,
            SeriesOrder = recommendation.Items.Count > 0 ? recommendation.Items.Max(i => i.SeriesOrder) + 1 : 1
        };

        recommendation.Items.Add(newItem);
        await _context.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostReorderItemsAsync([FromBody] List<int> sortedIds)
    {
        var items = await _context.RecommendationItems
            .Where(i => sortedIds.Contains(i.Id))
            .ToListAsync();

        foreach (var item in items)
        {
            var index = sortedIds.IndexOf(item.Id);
            if (index != -1)
            {
                item.SeriesOrder = index + 1;
            }
        }

        await _context.SaveChangesAsync();
        return new JsonResult(new { success = true });
    }

    private int? ExtractSeriesOrder(string title)
    {
        // Patterns: "Title (Series #1)", "Title (Series, Book 1)", "Title, Vol. 1"
        try 
        {
            var match = System.Text.RegularExpressions.Regex.Match(title, @"[#]([0-9]+)|Book\s+([0-9]+)|Vol\.?\s+([0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Find the first capturing group that matched
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success && int.TryParse(match.Groups[i].Value, out int order))
                    {
                        return order;
                    }
                }
            }
        }
        catch { /* Ignore regex errors */ }
        return null;
    }

    // New async endpoint to start series discovery
    public IActionResult OnPostStartDiscoverSeries(int id)
    {
        var processId = Guid.NewGuid().ToString();
        ProcessTracker.TryAdd(processId, new ProcessStatus { Percent = 0, Message = "Starting series discovery...", IsComplete = false });

        // Start background task
        _ = Task.Run(() => RunDiscoverSeriesLogic(processId, id));

        return new JsonResult(new { success = true, processId = processId });
    }

    // Progress polling endpoint (reuses OnGetRefreshProgress)

    // Background logic for series discovery
    private async Task RunDiscoverSeriesLogic(string processId, int recommendationId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var goodreadsService = scope.ServiceProvider.GetRequiredService<GoodreadsService>();

            UpdateRefreshStatus(processId, 5, "Loading recommendation...");

            var recommendation = await context.Recommendations
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == recommendationId);

            if (recommendation == null)
            {
                UpdateRefreshStatus(processId, 0, "Recommendation not found", isError: true);
                return;
            }

            if (!recommendation.IsSeries)
            {
                UpdateRefreshStatus(processId, 0, "This is not a series recommendation", isError: true);
                return;
            }

            UpdateRefreshStatus(processId, 10, "Searching Goodreads...");
            _logger.LogInformation($"Discovering series books for: {recommendation.Title}");

            // Get first book title if available to help with search
            var firstBookTitle = recommendation.Items.OrderBy(i => i.SeriesOrder).FirstOrDefault()?.Title;

            // Fetch series books from Goodreads
            var seriesBooks = await goodreadsService.GetSeriesBooksAsync(recommendation.Title, firstBookTitle);

            if (!seriesBooks.Any())
            {
                UpdateRefreshStatus(processId, 0, "Could not find series information on Goodreads", isError: true);
                return;
            }

            UpdateRefreshStatus(processId, 30, $"Found {seriesBooks.Count} books. Processing...");

            int addedCount = 0;
            int updatedCount = 0;
            int currentBook = 0;
            int totalBooks = seriesBooks.Count;

            foreach (var book in seriesBooks)
            {
                currentBook++;
                int progress = 30 + (int)((double)currentBook / totalBooks * 60); // 30-90%
                UpdateRefreshStatus(processId, progress, $"Processing: {book.Title} ({currentBook}/{totalBooks})");

                // Check if book already exists in the series
                var existingItem = recommendation.Items.FirstOrDefault(i =>
                    i.Title.Equals(book.Title, StringComparison.OrdinalIgnoreCase));

                if (existingItem != null)
                {
                    // Update existing book's order and cover if needed
                    existingItem.SeriesOrder = (int)book.SeriesOrder;
                    existingItem.SeriesSequence = book.SeriesOrder.ToString();
                    if (string.IsNullOrEmpty(existingItem.ThumbnailUrl) && !string.IsNullOrEmpty(book.CoverUrl))
                    {
                        existingItem.ThumbnailUrl = book.CoverUrl;
                    }
                    updatedCount++;
                }
                else
                {
                    // Add new book to the series
                    var newItem = new RecommendationItem
                    {
                        Title = book.Title,
                        Authors = book.Author,
                        ThumbnailUrl = book.CoverUrl,
                        SeriesOrder = (int)book.SeriesOrder,
                        SeriesSequence = book.SeriesOrder.ToString(),
                        Recommendation = recommendation
                    };
                    recommendation.Items.Add(newItem);
                    addedCount++;
                }
            }

            UpdateRefreshStatus(processId, 95, "Saving changes...");
            await context.SaveChangesAsync();

            _logger.LogInformation($"Added {addedCount} books, updated {updatedCount} books for series: {recommendation.Title}");
            UpdateRefreshStatus(processId, 100, $"Complete! Added {addedCount} new books, updated {updatedCount} existing books", isComplete: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering series books");
            UpdateRefreshStatus(processId, 0, $"Error: {ex.Message}", isError: true);
        }
    }

    // Keep old synchronous method for backwards compatibility (deprecated)
    public async Task<IActionResult> OnPostDiscoverSeriesAsync(int id, [FromServices] GoodreadsService goodreadsService)
    {
        var recommendation = await _context.Recommendations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recommendation == null)
        {
            return NotFound();
        }

        if (!recommendation.IsSeries)
        {
            TempData["Error"] = "This is not a series recommendation.";
            return RedirectToPage(new { id });
        }

        try
        {
            _logger.LogInformation($"Discovering series books for: {recommendation.Title}");
            
            // Get first book title if available to help with search
            var firstBookTitle = recommendation.Items.OrderBy(i => i.SeriesOrder).FirstOrDefault()?.Title;
            
            // Fetch series books from Goodreads
            var seriesBooks = await goodreadsService.GetSeriesBooksAsync(recommendation.Title, firstBookTitle);

            if (!seriesBooks.Any())
            {
                TempData["Error"] = "Could not find series information on Goodreads. Try adding books manually.";
                return RedirectToPage(new { id });
            }

            int addedCount = 0;
            int updatedCount = 0;

            foreach (var book in seriesBooks)
            {
                // Check if book already exists in the series
                var existingItem = recommendation.Items.FirstOrDefault(i => 
                    i.Title.Equals(book.Title, StringComparison.OrdinalIgnoreCase));

                if (existingItem != null)
                {
                    // Update existing book's order and cover if needed
                    existingItem.SeriesOrder = (int)book.SeriesOrder;
                    existingItem.SeriesSequence = book.SeriesOrder.ToString();
                    if (string.IsNullOrEmpty(existingItem.ThumbnailUrl) && !string.IsNullOrEmpty(book.CoverUrl))
                    {
                        existingItem.ThumbnailUrl = book.CoverUrl;
                    }
                    updatedCount++;
                }
                else
                {
                    // Add new book to the series
                    var newItem = new RecommendationItem
                    {
                        Title = book.Title,
                        Authors = book.Author,
                        ThumbnailUrl = book.CoverUrl,
                        SeriesOrder = (int)book.SeriesOrder,
                        SeriesSequence = book.SeriesOrder.ToString(),
                        Recommendation = recommendation
                    };
                    recommendation.Items.Add(newItem);
                    addedCount++;
                }
            }

    
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Series discovery complete! Added {addedCount} new books, updated {updatedCount} existing books.";
        _logger.LogInformation($"Added {addedCount} books, updated {updatedCount} books for series: {recommendation.Title}");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error discovering series books");
        TempData["Error"] = "An error occurred while discovering series books. Please try again.";
    }

    return RedirectToPage(new { id });
}

public async Task<IActionResult> OnPostBulkEditAsync(int id, string bookIds, string? authors, string? categories, string? narrator, string? publisher, string? publishedYear, string? language)
{
    if (string.IsNullOrWhiteSpace(bookIds))
    {
        TempData["Error"] = "No books selected for bulk edit.";
        return RedirectToPage(new { id });
    }

    try
    {
        var ids = bookIds.Split(',').Select(int.Parse).ToList();
        var books = await _context.RecommendationItems
            .Where(b => ids.Contains(b.Id))
            .ToListAsync();

        if (!books.Any())
        {
            TempData["Error"] = "No books found with the selected IDs.";
            return RedirectToPage(new { id });
        }

        int updatedCount = 0;

        foreach (var book in books)
        {
            bool updated = false;

            if (!string.IsNullOrWhiteSpace(authors))
            {
                book.Authors = authors.Trim();
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(categories))
            {
                book.Categories = categories.Trim();
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(narrator))
            {
                book.Narrator = narrator.Trim();
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(publisher))
            {
                book.Publisher = publisher.Trim();
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(publishedYear))
            {
                book.PublishedDate = publishedYear.Trim();
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(language))
            {
                book.Language = language.Trim();
                updated = true;
            }

            if (updated)
            {
                updatedCount++;
            }
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Successfully updated {updatedCount} book(s).";
        _logger.LogInformation($"Bulk edited {updatedCount} books in series ID {id}");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during bulk edit");
        TempData["Error"] = "An error occurred while updating books. Please try again.";
    }

    return RedirectToPage(new { id });
}

public async Task<IActionResult> OnPostBulkRemoveAsync(int id, string bookIds)
{
    if (string.IsNullOrWhiteSpace(bookIds))
    {
        TempData["Error"] = "No books selected for removal.";
        return RedirectToPage(new { id });
    }

    try
    {
        var ids = bookIds.Split(',').Select(int.Parse).ToList();
        var books = await _context.RecommendationItems
            .Where(b => ids.Contains(b.Id))
            .ToListAsync();

        if (!books.Any())
        {
            TempData["Error"] = "No books found with the selected IDs.";
            return RedirectToPage(new { id });
        }

        int removedCount = books.Count;
        _context.RecommendationItems.RemoveRange(books);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Successfully removed {removedCount} book(s) from the series.";
        _logger.LogInformation($"Bulk removed {removedCount} books from series ID {id}");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during bulk remove");
        TempData["Error"] = "An error occurred while removing books. Please try again.";
    }

    return RedirectToPage(new { id });
}
}
