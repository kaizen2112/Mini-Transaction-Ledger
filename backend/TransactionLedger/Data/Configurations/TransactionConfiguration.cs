using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionLedger.Domain;

namespace TransactionLedger.Data.Configurations;

/// <summary>
/// Transactions table per docs/04-database-design.md §3.3.
///
/// Unlike UX_Accounts_User_NameLower, every index here IS expressible in the
/// fluent API: partial indexes map to HasFilter and descending columns to
/// IsDescending, so none of this needs raw SQL.
/// </summary>
public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            // BR-03: the amount is a magnitude; direction lives in Type.
            table.HasCheckConstraint("CK_Transactions_AmountPositive", "\"Amount\" > 0");

            // BR-04: a ceiling well below what numeric(18,2) allows, so no
            // aggregate over these rows can overflow the column.
            table.HasCheckConstraint("CK_Transactions_AmountMax", "\"Amount\" <= 1000000000");

            // Sanity: a row may not reverse itself.
            table.HasCheckConstraint(
                "CK_Transactions_NotSelfReversing",
                "\"ReversesTransactionId\" <> \"Id\"");
        });

        builder.HasKey(t => t.Id)
            .HasName("PK_Transactions");

        builder.Property(t => t.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(t => t.AccountId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.Type)
            .HasConversion<int>()
            .IsRequired();

        // BR-02: money is numeric(18,2). Never float or double.
        builder.Property(t => t.Amount)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(t => t.Category)
            .HasConversion<int>()
            .IsRequired();

        // BR-25: optional, max 500, stored as-is and never interpreted as markup.
        builder.Property(t => t.Description)
            .HasMaxLength(Transaction.MaxDescriptionLength);

        builder.Property(t => t.OccurredAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.ReversesTransactionId)
            .HasColumnType("uuid");

        builder.Property(t => t.TransferId)
            .HasColumnType("uuid");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .HasConstraintName("FK_Transactions_Accounts")
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing FK: a reversal points back at what it reverses.
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(t => t.ReversesTransactionId)
            .HasConstraintName("FK_Transactions_Reverses")
            .OnDelete(DeleteBehavior.Restrict);

        // B8: the Transfers table now exists, so the constraint deferred in B6
        // is declared here.
        //
        // This closes a CIRCULAR foreign key — Transactions.TransferId points
        // at Transfers, and Transfers.DebitTransactionId points back at
        // Transactions. Neither side can be inserted with both ends satisfied
        // in a single statement, so TransferService writes in three steps
        // (legs, then transfer, then backfill TransferId) inside ONE database
        // transaction. Deferred constraints are unnecessary: nothing is visible
        // to another session until that transaction commits (docs/04 §3.4).
        builder.HasOne<Transfer>()
            .WithMany()
            .HasForeignKey(t => t.TransferId)
            .HasConstraintName("FK_Transactions_Transfers")
            .OnDelete(DeleteBehavior.Restrict);

        // BR-22, the double-reversal guard. PARTIAL, because PostgreSQL treats
        // NULLs as distinct in a unique index anyway and the overwhelming
        // majority of rows reverse nothing — so the filtered index is far
        // smaller for identical semantics.
        builder.HasIndex(t => t.ReversesTransactionId)
            .IsUnique()
            .HasFilter("\"ReversesTransactionId\" IS NOT NULL")
            .HasDatabaseName("UX_Transactions_Reverses");

        // BR-41, the workhorse index. The sort order is baked in, so the
        // default history page is an index scan with no sort node.
        builder.HasIndex(t => new { t.AccountId, t.OccurredAt, t.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Transactions_Account_Occurred");

        // BR-43 category report.
        builder.HasIndex(t => new { t.AccountId, t.Category })
            .HasDatabaseName("IX_Transactions_Account_Category");

        builder.HasIndex(t => t.TransferId)
            .HasFilter("\"TransferId\" IS NOT NULL")
            .HasDatabaseName("IX_Transactions_TransferId");

        // Deliberately absent: an index on Description. ILIKE %term% cannot
        // use a B-tree, so it would be dead weight (BR-42).
    }
}
