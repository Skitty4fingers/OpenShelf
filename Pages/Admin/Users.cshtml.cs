using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenShelf.Data;
using OpenShelf.Models;
using System.Security.Claims;

namespace OpenShelf.Pages.Admin;

[Authorize]
public class UsersModel : PageModel
{
    private readonly AppDbContext _context;

    public UsersModel(AppDbContext context)
    {
        _context = context;
    }

    public List<User> Users { get; set; } = new();
    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    public async Task OnGetAsync()
    {
        Users = await _context.Users
            .OrderBy(u => u.Username)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAddUserAsync(string newUsername, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newUsername) || string.IsNullOrWhiteSpace(newPassword))
        {
            Message = "Username and password are required.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        // Validate username format
        if (newUsername.Length < 3 || newUsername.Length > 50)
        {
            Message = "Username must be between 3 and 50 characters.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(newUsername, @"^[a-zA-Z0-9_]+$"))
        {
            Message = "Username can only contain letters, numbers, and underscores.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        // Validate password length
        if (newPassword.Length < 8)
        {
            Message = "Password must be at least 8 characters.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        // Check if username already exists (case-insensitive)
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == newUsername.ToLower());

        if (existingUser != null)
        {
            Message = $"Username '{newUsername}' already exists.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        // Create new user
        var newUser = new User
        {
            Username = newUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        Message = $"User '{newUsername}' created successfully!";
        MessageType = "success";
        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int userId, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            Message = "Password is required.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        if (newPassword.Length < 8)
        {
            Message = "Password must be at least 8 characters.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            Message = "User not found.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        // Update password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _context.SaveChangesAsync();

        Message = $"Password reset successfully for user '{user.Username}'.";
        MessageType = "success";
        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(int userId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        // Prevent deleting yourself
        if (currentUserId != null && int.Parse(currentUserId) == userId)
        {
            Message = "You cannot delete your own account.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            Message = "User not found.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        // Ensure at least one user remains
        var userCount = await _context.Users.CountAsync();
        if (userCount <= 1)
        {
            Message = "Cannot delete the last admin user.";
            MessageType = "danger";
            await OnGetAsync();
            return Page();
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        Message = $"User '{user.Username}' deleted successfully.";
        MessageType = "success";
        await OnGetAsync();
        return Page();
    }
}
