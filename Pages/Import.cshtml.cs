using System.Collections.Concurrent;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;
using OpenShelf.Services;

namespace OpenShelf.Pages;

public class ImportModel : PageModel
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settingsService;

    public ImportModel(IServiceScopeFactory scopeFactory, SettingsService settingsService)
    {
        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
    }

    // --- State ---
    
    // We use a static dictionary to track progress across requests.
    // In a real app, use IDistributedCache or Singleton Service.
    public static ConcurrentDictionary<string, ProcessStatus> ProcessTracker = new();

    public class ProcessStatus
    {
        public int Percent { get; set; }
        public string Message { get; set; } = "";
        public bool IsComplete { get; set; }
        public bool IsError { get; set; }
    }

    [BindProperty]
    public IFormFile? File { get; set; }

    [BindProperty]
    public string? Recommender { get; set; }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    public async Task<IActionResult> OnGetAsync() 
    { 
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnablePublicImport && !User.Identity.IsAuthenticated)
        {
            return Forbid();
        }
        return Page();
    }

    // --- Endpoints ---

    public async Task<IActionResult> OnPostStartImportAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnablePublicImport && !User.Identity.IsAuthenticated)
        {
            return new JsonResult(new { success = false, message = "Public import is disabled." });
        }

        if (File == null || File.Length == 0)
        {
            return new JsonResult(new { success = false, message = "Please select a file." });
        }

        var processId = Guid.NewGuid().ToString();
        ProcessTracker.TryAdd(processId, new ProcessStatus { Percent = 0, Message = "Initializing...", IsComplete = false });

        // Buffer file to memory
        using var ms = new MemoryStream();
        await File.CopyToAsync(ms);
        var fileBytes = ms.ToArray();
        
        var recommenderName = !string.IsNullOrWhiteSpace(Recommender) ? Recommender : "CSV Import";

        // Start Background Task
        _ = Task.Run(() => RunImportLogic(processId, fileBytes, recommenderName));

        return new JsonResult(new { success = true, processId = processId });
    }

    public JsonResult OnGetProgress(string processId)
    {
        if (ProcessTracker.TryGetValue(processId, out var status))
        {
            return new JsonResult(status);
        }
        return new JsonResult(new { percent = 0, message = "Unknown process", isError = true });
    }

    // --- Background Logic ---

    private async Task RunImportLogic(string processId, byte[] fileBytes, string recommenderName)
    {
        try
        {
            UpdateStatus(processId, 5, "Reading CSV...");

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var googleService = scope.ServiceProvider.GetRequiredService<GoogleBooksService>();
            var audibleService = scope.ServiceProvider.GetRequiredService<AudibleService>();

            using var ms = new MemoryStream(fileBytes);
            using var reader = new StreamReader(ms);
            
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch = args => args.Header.ToLower().Replace(".", "").Replace(" ", ""),
                MissingFieldFound = null,
                HeaderValidated = null,
            };

            using var csv = new CsvReader(reader, config);
            var records = csv.GetRecords<CsvBookImport>().ToList();

            if (!records.Any())
            {
                UpdateStatus(processId, 100, "No records found.", isError: true);
                return;
            }

            UpdateStatus(processId, 10, $"Analysing {records.Count} records...");

            // 1. Fetch Existing Data (Refined Query Logic)
            var seriesNames = records.Where(r => !string.IsNullOrEmpty(r.SeriesName)).Select(r => r.SeriesName!).Distinct().ToList();
            var standaloneTitles = records.Where(r => string.IsNullOrEmpty(r.SeriesName) && !string.IsNullOrEmpty(r.Title)).Select(r => r.Title!).Distinct().ToList();

            var potentialSeriesRecs = await context.Recommendations
                .Include(r => r.Items)
                .Where(r => seriesNames.Contains(r.Title))
                .ToListAsync();
            var existingSeriesRecs = potentialSeriesRecs.Where(r => r.IsSeries).ToList(); // IsSeries filter in memory

            var potentialStandaloneRecs = await context.Recommendations
                .Include(r => r.Items)
                .Where(r => standaloneTitles.Contains(r.Title))
                .ToListAsync();
            var existingStandaloneRecs = potentialStandaloneRecs.Where(r => !r.IsSeries).ToList();

            UpdateStatus(processId, 20, "Importing to Database...");

            // 2. Import Logic
            // ... (Same Logic but track touched items)
            var touchedItems = new List<RecommendationItem>();

            // Series
            var seriesGroups = records.Where(r => !string.IsNullOrEmpty(r.SeriesName)).GroupBy(r => r.SeriesName!).ToList();
            int current = 0;
            int total = records.Count;

            foreach (var group in seriesGroups)
            {
                var seriesName = group.Key;
                var rec = existingSeriesRecs.FirstOrDefault(r => r.Title.Equals(seriesName, StringComparison.OrdinalIgnoreCase));

                if (rec == null)
                {
                    rec = new Recommendation { Title = seriesName, RecommendedBy = recommenderName, AddedAt = DateTime.UtcNow, SeriesDescription = $"Imported Series: {seriesName}", Items = new List<RecommendationItem>() };
                    context.Recommendations.Add(rec);
                    existingSeriesRecs.Add(rec);
                }

                foreach (var row in group)
                {
                    var existingItem = rec.Items.FirstOrDefault(i => i.Title.Equals(row.Title, StringComparison.OrdinalIgnoreCase));
                    if (existingItem != null)
                    {
                        EnrichItem(existingItem, row);
                        touchedItems.Add(existingItem);
                    }
                    else
                    {
                        var newItem = MapToItem(row, 0);
                        rec.Items.Add(newItem);
                        touchedItems.Add(newItem);
                    }
                    current++;
                }

                // Normalize order
                 var sorted = rec.Items
                    .OrderBy(i => i.SeriesOrder ?? 999) 
                    .ThenBy(i => ParseSequence(i.SeriesSequence))
                    .ToList();
                for(int i=0; i<sorted.Count; i++) sorted[i].SeriesOrder = i + 1;
            }

            // Standalone
            var standalones = records.Where(r => string.IsNullOrEmpty(r.SeriesName)).ToList();
            foreach (var row in standalones)
            {
                var title = row.Title ?? "Unknown";
                var rec = existingStandaloneRecs.FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

                if (rec == null)
                {
                    rec = new Recommendation { Title = title, RecommendedBy = recommenderName, AddedAt = DateTime.UtcNow, Note = row.Summary, Items = new List<RecommendationItem> { MapToItem(row, 0) } };
                    context.Recommendations.Add(rec);
                    existingStandaloneRecs.Add(rec);
                    touchedItems.Add(rec.Items.First());
                }
                else
                {
                    var item = rec.Items.FirstOrDefault();
                    if (item != null)
                    {
                        EnrichItem(item, row);
                        touchedItems.Add(item);
                    }
                }
                current++;
            }

            await context.SaveChangesAsync();
            UpdateStatus(processId, 50, "Data Imported. Fetching Metadata...");

            // 3. Metadata Fetch
            int processedMeta = 0;
            int totalMeta = touchedItems.Count;
            
            foreach (var item in touchedItems)
            {
                // Update Progress frequently
                processedMeta++;
                int progress = 50 + (int)((double)processedMeta / totalMeta * 50); // Scale 50-100%
                UpdateStatus(processId, progress, $"Fetching Metadata: {item.Title} ({processedMeta}/{totalMeta})");

                try 
                {
                    // Google Books
                    var q = $"{item.Title} {item.Authors}";
                    var books = await googleService.SearchBooksAsync(q);
                    var match = books.FirstOrDefault();
                    if (match != null)
                    {
                        if (!string.IsNullOrEmpty(match.ThumbnailUrl)) item.ThumbnailUrl = match.ThumbnailUrl;
                        if (string.IsNullOrEmpty(item.Description)) item.Description = match.Description;
                        if (string.IsNullOrEmpty(item.Publisher)) item.Publisher = match.Publisher;
                        if (string.IsNullOrEmpty(item.PublishedDate)) item.PublishedDate = match.PublishedDate;
                        if (!item.PageCount.HasValue) item.PageCount = match.PageCount;
                        if (string.IsNullOrEmpty(item.Categories)) item.Categories = match.Categories;
                    }
                } catch { }

                try 
                {
                    // Audible
                    if (string.IsNullOrEmpty(item.Narrator) || string.IsNullOrEmpty(item.ListeningLength))
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
                } catch {}
            }

            await context.SaveChangesAsync();
            UpdateStatus(processId, 100, $"Done! Imported {total} books.", isComplete: true);

        }
        catch (Exception ex)
        {
            UpdateStatus(processId, 0, $"Error: {ex.Message}", isError: true);
        }
    }

    private void UpdateStatus(string id, int percent, string msg, bool isComplete = false, bool isError = false)
    {
        if (ProcessTracker.TryGetValue(id, out var status))
        {
            status.Percent = percent;
            status.Message = msg;
            status.IsComplete = isComplete;
            status.IsError = isError;
        }
    }

    // --- Helpers ---

    private void EnrichItem(RecommendationItem item, CsvBookImport row)
    {
        if (string.IsNullOrEmpty(item.Narrator)) item.Narrator = row.NarratedBy;
        if (string.IsNullOrEmpty(item.ListeningLength)) item.ListeningLength = row.Duration;
        if (string.IsNullOrEmpty(item.Description) && !string.IsNullOrEmpty(row.Description)) item.Description = CleanString(row.Description);
        else if (string.IsNullOrEmpty(item.Description) && !string.IsNullOrEmpty(row.Summary)) item.Description = CleanString(row.Summary);
        
        if (string.IsNullOrEmpty(item.Categories)) item.Categories = row.Genre;
        if (string.IsNullOrEmpty(item.Publisher)) item.Publisher = row.Publisher;
        if (string.IsNullOrEmpty(item.PublishedDate)) item.PublishedDate = row.ReleaseDate;

        if (string.IsNullOrEmpty(item.PurchaseDate)) item.PurchaseDate = row.PurchaseDate;
        if (string.IsNullOrEmpty(item.ReleaseDate)) item.ReleaseDate = row.ReleaseDate;
        if (string.IsNullOrEmpty(item.AverageRating)) item.AverageRating = row.AveRating;
        if (string.IsNullOrEmpty(item.RatingCount)) item.RatingCount = row.RatingCount;
        if (string.IsNullOrEmpty(item.SeriesName)) item.SeriesName = row.SeriesName;
        if (string.IsNullOrEmpty(item.SeriesSequence)) item.SeriesSequence = row.SeriesSequence;
        if (string.IsNullOrEmpty(item.ProductId)) item.ProductId = row.ProductId;
        if (string.IsNullOrEmpty(item.Asin)) item.Asin = row.Asin;
        if (string.IsNullOrEmpty(item.BookUrl)) item.BookUrl = row.BookUrl;
        if (string.IsNullOrEmpty(item.SeriesUrl)) item.SeriesUrl = row.SeriesUrl;
        if (string.IsNullOrEmpty(item.Abridged)) item.Abridged = row.Abridged;
        if (string.IsNullOrEmpty(item.Language)) item.Language = row.Language;
        if (string.IsNullOrEmpty(item.Copyright)) item.Copyright = row.Copyright;
    }

    private RecommendationItem MapToItem(CsvBookImport row, int defaultOrder)
    {
        int order = defaultOrder;
        if (int.TryParse(row.SeriesSequence, out int parsed)) order = parsed;

        return new RecommendationItem
        {
            Title = row.Title ?? "Unknown",
            Authors = row.Author ?? "Unknown",
            Narrator = row.NarratedBy,
            Description = !string.IsNullOrEmpty(row.Description) ? CleanString(row.Description) : CleanString(row.Summary),
            Categories = row.Genre,
            Publisher = row.Publisher,
            PublishedDate = row.ReleaseDate,
            ListeningLength = row.Duration,
            PurchaseDate = row.PurchaseDate,
            ReleaseDate = row.ReleaseDate,
            AverageRating = row.AveRating,
            RatingCount = row.RatingCount,
            SeriesName = row.SeriesName,
            SeriesSequence = row.SeriesSequence,
            ProductId = row.ProductId,
            Asin = row.Asin,
            BookUrl = row.BookUrl,
            SeriesUrl = row.SeriesUrl,
            Abridged = row.Abridged,
            Language = row.Language,
            Copyright = row.Copyright,
            SeriesOrder = order
        };
    }

    private double ParseSequence(string? seq)
    {
        if (double.TryParse(seq, out double val)) return val;
        return 9999;
    }

    private string? CleanString(string? val)
    {
        if (string.IsNullOrEmpty(val)) return null;
        return val.Replace("<p>", "").Replace("</p>", "").Trim();
    }

    public class CsvBookImport
    {
        [Name("Title")] public string? Title { get; set; }
        [Name("Author")] public string? Author { get; set; }
        [Name("Narrated By")] public string? NarratedBy { get; set; }
        [Name("Purchase Date")] public string? PurchaseDate { get; set; }
        [Name("Duration")] public string? Duration { get; set; }
        [Name("Release Date")] public string? ReleaseDate { get; set; }
        [Name("Ave. Rating")] public string? AveRating { get; set; }
        [Name("Genre")] public string? Genre { get; set; }
        [Name("Series Name")] public string? SeriesName { get; set; }
        [Name("Series Sequence")] public string? SeriesSequence { get; set; }
        [Name("Product ID")] public string? ProductId { get; set; }
        [Name("ASIN")] public string? Asin { get; set; }
        [Name("Book URL")] public string? BookUrl { get; set; }
        [Name("Summary")] public string? Summary { get; set; }
        [Name("Description")] public string? Description { get; set; }
        [Name("Rating Count")] public string? RatingCount { get; set; }
        [Name("Publisher")] public string? Publisher { get; set; }
        [Name("Copyright")] public string? Copyright { get; set; }
        [Name("Series URL")] public string? SeriesUrl { get; set; }
        [Name("Abridged")] public string? Abridged { get; set; }
        [Name("Language")] public string? Language { get; set; }
    }
}
