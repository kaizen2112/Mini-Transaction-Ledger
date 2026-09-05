using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionLedger.Domain;

namespace TransactionLedger.Data.Configurations;

/// <summary>
/// Transfers table per docs/04-database-design.md §3.4.
/// </summary>
public sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("Transfers", table =>
        {
            // BR-27. The service returns 400 SAME_ACCOUNT_TRANSFER long before
            // this fires; the constraint is the backstop that turns a future
            // forgotten check into a loud failure instead of a self-transfer
            // that silently nets to zero while writing two ledger rows.
            table.HasCheckConstraint(
                "CK_Transfers_DifferentAccounts",
                "\"SourceAccountId\" <> \"DestinationAccountId\"");

            // BR-03.
            table.HasCheckConstraint("CK_Transfers_AmountPositive", "\"Amount\" > 0");
        });

        builder.HasKey(t => t.Id)
            .HasName("PK_Transfers");

        builder.Property(t => t.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(t => t.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.SourceAccountId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.DestinationAccountId)
            .HasColumnType("uuid")
            .IsRequired();

        // BR-02: money is numeric(18,2). Never float or double.
        builder.Property(t => t.Amount)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(t => t.DebitTransactionId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.CreditTransactionId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.OccurredAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .HasConstraintName("FK_Transfers_Users")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.SourceAccountId)
            .HasConstraintName("FK_Transfers_SourceAccounts")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.DestinationAccountId)
            .HasConstraintName("FK_Transfers_DestinationAccounts")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(t => t.DebitTransactionId)
            .HasConstraintName("FK_Transfers_DebitTransactions")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(t => t.CreditTransactionId)
            .HasConstraintName("FK_Transfers_CreditTransactions")
            .OnDelete(DeleteBehavior.Restrict);

        // One leg belongs to exactly one transfer. Not merely tidy: without
        // these, a bug could point two Transfers rows at the same debit leg and
        // the money would appear to have moved twice from one ledger entry.
        builder.HasIndex(t => t.DebitTransactionId)
            .IsUnique()
            .HasDatabaseName("UX_Transfers_DebitTx");

        builder.HasIndex(t => t.CreditTransactionId)
            .IsUnique()
            .HasDatabaseName("UX_Transfers_CreditTx");

        // GET /api/transfers filters on UserId (BR-07), which is exactly why
        // the column is denormalised onto this table.
        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("IX_Transfers_UserId");
    }
}
