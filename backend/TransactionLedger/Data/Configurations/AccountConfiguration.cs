using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionLedger.Domain;

namespace TransactionLedger.Data.Configurations;

/// <summary>
/// Accounts table per docs/04-database-design.md §3.2.
///
/// UX_Accounts_User_NameLower is deliberately absent here: it is a FUNCTIONAL
/// unique index on (UserId, LOWER(Name)) and EF Core's fluent API cannot
/// express one, so it is created with raw SQL in the migration (§3.2).
/// </summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", table =>
            // Belt and braces for BR-20: if a future code path ever forgets
            // the service-level overdraft check, the database refuses the
            // write outright.
            table.HasCheckConstraint("CK_Accounts_BalanceNonNegative", "\"Balance\" >= 0"));

        builder.HasKey(a => a.Id)
            .HasName("PK_Accounts");

        builder.Property(a => a.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(a => a.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.Name)
            .HasMaxLength(100)
            .IsRequired();

        // Stored as integer, not a PostgreSQL enum type (§1.4).
        builder.Property(a => a.Type)
            .HasConversion<int>()
            .IsRequired();

        // BR-02: money is numeric(18,2). Never float or double.
        builder.Property(a => a.Balance)
            .HasColumnType("numeric(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        // No navigation property: nothing in this scope needs to traverse
        // from an account back to its user, and ownership is always a
        // predicate on UserId (BR-07).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .HasConstraintName("FK_Accounts_Users")
            // RESTRICT, not CASCADE: a cascade would let one deleted user
            // silently erase a financial history (§3.2).
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("IX_Accounts_UserId");
    }
}
