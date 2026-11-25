using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CarTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<LineItem> LineItems => Set<LineItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<FuelUp> FuelUps => Set<FuelUp>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // SQLite can't compare/sort DateTimeOffset natively; store as a sortable 64-bit value.
        c.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Invite>().HasIndex(i => i.CodeHash).IsUnique();

        b.Entity<Vehicle>(e =>
        {
            e.HasIndex(v => v.OwnerId);
            e.HasOne(v => v.Owner).WithMany().HasForeignKey(v => v.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.Property(v => v.Name).HasMaxLength(100);
        });

        b.Entity<Entry>(e =>
        {
            e.HasIndex(x => new { x.VehicleId, x.Date });
            e.HasOne(x => x.Vehicle).WithMany(v => v.Entries).HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RelatedEntry).WithMany().HasForeignKey(x => x.RelatedEntryId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.LineItems).WithOne().HasForeignKey(l => l.EntryId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tags).WithMany(t => t.Entries).UsingEntity("EntryTags");
            e.HasMany(x => x.Documents).WithMany(d => d.Entries).UsingEntity("EntryDocuments");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Ignore(x => x.Total);
        });

        b.Entity<LineItem>().Ignore(l => l.Total);

        b.Entity<Tag>(e =>
        {
            e.HasIndex(t => new { t.OwnerId, t.Name }).IsUnique();
            e.Property(t => t.Name).HasMaxLength(50);
        });

        b.Entity<Document>(e =>
        {
            e.HasIndex(d => new { d.OwnerId, d.Status });
            e.HasIndex(d => new { d.OwnerId, d.Sha256 });
            e.HasOne<AppUser>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.VehicleId).OnDelete(DeleteBehavior.SetNull);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.FileName).HasMaxLength(200);
        });

        b.Entity<FuelUp>(e =>
        {
            e.HasIndex(f => new { f.VehicleId, f.Date });
            e.HasOne(f => f.Vehicle).WithMany(v => v.FuelUps).HasForeignKey(f => f.VehicleId).OnDelete(DeleteBehavior.Cascade);
            e.Property(f => f.Grade).HasMaxLength(10);
            e.Property(f => f.Station).HasMaxLength(100);
        });

        b.Entity<AiUsage>(e =>
        {
            e.HasIndex(u => new { u.OwnerId, u.Purpose, u.CreatedAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(u => u.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.Property(u => u.Purpose).HasMaxLength(20);
        });

        b.Entity<Photo>(e =>
        {
            e.HasIndex(p => p.VehicleId);
            e.HasOne<Vehicle>().WithMany(v => v.Photos).HasForeignKey(p => p.VehicleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Entry).WithMany(x => x.Photos).HasForeignKey(p => p.EntryId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
