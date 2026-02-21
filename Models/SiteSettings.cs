using System.ComponentModel.DataAnnotations;

namespace OpenShelf.Models;

public class SiteSettings
{
    [Key]
    public int Id { get; set; } = 1;

    public string? GoogleBooksApiKey { get; set; }

    public bool EnableGoogleBooks { get; set; } = true;
    public bool EnableOpenLibrary { get; set; } = true;
    public bool EnableAudible { get; set; } = true;
    public bool EnableGoodreads { get; set; } = true;

    public bool EnableChat { get; set; } = true;
    public bool EnablePublicImport { get; set; } = true;
    public bool EnablePublicMetadataRefresh { get; set; } = true;
    public bool EnableGetThisBookLinks { get; set; } = true;

    // Authentication Providers
    public bool EnableGoogleAuth { get; set; } = false;
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }
}
