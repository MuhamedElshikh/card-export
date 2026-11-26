using IdCardReader.Domain;
using Microsoft.EntityFrameworkCore;

namespace IdCardReader.Infrastructure;

/// <summary>EF Core context for stored ID cards (SQL Server).</summary>
public class IdCardReaderDbContext : DbContext
{
    /// <summary>Creates the context.</summary>
    public IdCardReaderDbContext(DbContextOptions<IdCardReaderDbContext> options)
        : base(options)
    {
    }

    /// <summary>Stored ID cards.</summary>
    public DbSet<IdCard> IdCards => Set<IdCard>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IdCard>(entity =>
        {
            entity.ToTable("IdCards");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.NationalId)
                .IsRequired()
                .HasMaxLength(14)
                .IsUnicode(false);

            entity.HasIndex(e => e.NationalId)
                .IsUnique()
                .HasDatabaseName("IX_IdCards_NationalId");

            entity.Property(e => e.FullName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Address)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.Occupation)
                .HasMaxLength(200);

            entity.Property(e => e.MaritalStatus)
                .HasMaxLength(100);

            entity.Property(e => e.GovernorateCode)
                .IsRequired()
                .HasMaxLength(2);

            entity.Property(e => e.GovernorateEnglish)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.GovernorateArabic)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.ImageReference)
                .HasMaxLength(400);

            entity.Property(e => e.CreatedAt)
                .IsRequired();
        });
    }
}
