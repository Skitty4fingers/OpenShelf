using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenShelf.Services;

namespace OpenShelf.Pages;

public class UserLoginModel : PageModel
{
    private readonly SettingsService _settingsService;

    public UserLoginModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool GoogleAuthEnabled { get; set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        var settings = await _settingsService.GetSettingsAsync();
        GoogleAuthEnabled = settings.EnableGoogleAuth
            && !string.IsNullOrEmpty(settings.GoogleClientId)
            && !string.IsNullOrEmpty(settings.GoogleClientSecret);

        if (!GoogleAuthEnabled)
        {
            // No auth providers enabled → redirect home
            return RedirectToPage("/Index");
        }

        // If already signed in via ExternalAuth, redirect home
        if (User.Identities.Any(i => i.AuthenticationType == "ExternalAuth" && i.IsAuthenticated))
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public IActionResult OnGetGoogle(string? returnUrl = null)
    {
        var redirectUrl = Url.Page("/UserLogin", pageHandler: "GoogleCallback", values: new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, "Google");
    }

    public async Task<IActionResult> OnGetGoogleCallbackAsync(string? returnUrl = null)
    {
        // The ExternalAuth cookie is already set by the Google middleware at this point
        // Redirect to the return URL or home
        if (!string.IsNullOrEmpty(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToPage("/Index");
    }
}
