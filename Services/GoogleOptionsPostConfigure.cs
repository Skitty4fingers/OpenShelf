using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.Options;

namespace OpenShelf.Services;

/// <summary>
/// Dynamically configures Google OAuth options from database settings at challenge time.
/// This allows the Client ID/Secret to be managed through the Admin UI instead of appsettings.json.
/// </summary>
public class GoogleOptionsPostConfigure : IPostConfigureOptions<GoogleOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public GoogleOptionsPostConfigure(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void PostConfigure(string? name, GoogleOptions options)
    {
        if (name != "Google") return;

        using var scope = _serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var settings = settingsService.GetSettingsAsync().GetAwaiter().GetResult();

        if (settings.EnableGoogleAuth
            && !string.IsNullOrEmpty(settings.GoogleClientId)
            && !string.IsNullOrEmpty(settings.GoogleClientSecret))
        {
            options.ClientId = settings.GoogleClientId;
            options.ClientSecret = settings.GoogleClientSecret;
        }
    }
}
