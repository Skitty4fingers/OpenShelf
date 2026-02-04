using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;

namespace OpenShelf.Pages;

public class AdminLoginModel : PageModel
{
    private readonly AppDbContext _context;

    public AdminLoginModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? WarningMessage { get; set; }

    public async Task OnGetAsync()
    {
        // Ensure default admin user exists
        await EnsureDefaultAdminAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter both username and password.";
            return Page();
        }

        // Find user by username (case-insensitive)
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == Username.ToLower());

        if (user == null)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        // Verify password using BCrypt
        if (!BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash))
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        // Update last login time
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Create authentication claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("Role", "Admin")
        };

        var claimsIdentity = new ClaimsIdentity(claims, "CookieAuth");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true
        };

        await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(claimsIdentity), authProperties);

        // Check if using default password
        if (user.Username.ToLower() == "admin" && BCrypt.Net.BCrypt.Verify("admin", user.PasswordHash))
        {
            TempData["Warning"] = "⚠️ You are using the default password. Please change it immediately in Admin > Manage Users!";
        }

        if (!string.IsNullOrEmpty(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        
        return RedirectToPage("/Admin/Index");
    }

    private async Task EnsureDefaultAdminAsync()
    {
        // Only create admin user if it doesn't exist - don't reset password on every visit
        var adminExists = await _context.Users.AnyAsync(u => u.Username.ToLower() == "admin");

        if (!adminExists)
        {
            // Create default admin user
            var adminUser = new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();
        }
    }
}
