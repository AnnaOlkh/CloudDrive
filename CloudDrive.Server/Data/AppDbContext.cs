using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CloudDrive.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileEntry> Files => Set<FileEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.UserName).IsUnique();
            e.Property(u => u.UserName).HasMaxLength(32);
        });

        b.Entity<Folder>(e =>
        {
            e.Property(f => f.Name).HasMaxLength(255);
            e.HasOne(f => f.Parent).WithMany().HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(f => new { f.OwnerId, f.ParentId, f.Name }).IsUnique().HasFilter("\"ParentId\" IS NOT NULL");
            e.HasIndex(f => f.OwnerId).IsUnique().HasFilter("\"ParentId\" IS NULL");
        });

        b.Entity<FileEntry>(e =>
        {
            e.Property(f => f.Name).HasMaxLength(255);
            e.Property(f => f.Version).IsConcurrencyToken();
            e.HasIndex(f => new { f.FolderId, f.Name }).IsUnique();
            e.HasOne(f => f.Folder).WithMany().HasForeignKey(f => f.FolderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.UploadedBy).WithMany().HasForeignKey(f => f.UploadedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.LastEditedBy).WithMany().HasForeignKey(f => f.LastEditedById).OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
