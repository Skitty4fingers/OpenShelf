using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;
using OpenShelf.Services;
using System.Globalization;
using System.IO;
using CsvHelper;

namespace OpenShelf.Pages;

[Authorize]
public class AdminIndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly GoogleBooksService _googleService;
    private readonly AudibleService _audibleService;

    public AdminIndexModel(AppDbContext context, GoogleBooksService googleService, AudibleService audibleService)
    {
        _context = context;
        _googleService = googleService;
        _audibleService = audibleService;
    }

    public List<RecommendationViewModel> Recommendations { get; set; } = new();

    public string? Message { get; set; }
    public string MessageType { get; set; }

    public async Task OnGetAsync()
    {
        var recs = await _context.Recommendations
            .Include(r => r.Items)
            .OrderByDescending(r => r.AddedAt)
            .ToListAsync();

        Recommendations = recs.Select(r => new RecommendationViewModel
        {
            Id = r.Id,
            Title = r.Title,
            IsSeries = r.IsSeries,
            ItemsCount = r.Items.Count,
            RecommendedBy = r.RecommendedBy,
            AddedAt = r.AddedAt,
            Likes = r.Likes,
            Thumbnail = r.IsSeries 
                ? r.Items.FirstOrDefault()?.ThumbnailUrl 
                : r.Items.FirstOrDefault()?.ThumbnailUrl,
            IsStaffPick = r.IsStaffPick
        }).ToList();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync("CookieAuth");
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostBulkDeleteAsync(string selectedIds)
    {
        if (string.IsNullOrEmpty(selectedIds))
        {
             Message = "No items selected.";
             MessageType = "warning";
             await OnGetAsync();
             return Page();
        }

        var ids = selectedIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(id => int.TryParse(id, out var i) ? i : 0)
                           .Where(i => i > 0)
                           .ToList();

        if (!ids.Any()) 
        {
             Message = "No items selected.";
             MessageType = "warning";
             await OnGetAsync();
             return Page();
        }

        var toDelete = await _context.Recommendations
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();

        _context.Recommendations.RemoveRange(toDelete);
        await _context.SaveChangesAsync();

        Message = $"Deleted {toDelete.Count} items.";
        MessageType = "success";
        
        await OnGetAsync();
        return Page();
    }
    
    public async Task<IActionResult> OnPostBulkRefreshAsync(string selectedIds)
    {
         if (string.IsNullOrEmpty(selectedIds)) return RedirectToPage();

         var ids = selectedIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(id => int.TryParse(id, out var i) ? i : 0)
                           .Where(i => i > 0)
                           .ToList();

         if (!ids.Any()) return RedirectToPage();

         var recs = await _context.Recommendations
            .Include(r => r.Items)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();

         int updated = 0;
         foreach (var rec in recs)
         {
             foreach (var item in rec.Items)
             {
                 try 
                 {
                    var q = $"{item.Title} {item.Authors}";
                    var books = await _googleService.SearchBooksAsync(q);
                    var match = books.FirstOrDefault();
                    if (match != null)
                    {
                        if (!string.IsNullOrEmpty(match.ThumbnailUrl)) item.ThumbnailUrl = match.ThumbnailUrl;
                        if (string.IsNullOrEmpty(item.Description)) item.Description = match.Description;
                        if (string.IsNullOrEmpty(item.Publisher)) item.Publisher = match.Publisher;
                        if (string.IsNullOrEmpty(item.PublishedDate)) item.PublishedDate = match.PublishedDate;
                        if (!item.PageCount.HasValue) item.PageCount = match.PageCount;
                        if (string.IsNullOrEmpty(item.Categories)) item.Categories = match.Categories;
                        updated++;
                    }
                    
                    // Try Goodreads for cover image if Google Books didn't have one
                    if (string.IsNullOrEmpty(item.ThumbnailUrl))
                    {
                        try
                        {
                            var goodreadsService = HttpContext.RequestServices.GetRequiredService<GoodreadsService>();
                            var (goodreadsCover, goodreadsDesc, _) = await goodreadsService.SearchAndGetMetadataAsync(item.Title, item.Authors);
                            if (!string.IsNullOrEmpty(goodreadsCover))
                            {
                                item.ThumbnailUrl = goodreadsCover;
                            }
                            // Also use Goodreads description if we don't have one
                            if (string.IsNullOrEmpty(item.Description) && !string.IsNullOrEmpty(goodreadsDesc))
                            {
                                item.Description = goodreadsDesc;
                            }
                        }
                        catch { }
                    }
                    
                    // Try Audible for narrator, listening length, and cover image fallback
                    try
                    {
                        var (narrator, length, _, coverImage) = await _audibleService.SearchAndGetMetadataAsync(item.Title, item.Authors);
                        if (!string.IsNullOrEmpty(narrator)) item.Narrator = narrator;
                        if (!string.IsNullOrEmpty(length)) item.ListeningLength = length;
                        // Use Amazon cover as fallback if Google Books and Goodreads didn't provide one
                        if (string.IsNullOrEmpty(item.ThumbnailUrl) && !string.IsNullOrEmpty(coverImage))
                        {
                            item.ThumbnailUrl = coverImage;
                        }
                    }
                    catch { }
                 } catch {}
             }
         }
         await _context.SaveChangesAsync();
         
         Message = $"Refreshed metadata for {recs.Count} recommendations ({updated} items updated).";
         MessageType = "success";

         await OnGetAsync();
         return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        var recs = await _context.Recommendations
            .Include(r => r.Items)
            .Include(r => r.Comments)
            .OrderBy(r => r.Title)
            .ToListAsync();
            
        var exportData = new List<FullExportRecord>();
        foreach(var rec in recs)
        {
            if (rec.Items.Any())
            {
                foreach(var item in rec.Items)
                {
                    exportData.Add(MapToFullExport(rec, item));
                }
            }
            else
            {
                // Empty recommendation (no items)
                exportData.Add(new FullExportRecord
                {
                    Rec_Id = rec.Id,
                    Rec_Title = rec.Title,
                    Rec_RecommendedBy = rec.RecommendedBy,
                    Rec_Note = rec.Note,
                    Rec_AddedAt = rec.AddedAt,
                    Rec_Likes = rec.Likes,
                    Rec_SeriesDescription = rec.SeriesDescription,
                    Comments = FormatComments(rec.Comments)
                });
            }
        }

        using var memory = new MemoryStream();
        using var writer = new StreamWriter(memory);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        
        csv.WriteRecords(exportData);
        writer.Flush();
        return File(memory.ToArray(), "text/csv", $"OpenShelf_FullBackup_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    private FullExportRecord MapToFullExport(Recommendation rec, RecommendationItem item)
    {
        return new FullExportRecord
        {
            // Recommendation fields
            Rec_Id = rec.Id,
            Rec_Title = rec.Title,
            Rec_RecommendedBy = rec.RecommendedBy,
            Rec_Note = rec.Note,
            Rec_AddedAt = rec.AddedAt,
            Rec_Likes = rec.Likes,
            Rec_SeriesDescription = rec.SeriesDescription,
            
            // RecommendationItem fields
            Item_Id = item.Id,
            Item_Title = item.Title,
            Item_Authors = item.Authors,
            Item_ThumbnailUrl = item.ThumbnailUrl,
            Item_GoogleVolumeId = item.GoogleVolumeId,
            Item_Description = item.Description,
            Item_PageCount = item.PageCount,
            Item_Narrator = item.Narrator,
            Item_ListeningLength = item.ListeningLength,
            Item_Categories = item.Categories,
            Item_Publisher = item.Publisher,
            Item_PublishedDate = item.PublishedDate,
            Item_PurchaseDate = item.PurchaseDate,
            Item_ReleaseDate = item.ReleaseDate,
            Item_AverageRating = item.AverageRating,
            Item_RatingCount = item.RatingCount,
            Item_SeriesName = item.SeriesName,
            Item_SeriesSequence = item.SeriesSequence,
            Item_ProductId = item.ProductId,
            Item_Asin = item.Asin,
            Item_BookUrl = item.BookUrl,
            Item_SeriesUrl = item.SeriesUrl,
            Item_Abridged = item.Abridged,
            Item_Language = item.Language,
            Item_Copyright = item.Copyright,
            Item_SeriesOrder = item.SeriesOrder,
            
            // Comments (pipe-separated)
            Comments = FormatComments(rec.Comments)
        };
    }

    private string? FormatComments(List<Comment> comments)
    {
        if (comments == null || !comments.Any()) return null;
        
        // Format: Author1:Text1:Date1|Author2:Text2:Date2
        return string.Join("|", comments.Select(c => 
            $"{EscapeCommentField(c.Author)}:{EscapeCommentField(c.Text)}:{c.CreatedAt:O}"));
    }

    private string EscapeCommentField(string field)
    {
        // Escape colons and pipes in comment fields
        return field?.Replace(":", "\\:").Replace("|", "\\|") ?? "";
    }

    public class FullExportRecord
    {
        // Recommendation fields
        public int Rec_Id { get; set; }
        public string Rec_Title { get; set; } = "";
        public string Rec_RecommendedBy { get; set; } = "";
        public string? Rec_Note { get; set; }
        public DateTime Rec_AddedAt { get; set; }
        public int Rec_Likes { get; set; }
        public string? Rec_SeriesDescription { get; set; }
        
        // RecommendationItem fields
        public int? Item_Id { get; set; }
        public string? Item_Title { get; set; }
        public string? Item_Authors { get; set; }
        public string? Item_ThumbnailUrl { get; set; }
        public string? Item_GoogleVolumeId { get; set; }
        public string? Item_Description { get; set; }
        public int? Item_PageCount { get; set; }
        public string? Item_Narrator { get; set; }
        public string? Item_ListeningLength { get; set; }
        public string? Item_Categories { get; set; }
        public string? Item_Publisher { get; set; }
        public string? Item_PublishedDate { get; set; }
        public string? Item_PurchaseDate { get; set; }
        public string? Item_ReleaseDate { get; set; }
        public string? Item_AverageRating { get; set; }
        public string? Item_RatingCount { get; set; }
        public string? Item_SeriesName { get; set; }
        public string? Item_SeriesSequence { get; set; }
        public string? Item_ProductId { get; set; }
        public string? Item_Asin { get; set; }
        public string? Item_BookUrl { get; set; }
        public string? Item_SeriesUrl { get; set; }
        public string? Item_Abridged { get; set; }
        public string? Item_Language { get; set; }
        public string? Item_Copyright { get; set; }
        public int? Item_SeriesOrder { get; set; }
        
        // Comments (pipe-separated)
        public string? Comments { get; set; }
    }

    // New Bulk Update Handler
    public async Task<IActionResult> OnPostBulkUpdateAsync(string updateIds, string? newRecommender, string? newCategory)
    {
        if (string.IsNullOrEmpty(updateIds)) return RedirectToPage();

        var ids = updateIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(id => int.TryParse(id, out var i) ? i : 0)
                           .Where(i => i > 0)
                           .ToList();

        if (!ids.Any()) return RedirectToPage();

        var recs = await _context.Recommendations
            .Include(r => r.Items)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();

        int count = 0;
        foreach (var rec in recs)
        {
            if (!string.IsNullOrWhiteSpace(newRecommender))
            {
                rec.RecommendedBy = newRecommender;
            }
            
            if (!string.IsNullOrWhiteSpace(newCategory))
            {
                foreach(var item in rec.Items)
                {
                    item.Categories = newCategory;
                }
            }
            count++;
        }

        await _context.SaveChangesAsync();
        Message = $"Updated {count} items.";
        MessageType = "success";
        
        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostReSanitizeAsync()
    {
        try
        {
            var allItems = await _context.RecommendationItems.ToListAsync();
            int updatedCount = 0;

            foreach (var item in allItems)
            {
                bool wasUpdated = false;

                if (!string.IsNullOrEmpty(item.Title))
                {
                    var cleanedTitle = CleanString(item.Title);
                    if (cleanedTitle != item.Title)
                    {
                        item.Title = cleanedTitle;
                        wasUpdated = true;
                    }
                }

                if (!string.IsNullOrEmpty(item.Authors))
                {
                    var cleanedAuthors = CleanString(item.Authors);
                    if (cleanedAuthors != item.Authors)
                    {
                        item.Authors = cleanedAuthors;
                        wasUpdated = true;
                    }
                }

                if (!string.IsNullOrEmpty(item.Description))
                {
                    var cleanedDesc = CleanString(item.Description);
                    if (cleanedDesc != item.Description)
                    {
                        item.Description = cleanedDesc;
                        wasUpdated = true;
                    }
                }

                if (!string.IsNullOrEmpty(item.Publisher))
                {
                    var cleanedPublisher = CleanString(item.Publisher);
                    if (cleanedPublisher != item.Publisher)
                    {
                        item.Publisher = cleanedPublisher;
                        wasUpdated = true;
                    }
                }

                if (!string.IsNullOrEmpty(item.Narrator))
                {
                    var cleanedNarrator = CleanString(item.Narrator);
                    if (cleanedNarrator != item.Narrator)
                    {
                        item.Narrator = cleanedNarrator;
                        wasUpdated = true;
                    }
                }

                if (!string.IsNullOrEmpty(item.Categories))
                {
                    var cleanedCategories = CleanString(item.Categories);
                    if (cleanedCategories != item.Categories)
                    {
                        item.Categories = cleanedCategories;
                        wasUpdated = true;
                    }
                }

                if (wasUpdated)
                {
                    updatedCount++;
                }
            }

            await _context.SaveChangesAsync();

            Message = $"Successfully re-sanitized {updatedCount} book(s)!";
            MessageType = "success";
        }
        catch (Exception ex)
        {
            Message = $"Error re-sanitizing data: {ex.Message}";
            MessageType = "danger";
        }

        return RedirectToPage();
    }

    private string CleanString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var decoded = System.Net.WebUtility.HtmlDecode(input);
        var stripped = System.Text.RegularExpressions.Regex.Replace(decoded, @"<[^>]+>", "");
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim();

        return stripped;
    }

    public class RecommendationViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public bool IsSeries { get; set; }
        public int ItemsCount { get; set; }
        public string RecommendedBy { get; set; } = "";
        public DateTime AddedAt { get; set; }
        public int Likes { get; set; }
        public string? Thumbnail { get; set; }
        public bool IsStaffPick { get; set; }
    }

    public async Task<IActionResult> OnPostToggleStaffPickAsync(int id)
    {
        var rec = await _context.Recommendations.FindAsync(id);
        if (rec != null)
        {
            if (!rec.IsStaffPick)
            {
                // Turning ON - clear all other staff picks first (only 1 allowed)
                var existingPicks = await _context.Recommendations
                    .Where(r => r.IsStaffPick && r.Id != id)
                    .ToListAsync();
                
                foreach (var pick in existingPicks)
                {
                    pick.IsStaffPick = false;
                }
                
                rec.IsStaffPick = true;
                Message = $"'{rec.Title}' is now the Staff Pick.";
            }
            else
            {
                // Turning OFF
                rec.IsStaffPick = false;
                Message = "Staff Pick removed.";
            }
            
            await _context.SaveChangesAsync();
            MessageType = "success";
        }
        return RedirectToPage();
    }
}
