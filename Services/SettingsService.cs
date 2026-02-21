using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;

namespace OpenShelf.Services;

public class SettingsService
{
    private readonly AppDbContext _context;

    public SettingsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SiteSettings> GetSettingsAsync()
    {
        var settings = await _context.SiteSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new SiteSettings { Id = 1 };
            _context.SiteSettings.Add(settings);
            await _context.SaveChangesAsync();
        }
        return settings;
    }

    public async Task UpdateSettingsAsync(SiteSettings newSettings)
    {
        var settings = await GetSettingsAsync();

        settings.GoogleBooksApiKey = newSettings.GoogleBooksApiKey;
        settings.EnableGoogleBooks = newSettings.EnableGoogleBooks;
        settings.EnableOpenLibrary = newSettings.EnableOpenLibrary;
        settings.EnableAudible = newSettings.EnableAudible;
        settings.EnableGoodreads = newSettings.EnableGoodreads;
        
        settings.EnableChat = newSettings.EnableChat;
        settings.EnablePublicImport = newSettings.EnablePublicImport;
        settings.EnablePublicMetadataRefresh = newSettings.EnablePublicMetadataRefresh;
        settings.EnableGetThisBookLinks = newSettings.EnableGetThisBookLinks;

        // Authentication
        settings.EnableGoogleAuth = newSettings.EnableGoogleAuth;
        settings.GoogleClientId = newSettings.GoogleClientId;
        settings.GoogleClientSecret = newSettings.GoogleClientSecret;
        settings.RequireLogin = newSettings.RequireLogin;

        await _context.SaveChangesAsync();
    }
}
