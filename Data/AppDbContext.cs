using Microsoft.EntityFrameworkCore;
using OpenShelf.Models;

namespace OpenShelf.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Recommendation> Recommendations { get; set; }
    public DbSet<RecommendationItem> RecommendationItems { get; set; }
    public DbSet<Comment> Comments { get; set; }
    public DbSet<RecommendationLike> RecommendationLikes { get; set; }
    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ensure username is unique
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();
    }
}
