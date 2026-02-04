using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace OpenShelf.Pages.Admin;

[Authorize]
public class RestoreModel : PageModel
{
    private readonly AppDbContext _context;

    public RestoreModel(AppDbContext context)
    {
        _context = context;
    }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";
    public List<string> ValidationResults { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFile backupFile, bool clearExisting = false)
    {
        if (backupFile == null || backupFile.Length == 0)
        {
            Message = "Please select a backup file.";
            MessageType = "danger";
            return Page();
        }

        if (!backupFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            Message = "Please upload a CSV file.";
            MessageType = "danger";
            return Page();
        }

        try
        {
            using var reader = new StreamReader(backupFile.OpenReadStream());
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            });

            var records = csv.GetRecords<FullExportRecord>().ToList();

            if (!records.Any())
            {
                Message = "The backup file is empty.";
                MessageType = "warning";
                return Page();
            }

            // Clear existing data if requested
            if (clearExisting)
            {
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Comments");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM RecommendationItems");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Recommendations");
                ValidationResults.Add("✓ Cleared all existing data");
            }

            // Group records by Recommendation ID
            var groupedByRec = records.GroupBy(r => r.Rec_Id);

            int recsAdded = 0;
            int itemsAdded = 0;
            int commentsAdded = 0;

            foreach (var group in groupedByRec)
            {
                var firstRecord = group.First();
                
                // Check if recommendation already exists
                var existingRec = await _context.Recommendations
                    .Include(r => r.Items)
                    .Include(r => r.Comments)
                    .FirstOrDefaultAsync(r => r.Id == firstRecord.Rec_Id);

                Recommendation rec;
                if (existingRec == null)
                {
                    // Create new recommendation
                    rec = new Recommendation
                    {
                        Id = firstRecord.Rec_Id,
                        Title = firstRecord.Rec_Title,
                        RecommendedBy = firstRecord.Rec_RecommendedBy,
                        Note = firstRecord.Rec_Note,
                        AddedAt = firstRecord.Rec_AddedAt,
                        Likes = firstRecord.Rec_Likes,
                        SeriesDescription = firstRecord.Rec_SeriesDescription
                    };
                    _context.Recommendations.Add(rec);
                    recsAdded++;
                }
                else
                {
                    rec = existingRec;
                    // Update existing recommendation
                    rec.Title = firstRecord.Rec_Title;
                    rec.RecommendedBy = firstRecord.Rec_RecommendedBy;
                    rec.Note = firstRecord.Rec_Note;
                    rec.AddedAt = firstRecord.Rec_AddedAt;
                    rec.Likes = firstRecord.Rec_Likes;
                    rec.SeriesDescription = firstRecord.Rec_SeriesDescription;
                }

                // Add/update items
                foreach (var record in group)
                {
                    if (record.Item_Id.HasValue)
                    {
                        var existingItem = rec.Items.FirstOrDefault(i => i.Id == record.Item_Id.Value);
                        
                        if (existingItem == null)
                        {
                            var item = new RecommendationItem
                            {
                                Id = record.Item_Id.Value,
                                RecommendationId = rec.Id,
                                Title = record.Item_Title ?? "",
                                Authors = record.Item_Authors ?? "",
                                ThumbnailUrl = record.Item_ThumbnailUrl,
                                GoogleVolumeId = record.Item_GoogleVolumeId,
                                Description = record.Item_Description,
                                PageCount = record.Item_PageCount,
                                Narrator = record.Item_Narrator,
                                ListeningLength = record.Item_ListeningLength,
                                Categories = record.Item_Categories,
                                Publisher = record.Item_Publisher,
                                PublishedDate = record.Item_PublishedDate,
                                PurchaseDate = record.Item_PurchaseDate,
                                ReleaseDate = record.Item_ReleaseDate,
                                AverageRating = record.Item_AverageRating,
                                RatingCount = record.Item_RatingCount,
                                SeriesName = record.Item_SeriesName,
                                SeriesSequence = record.Item_SeriesSequence,
                                ProductId = record.Item_ProductId,
                                Asin = record.Item_Asin,
                                BookUrl = record.Item_BookUrl,
                                SeriesUrl = record.Item_SeriesUrl,
                                Abridged = record.Item_Abridged,
                                Language = record.Item_Language,
                                Copyright = record.Item_Copyright,
                                SeriesOrder = record.Item_SeriesOrder
                            };
                            rec.Items.Add(item);
                            itemsAdded++;
                        }
                        else
                        {
                            // Update existing item
                            existingItem.Title = record.Item_Title ?? "";
                            existingItem.Authors = record.Item_Authors ?? "";
                            existingItem.ThumbnailUrl = record.Item_ThumbnailUrl;
                            existingItem.GoogleVolumeId = record.Item_GoogleVolumeId;
                            existingItem.Description = record.Item_Description;
                            existingItem.PageCount = record.Item_PageCount;
                            existingItem.Narrator = record.Item_Narrator;
                            existingItem.ListeningLength = record.Item_ListeningLength;
                            existingItem.Categories = record.Item_Categories;
                            existingItem.Publisher = record.Item_Publisher;
                            existingItem.PublishedDate = record.Item_PublishedDate;
                            existingItem.PurchaseDate = record.Item_PurchaseDate;
                            existingItem.ReleaseDate = record.Item_ReleaseDate;
                            existingItem.AverageRating = record.Item_AverageRating;
                            existingItem.RatingCount = record.Item_RatingCount;
                            existingItem.SeriesName = record.Item_SeriesName;
                            existingItem.SeriesSequence = record.Item_SeriesSequence;
                            existingItem.ProductId = record.Item_ProductId;
                            existingItem.Asin = record.Item_Asin;
                            existingItem.BookUrl = record.Item_BookUrl;
                            existingItem.SeriesUrl = record.Item_SeriesUrl;
                            existingItem.Abridged = record.Item_Abridged;
                            existingItem.Language = record.Item_Language;
                            existingItem.Copyright = record.Item_Copyright;
                            existingItem.SeriesOrder = record.Item_SeriesOrder;
                        }
                    }
                }

                // Parse and add comments (only from first record since they're the same for all items)
                if (!string.IsNullOrEmpty(firstRecord.Comments))
                {
                    var parsedComments = ParseComments(firstRecord.Comments);
                    foreach (var commentData in parsedComments)
                    {
                        var existingComment = rec.Comments.FirstOrDefault(c => 
                            c.Author == commentData.Author && 
                            c.Text == commentData.Text && 
                            c.CreatedAt == commentData.CreatedAt);
                        
                        if (existingComment == null)
                        {
                            rec.Comments.Add(new Comment
                            {
                                Author = commentData.Author,
                                Text = commentData.Text,
                                CreatedAt = commentData.CreatedAt,
                                RecommendationId = rec.Id
                            });
                            commentsAdded++;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            ValidationResults.Add($"✓ Processed {groupedByRec.Count()} recommendation(s)");
            ValidationResults.Add($"✓ Added/Updated {recsAdded} recommendation(s)");
            ValidationResults.Add($"✓ Added/Updated {itemsAdded} item(s)");
            ValidationResults.Add($"✓ Added {commentsAdded} comment(s)");
            
            Message = "Backup restored successfully!";
            MessageType = "success";
        }
        catch (Exception ex)
        {
            Message = $"Error restoring backup: {ex.Message}";
            MessageType = "danger";
            ValidationResults.Add($"✗ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                ValidationResults.Add($"✗ Inner Error: {ex.InnerException.Message}");
            }
        }

        return Page();
    }

    private List<(string Author, string Text, DateTime CreatedAt)> ParseComments(string commentsField)
    {
        var result = new List<(string, string, DateTime)>();
        
        if (string.IsNullOrEmpty(commentsField)) return result;

        // Format: Author1:Text1:Date1|Author2:Text2:Date2
        var commentParts = commentsField.Split('|');
        
        foreach (var part in commentParts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            
            var fields = part.Split(':');
            if (fields.Length >= 3)
            {
                var author = UnescapeCommentField(fields[0]);
                var text = UnescapeCommentField(fields[1]);
                var dateStr = fields[2];
                
                if (DateTime.TryParse(dateStr, null, DateTimeStyles.RoundtripKind, out var createdAt))
                {
                    result.Add((author, text, createdAt));
                }
            }
        }
        
        return result;
    }

    private string UnescapeCommentField(string field)
    {
        return field?.Replace("\\:", ":").Replace("\\|", "|") ?? "";
    }

    // Same class as in AdminIndexModel
    public class FullExportRecord
    {
        public int Rec_Id { get; set; }
        public string Rec_Title { get; set; } = "";
        public string Rec_RecommendedBy { get; set; } = "";
        public string? Rec_Note { get; set; }
        public DateTime Rec_AddedAt { get; set; }
        public int Rec_Likes { get; set; }
        public string? Rec_SeriesDescription { get; set; }
        
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
        
        public string? Comments { get; set; }
    }
}
