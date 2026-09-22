using Acentra.Models;
using Microsoft.EntityFrameworkCore;

namespace Acentra.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique index on Resource(Name)
        modelBuilder.Entity<Resource>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.Name).IsUnique();
            entity.Property(r => r.Name).IsRequired().HasMaxLength(150);
            entity.Property(r => r.Type).IsRequired().HasMaxLength(100);
            entity.Property(r => r.Location).IsRequired().HasMaxLength(150);
            entity.Property(r => r.Active).HasDefaultValue(true);
        });

        // Booking configuration
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.UserName).IsRequired().HasMaxLength(150);
            entity.Property(b => b.UserEmail).IsRequired().HasMaxLength(150);
            entity.Property(b => b.Status).IsRequired().HasMaxLength(50);
            
            // Concurrency token configured as requested:
            entity.Property(b => b.Version).IsConcurrencyToken();

            // Private OwnerToken used for cancellation authorization
            entity.Property(b => b.OwnerToken).IsRequired();

            // Index on Booking(ResourceId, StartUtc, EndUtc)
            entity.HasIndex(b => new { b.ResourceId, b.StartUtc, b.EndUtc });

            entity.HasOne(b => b.Resource)
                  .WithMany(r => r.Bookings)
                  .HasForeignKey(b => b.ResourceId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
