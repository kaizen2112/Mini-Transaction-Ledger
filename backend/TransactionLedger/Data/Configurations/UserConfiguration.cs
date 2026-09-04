using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionLedger.Domain;

namespace TransactionLedger.Data.Configurations;

/// <summary>
/// Users table per docs/04-database-design.md §3.1. UX_Users_Email is the only
/// thing that actually guarantees email uniqueness — the service attempts the
/// insert and translates the violation rather than pre-checking (BR-11).
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id)
            .HasName("PK_Users");

        builder.Property(u => u.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(u => u.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("UX_Users_Email");
    }
}
