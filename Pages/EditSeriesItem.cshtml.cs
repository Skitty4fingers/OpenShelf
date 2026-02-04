using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;

namespace OpenShelf.Pages;

public class EditSeriesItemModel : PageModel
{
    private readonly AppDbContext _context;

    public EditSeriesItemModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public RecommendationItem Item { get; set; } = null!;
    
    public string SeriesTitle { get; set; } = string.Empty;
    public int RecommendationId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var item = await _context.RecommendationItems
            .Include(i => i.Recommendation)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (item == null || item.Recommendation == null)
        {
            return NotFound();
        }

        Item = item;
        SeriesTitle = item.Recommendation.Title;
        RecommendationId = item.Recommendation.Id;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var itemToUpdate = await _context.RecommendationItems
            .Include(i => i.Recommendation)
            .FirstOrDefaultAsync(i => i.Id == Item.Id);

        if (itemToUpdate == null)
        {
            return NotFound();
        }

        // Update fields
        itemToUpdate.Title = Item.Title;
        itemToUpdate.Authors = Item.Authors;
        itemToUpdate.Description = Item.Description;
        itemToUpdate.ThumbnailUrl = Item.ThumbnailUrl;
        itemToUpdate.Categories = Item.Categories;
        itemToUpdate.Publisher = Item.Publisher;
        itemToUpdate.PublishedDate = Item.PublishedDate;
        itemToUpdate.PageCount = Item.PageCount;
        itemToUpdate.Narrator = Item.Narrator;
        itemToUpdate.ListeningLength = Item.ListeningLength;
        itemToUpdate.SeriesSequence = Item.SeriesSequence;

        await _context.SaveChangesAsync();

        return RedirectToPage("./Details", new { id = itemToUpdate.Recommendation.Id });
    }
}
