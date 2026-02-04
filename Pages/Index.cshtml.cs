using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;
using OpenShelf.Services;

namespace OpenShelf.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(AppDbContext context, ILogger<IndexModel> logger)
    {
        _context = context;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "default"; // "default" (4-wide) or "mini" (8-wide)

    [BindProperty(SupportsGet = true)]
    public string SearchQuery { get; set; } // Fuzzy find by title/author/series

    public IList<Recommendation> Recommendations { get; set; } = default!;
    
    // Filter/Sort parameters
    [BindProperty(SupportsGet = true)]
    public string? Genre { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public string? Recommender { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Narrator { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "recent";

    // For filter dropdowns
    public List<string> AllGenres { get; set; } = new();
    public List<string> AllRecommenders { get; set; } = new();
    public List<string> AllNarrators { get; set; } = new();

    public Recommendation? HighestRatedRecommendation { get; set; }
    public Recommendation? StaffPickRecommendation { get; set; }

    public async Task OnGetAsync()
    {
        var query = _context.Recommendations
            .Include(r => r.Items)
            .Include(r => r.Comments)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(Genre))
        {
            query = query.Where(r => r.Items.Any(i => i.Categories != null && i.Categories.Contains(Genre)));
        }

        if (!string.IsNullOrEmpty(Recommender))
        {
            query = query.Where(r => r.RecommendedBy == Recommender);
        }

        if (!string.IsNullOrEmpty(SearchQuery))
        {
            var term = SearchQuery.ToLower();
            // Search in Title, Series Description, or Items (Title, Authors, SeriesName)
            query = query.Where(r => 
                r.Title.ToLower().Contains(term) || 
                r.SeriesDescription.ToLower().Contains(term) ||
                r.Items.Any(i => i.Title.ToLower().Contains(term) || 
                                 i.Authors.ToLower().Contains(term) || 
                                 (i.SeriesName != null && i.SeriesName.ToLower().Contains(term)))
            );
        }

        if (!string.IsNullOrEmpty(Narrator))
        {
            // Use StartsWith because the dropdown value might be truncated (first 3 names)
            query = query.Where(r => r.Items.Any(i => i.Narrator != null && i.Narrator.StartsWith(Narrator)));
        }

        // Apply sorting
        query = Sort switch
        {
            "popular" => query.OrderByDescending(r => r.Likes).ThenByDescending(r => r.AddedAt),
            "title" => query.OrderBy(r => r.Title),
            "author" => query.OrderBy(r => r.Items.Min(i => i.Authors)),
            "year" => query.OrderByDescending(r => r.Items.Max(i => i.PublishedDate)),
            "length" => query.OrderByDescending(r => r.Items.Sum(i => i.PageCount ?? 0)),
            _ => query.OrderByDescending(r => r.AddedAt)
        };

        Recommendations = await query.ToListAsync();
        
        _logger.LogInformation($"Index Page: Found {Recommendations.Count} recommendations in DB.");

        // Populate filter options
        AllRecommenders = await _context.Recommendations
            .Select(r => r.RecommendedBy)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync();

        var rawNarrators = await _context.RecommendationItems
            .Where(i => !string.IsNullOrEmpty(i.Narrator))
            .Select(i => i.Narrator)
            .Distinct()
            .ToListAsync();

        // Process narrators in memory to trim to first 3 names if list is long
        AllNarrators = rawNarrators
            .Select(n => {
                var parts = n!.Split(',').Select(p => p.Trim()).ToList();
                if (parts.Count > 3)
                {
                    return string.Join(", ", parts.Take(3));
                }
                return n;
            })
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        AllGenres = await _context.RecommendationItems
            .Where(i => i.Categories != null)
            .Select(i => i.Categories!)
            .ToListAsync();

        // Flatten and deduplicate genres
        AllGenres = AllGenres
            .SelectMany(c => c.Split(','))
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // --- Highlights Logic ---
        

        // --- Highlights Logic ---
        
        // 1. Highest Rated (Most User Likes/Thumbs Up in Current Month)
        // Since we just added the tracking table, we might not have data yet. 
        // Fallback to total likes if no monthly data exists? 
        // User requested "Highest Rated in (current Month) look at only current month thumbs".
        // Use RecommendationLike table.
        
        var currentMonth = DateTime.UtcNow.Month;
        var currentYear = DateTime.UtcNow.Year;

        // Get ID of recommendation with most likes this month
        var trendingId = await _context.RecommendationLikes
            .Where(l => l.CreatedAt.Month == currentMonth && l.CreatedAt.Year == currentYear)
            .GroupBy(l => l.RecommendationId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        if (trendingId > 0)
        {
             HighestRatedRecommendation = await _context.Recommendations
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == trendingId);
             
             // Populate transient likes count for display if we want to show *monthly* likes? 
             // Or just total likes? User said "look at only current month thumbs" for SELECTION.
             // Display usually shows total likes, but maybe we should show monthly count?
             // Let's stick to showing total likes on the card, but selecting by monthly trend.
        }
        else
        {
            // Fallback: If no likes this month, maybe show all-time highest? 
            // Or show nothing? "Highest Rated in February" implies specific timeframe.
            // Let's show all-time highest as fallback so the UI isn't empty, 
            // OR maybe the user wants it empty? 
            // "look at only current month thumbs" suggests strict filtering.
            // But for now, let's keep the all-time fallback but MAYBE label it differently?
            // Actually, let's stick to the STRICT interpretation: Only current month likes determine the winner.
            // If no likes, then HighestRatedRecommendation is null (section hidden).
            // BUT, to avoid an empty dashboard initially, I'll fallback to "Most Recent" or "All Time" but maybe that defeats the purpose?
            // I'll assume fallback to All-Time is better than empty space.
            
             HighestRatedRecommendation = await _context.Recommendations
                .Include(r => r.Items)
                .OrderByDescending(r => r.Likes)
                .ThenByDescending(r => r.AddedAt)
                .FirstOrDefaultAsync();
        }

        // 2. Staff Pick (Explicitly selected by Admin)
        // 2. Staff Pick (Explicitly selected by Admin)
        // User requested: "allow the highest rated and staff pick to be the same"
        
        StaffPickRecommendation = await _context.Recommendations
            .Include(r => r.Items)
            .Where(r => r.IsStaffPick)
            .OrderBy(r => EF.Functions.Random())
            .FirstOrDefaultAsync();
            
        // Fallback if no staff picks selected yet: Do NOT show a random one.
        // User requested: "if no staff picks selected dont show the highlight card"
        if (StaffPickRecommendation == null)
        {
             // Do nothing - StaffPickRecommendation remains null, leaving the UI section empty/hidden
        }
    }

    public async Task<IActionResult> OnPostLikeAsync(int id)
    {
        var recommendation = await _context.Recommendations.FindAsync(id);
        if (recommendation != null)
        {
            recommendation.Likes++;
            
            // Track history
            _context.RecommendationLikes.Add(new RecommendationLike 
            { 
                RecommendationId = id,
                CreatedAt = DateTime.UtcNow
            });
            
            await _context.SaveChangesAsync();
            return new JsonResult(new { likes = recommendation.Likes });
        }
        return NotFound();
    }

    public async Task<IActionResult> OnGetCommentsAsync(int id)
    {
        var comments = await _context.Comments
            .Where(c => c.RecommendationId == id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return new JsonResult(comments);
    }

    [BindProperty]
    public CommentInputModel NewComment { get; set; } = new();

    public class CommentInputModel
    {
        public int RecommendationId { get; set; }
        public string Author { get; set; } = "Anonymous";
        public string Text { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAddCommentAsync()
    {
        if (NewComment.RecommendationId == 0 || string.IsNullOrWhiteSpace(NewComment.Text))
            return BadRequest("Invalid comment data");

        var rec = await _context.Recommendations.FindAsync(NewComment.RecommendationId);
        if (rec == null) return NotFound();

        var comment = new Comment
        {
            RecommendationId = NewComment.RecommendationId,
            Author = string.IsNullOrWhiteSpace(NewComment.Author) ? "Anonymous" : NewComment.Author,
            Text = NewComment.Text,
            CreatedAt = DateTime.UtcNow
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        return new JsonResult(comment);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var rec = await _context.Recommendations.FindAsync(id);
        if (rec != null)
        {
            _context.Recommendations.Remove(rec);
            await _context.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshAsync(int id, [FromServices] GoogleBooksService booksService, [FromServices] AudibleService audibleService)
    {
        var rec = await _context.Recommendations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rec == null) return NotFound();

        foreach (var item in rec.Items)
        {
            // Use precision search "Title by Author" to find better data
            var query = $"{item.Title} by {item.Authors}";
            var results = await booksService.SearchBooksAsync(query);
            var bestMatch = results.FirstOrDefault();

            if (bestMatch != null)
            {
                // Update missing fields
                if (string.IsNullOrEmpty(item.Description)) item.Description = bestMatch.Description;
                if (string.IsNullOrEmpty(item.ThumbnailUrl)) item.ThumbnailUrl = bestMatch.ThumbnailUrl;
                if (string.IsNullOrEmpty(item.PublishedDate)) item.PublishedDate = bestMatch.PublishedDate;
                if (string.IsNullOrEmpty(item.GoogleVolumeId)) item.GoogleVolumeId = bestMatch.Id;
                if (!item.PageCount.HasValue) item.PageCount = bestMatch.PageCount;
                if (string.IsNullOrEmpty(item.GoogleVolumeId)) item.GoogleVolumeId = bestMatch.Id;
                if (!item.PageCount.HasValue) item.PageCount = bestMatch.PageCount;
            }

            // Audiobook Metadata
            try 
            {
                var (narrator, length, _, coverImage) = await audibleService.SearchAndGetMetadataAsync(item.Title, item.Authors);
                if (!string.IsNullOrEmpty(narrator)) item.Narrator = narrator;
                if (!string.IsNullOrEmpty(length)) item.ListeningLength = length;
                // Use Amazon cover as fallback if Google Books didn't provide one
                if (string.IsNullOrEmpty(item.ThumbnailUrl) && !string.IsNullOrEmpty(coverImage))
                {
                    item.ThumbnailUrl = coverImage;
                }
            }
            catch {}
        }
        await _context.SaveChangesAsync();
        return RedirectToPage();
    }
}
