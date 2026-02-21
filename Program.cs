using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Services;
using OpenShelf.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddScoped<GoogleBooksService>();
builder.Services.AddScoped<AudibleService>();
builder.Services.AddScoped<GoodreadsService>();
builder.Services.AddScoped<SettingsService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", options =>
    {
        options.Cookie.Name = "OpenShelf.Auth";
        options.LoginPath = "/Admin/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Allow HTTP in development
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true; // Required for auth to work without consent
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    })
    .AddCookie("ExternalAuth", options =>
    {
        options.Cookie.Name = "OpenShelf.External";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    })
    .AddGoogle("Google", options =>
    {
        options.SignInScheme = "ExternalAuth";
        options.ClientId = "placeholder";
        options.ClientSecret = "placeholder";
        options.CallbackPath = "/signin-google";
    });

// Dynamic Google config from DB
builder.Services.AddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>, OpenShelf.Services.GoogleOptionsPostConfigure>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("Role", "Admin"));
});

// Persist Data Protection Keys (Prevent 400 errors on restart)
// The cookie encryption keys are rotated if not persisted, invalidating all cookies/tokens.
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
if (!Directory.Exists(keysFolder))
{
    Directory.CreateDirectory(keysFolder);
}
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("OpenShelf");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Require Login gate — redirect unauthenticated users to sign-in page
// when RequireLogin is enabled in SiteSettings
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";

    // Always allow: static files, login pages, auth callbacks, admin login
    var allowedPaths = new[] { "/UserLogin", "/UserLogout", "/Admin/Login", "/Admin/", "/signin-google", "/Error" };
    bool isAllowed = path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/images", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/_", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
        || allowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    if (!isAllowed)
    {
        // Check default scheme (CookieAuth — for admin users)
        bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;

        // Also explicitly check ExternalAuth scheme (for Google SSO users)
        // context.User only contains the default scheme, so we must check ExternalAuth separately
        if (!isAuthenticated)
        {
            var externalResult = await context.AuthenticateAsync("ExternalAuth");
            isAuthenticated = externalResult.Succeeded;
        }

        if (!isAuthenticated)
        {
            var settingsSvc = context.RequestServices.GetRequiredService<SettingsService>();
            var settings = await settingsSvc.GetSettingsAsync();
            if (settings.RequireLogin
                && settings.EnableGoogleAuth
                && !string.IsNullOrEmpty(settings.GoogleClientId)
                && !string.IsNullOrEmpty(settings.GoogleClientSecret))
            {
                context.Response.Redirect($"/UserLogin?returnUrl={Uri.EscapeDataString(path)}");
                return;
            }
        }
    }

    await next();
});

app.MapRazorPages();

// Ensure DB is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    
    // Check for CLI command to reset users
    if (args.Contains("reset-users")) 
    {
        Console.WriteLine("⚠️ RESETTING ALL USERS...");
        db.Users.RemoveRange(db.Users);
        db.SaveChanges();
    }

    // Ensure Default Admin Exists (if no users found)
    if (!db.Users.Any())
    {
        Console.WriteLine("Creating default admin user...");
        var admin = new User 
        { 
            Username = "admin", 
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            CreatedAt = DateTime.UtcNow 
        };
        db.Users.Add(admin);
        db.SaveChanges();
        Console.WriteLine("✅ Default admin created: admin / admin");
    }
}

// Configure Proxy Headers (Critical for Docker/Reverse Proxies)
// We must clear the defaults because they might restrict the allowed forwarding network
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    KnownNetworks = { }, // Trust all upstream proxies
    KnownProxies = { }
});



app.Run();
