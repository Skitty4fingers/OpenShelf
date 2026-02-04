using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;

namespace OpenShelf.Pages;

public class EditModel : PageModel
{
    private readonly AppDbContext _context;

    public EditModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public Recommendation Recommendation { get; set; } = default!;

    [BindProperty]
    public RecommendationItem? FirstItem { get; set; }

    [BindProperty]
    public string Format { get; set; } = "standalone"; // "series", "audiobook", "standalone"

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var rec = await _context.Recommendations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (rec == null) return NotFound();

        Recommendation = rec;

        if (rec.IsSeries)
        {
            Format = "series";
        }
        else
        {
            FirstItem = rec.Items.FirstOrDefault();
            // Try to detect format from item
            if (FirstItem != null && (!string.IsNullOrEmpty(FirstItem.Narrator) || !string.IsNullOrEmpty(FirstItem.ListeningLength)))
            {
                Format = "audiobook";
            }
            else
            {
                Format = "standalone";
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Remove validation errors for FirstItem.Recommendation since it's not bound
        ModelState.Remove("FirstItem.Recommendation");
        ModelState.Remove("FirstItem.RecommendationId");
        
        if (!ModelState.IsValid)
        {
            // Log validation errors for debugging
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            foreach (var error in errors)
            {
                Console.WriteLine($"Validation Error: {error}");
            }
            
            // Reload the data for the page
            var reloadRec = await _context.Recommendations
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == Recommendation.Id);
            
            if (reloadRec != null)
            {
                Recommendation = reloadRec;
                if (!reloadRec.IsSeries)
                {
                    FirstItem = reloadRec.Items.FirstOrDefault();
                }
            }
            
            return Page();
        }

        var recToUpdate = await _context.Recommendations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == Recommendation.Id);

        if (recToUpdate == null) return NotFound();

        // Update High Level
        recToUpdate.Title = Recommendation.Title;
        recToUpdate.RecommendedBy = Recommendation.RecommendedBy;
        recToUpdate.Note = Recommendation.Note;
        recToUpdate.SeriesDescription = Recommendation.SeriesDescription;

        // Update Items logic
        if (recToUpdate.IsSeries)
        {
            // Series mode: Just update high level details for now.
            // Full item editing inside series is complex for a simple edit page.
            // Just saving Recommendation fields is a good start.
        }
        else
        {
            // Update single item
            var itemToUpdate = recToUpdate.Items.FirstOrDefault();
            if (itemToUpdate != null && FirstItem != null)
            {
                // Only update if values are provided
                if (!string.IsNullOrEmpty(FirstItem.Title))
                    itemToUpdate.Title = FirstItem.Title;
                if (!string.IsNullOrEmpty(FirstItem.Authors))
                    itemToUpdate.Authors = FirstItem.Authors;
                
                itemToUpdate.Description = FirstItem.Description;
                itemToUpdate.Publisher = FirstItem.Publisher;
                itemToUpdate.PublishedDate = FirstItem.PublishedDate;
                itemToUpdate.PageCount = FirstItem.PageCount;
                itemToUpdate.Categories = FirstItem.Categories;
                
                // Audio
                itemToUpdate.Narrator = FirstItem.Narrator;
                itemToUpdate.ListeningLength = FirstItem.ListeningLength;
                
                // URLs
                itemToUpdate.ThumbnailUrl = FirstItem.ThumbnailUrl;
                itemToUpdate.BookUrl = FirstItem.BookUrl;
            }
        }

        await _context.SaveChangesAsync();

        return RedirectToPage("./Details", new { id = recToUpdate.Id });
    }
}
