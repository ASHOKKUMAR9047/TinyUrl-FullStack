using Microsoft.EntityFrameworkCore;
using TinyUrl.Api.Models;

namespace TinyUrl.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<ShortUrl> ShortUrls { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Code as Primary Key for O(1) lookups
            modelBuilder.Entity<ShortUrl>()
                .HasKey(s => s.Code);

            modelBuilder.Entity<ShortUrl>()
                .Property(s => s.Code)
                .HasMaxLength(6);

            modelBuilder.Entity<ShortUrl>()
                .Property(s => s.TotalClicks)
                .HasDefaultValue(0);

            modelBuilder.Entity<ShortUrl>()
                .Property(s => s.IsPrivate)
                .HasDefaultValue(false);

            modelBuilder.Entity<ShortUrl>()
                .Property(s => s.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        }
    }
}
