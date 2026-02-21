using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenShelf.Models;
using OpenShelf.Services;
using System.Threading.Tasks;

namespace OpenShelf.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class SettingsModel : PageModel
{
    private readonly SettingsService _settingsService;

    public SettingsModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [BindProperty]
    public SiteSettings Settings { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        Settings = await _settingsService.GetSettingsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _settingsService.UpdateSettingsAsync(Settings);
        
        TempData["SuccessMessage"] = "Site settings have been updated successfully.";
        
        return RedirectToPage();
    }
}
