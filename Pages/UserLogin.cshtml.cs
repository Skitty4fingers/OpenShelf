using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using OpenShelf.Services;

namespace OpenShelf.Pages;

public class UserLoginModel : PageModel
{
    private readonly SettingsService _settingsService;
    private readonly IOptionsMonitorCache<GoogleOptions> _optionsCache;
    private readonly IOptionsMonitor<GoogleOptions> _optionsMonitor;

    public UserLoginModel(
        SettingsService settingsService,
        IOptionsMonitorCache<GoogleOptions> optionsCache,
        IOptionsMonitor<GoogleOptions> optionsMonitor)
    {
        _settingsService = settingsService;
        _optionsCache = optionsCache;
        _optionsMonitor = optionsMonitor;
    }

    public bool GoogleAuthEnabled { get; set; }
    public bool RequireLogin { get; set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        var settings = await _settingsService.GetSettingsAsync();
        GoogleAuthEnabled = settings.EnableGoogleAuth
            && !string.IsNullOrEmpty(settings.GoogleClientId)
            && !string.IsNullOrEmpty(settings.GoogleClientSecret);
        RequireLogin = settings.RequireLogin;

        if (!GoogleAuthEnabled)
        {
            return RedirectToPage("/Index");
        }

        if (User.Identities.Any(i => i.AuthenticationType == "ExternalAuth" && i.IsAuthenticated))
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnGetGoogleAsync(string? returnUrl = null)
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!settings.EnableGoogleAuth
            || string.IsNullOrEmpty(settings.GoogleClientId)
            || string.IsNullOrEmpty(settings.GoogleClientSecret))
        {
            return RedirectToPage("/Index");
        }

        // Clear cached options and directly set credentials from DB
        _optionsCache.TryRemove("Google");
        var googleOptions = _optionsMonitor.Get("Google");
        googleOptions.ClientId = settings.GoogleClientId;
        googleOptions.ClientSecret = settings.GoogleClientSecret;

        var redirectUrl = Url.Page("/UserLogin", pageHandler: "GoogleCallback", values: new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, "Google");
    }

    public async Task<IActionResult> OnGetGoogleCallbackAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToPage("/Index");
    }
}

